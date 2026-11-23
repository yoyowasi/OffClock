using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace OffClock
{
    internal sealed class ReleaseUpdate
    {
        private const string Repository = "github.com/yoyowasi/OffClock";
        private const string Asset = "OffClock.zip";
        private const string Exe = "OffClock.exe";
        private const int MaxFileSize = 16 * 1024 * 1024;
        private const string TempPrefix = "OffClock-update-";
        public readonly string Tag;
        public readonly string Digest;
        public readonly Version Version;
        public readonly string Notes;

        private ReleaseUpdate(string tag, string digest, Version version, string notes)
        {
            Tag = tag;
            Digest = digest;
            Version = version;
            Notes = notes;
        }

        public static ReleaseUpdate Check()
        {
            string response = RunGh("api --hostname github.com repos/yoyowasi/OffClock/releases/latest");
            return FromJson(response, typeof(ReleaseUpdate).Assembly.GetName().Version);
        }

        internal static ReleaseUpdate FromJson(string response, Version installed)
        {
            var data = new JavaScriptSerializer().Deserialize<ReleaseData>(response);
            Version version;
            if (data == null || data.draft || data.prerelease || data.tag_name == null ||
                !Regex.IsMatch(data.tag_name, @"\Av[0-9]{1,8}\.[0-9]{1,8}\.[0-9]{1,8}\z") ||
                !Version.TryParse(data.tag_name.Substring(1) + ".0", out version) || version <= installed)
                return null;

            foreach (ReleaseAsset asset in data.assets ?? new ReleaseAsset[0])
            {
                if (asset.name == Asset && asset.digest != null &&
                    Regex.IsMatch(asset.digest, @"\Asha256:[0-9a-fA-F]{64}\z"))
                    return new ReleaseUpdate(data.tag_name, asset.digest.Substring(7), version, data.body);
            }
            throw new InvalidDataException("릴리스에서 설치 파일 또는 검증 정보를 찾을 수 없습니다.");
        }

        public string Download()
        {
            string id = Guid.NewGuid().ToString("N");
            string folder = TempFolder(id);
            Directory.CreateDirectory(folder);
            try
            {
                RunGh("release download " + Tag + " --repo " + Repository +
                    " --pattern " + Asset + " --dir " + Quote(folder));
                string archivePath = Path.Combine(folder, Asset);
                using (var input = File.OpenRead(archivePath))
                {
                    if (input.Length > MaxFileSize) throw new InvalidDataException("업데이트 파일이 너무 큽니다.");
                    using (var sha = SHA256.Create())
                    {
                        string actual = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "");
                        if (!string.Equals(actual, Digest, StringComparison.OrdinalIgnoreCase))
                            throw new InvalidDataException("업데이트 파일의 검증값이 일치하지 않습니다.");
                    }
                    input.Position = 0;
                    using (var zip = new ZipArchive(input))
                    {
                        if (zip.Entries.Count != 1 || zip.Entries[0].FullName != Exe || zip.Entries[0].Length > MaxFileSize)
                            throw new InvalidDataException("업데이트 압축 파일 구성이 올바르지 않습니다.");
                        zip.Entries[0].ExtractToFile(Path.Combine(folder, Exe));
                    }
                }
                File.Delete(archivePath);
                if (ExecutableVersion(Path.Combine(folder, Exe)) != Version)
                    throw new InvalidDataException("릴리스와 실행 파일의 버전이 일치하지 않습니다.");
                return id;
            }
            catch
            {
                DeleteStage(id);
                throw;
            }
        }

        public static void StartInstaller(string id)
        {
            using (var current = Process.GetCurrentProcess())
            using (Process.Start(new ProcessStartInfo(Path.Combine(TempFolder(id), Exe),
                "--install-update " + current.Id.ToString(CultureInfo.InvariantCulture) + " " + id +
                " " + Quote(Application.ExecutablePath)) { UseShellExecute = false }))
            {
            }
        }

        public static int Install(string[] args)
        {
            int processId;
            Guid stageId;
            if (args.Length != 4 || !int.TryParse(args[1], out processId) || processId <= 0 ||
                !Guid.TryParseExact(args[2], "N", out stageId) || !Path.IsPathRooted(args[3]))
                return 1;

            string id = stageId.ToString("N");
            string source = Application.ExecutablePath;
            string target = Path.GetFullPath(args[3]);
            if (!string.Equals(source, Path.Combine(TempFolder(id), Exe), StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetExtension(target), ".exe", StringComparison.OrdinalIgnoreCase))
                return 1;

            string staged = target + "." + id + ".new";
            string backup = target + "." + id + ".bak";
            bool oldExited = false;
            bool replaced = false;
            try
            {
                if (ExecutableVersion(source) <= ExecutableVersion(target)) return 1;
                try
                {
                    using (var previous = Process.GetProcessById(processId))
                        if (!previous.WaitForExit(10000)) return 1;
                }
                catch (ArgumentException) { }
                oldExited = true;
                File.Copy(source, staged);
                File.Replace(staged, target, backup);
                replaced = true;
                using (var restarted = Launch(target, "--clean-update " + id + " --show-release-notes"))
                    if (restarted.WaitForExit(1200))
                        throw new InvalidOperationException("The updated app exited during startup.");
                TryDelete(backup);
                return 0;
            }
            catch (Exception ex)
            {
                if (!(ex is IOException || ex is UnauthorizedAccessException || ex is Win32Exception ||
                    ex is InvalidOperationException || ex is BadImageFormatException || ex is InvalidDataException))
                    throw;
                if (oldExited)
                {
                    if (replaced) File.Replace(backup, target, null);
                    using (Launch(target, "--clean-update " + id)) { }
                }
                return 1;
            }
            finally { TryDelete(staged); }
        }

        internal static Version ExecutableVersion(string exe)
        {
            AssemblyName identity = AssemblyName.GetAssemblyName(exe);
            if (identity.Name != "OffClock") throw new InvalidDataException("퇴근시계 실행 파일이 아닙니다.");
            return identity.Version;
        }

        internal static string InstalledNotes()
        {
            using (var stream = typeof(ReleaseUpdate).Assembly.GetManifestResourceStream("OffClock.ReleaseNotes"))
            {
                if (stream == null) return null;
                using (var reader = new StreamReader(stream)) return reader.ReadToEnd();
            }
        }

        public static void CleanAfterInstall(string id)
        {
            Guid parsed;
            if (!Guid.TryParseExact(id, "N", out parsed)) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                for (int i = 0; i < 20 && !DeleteStage(parsed.ToString("N")); i++) Thread.Sleep(250);
            });
        }

        public static bool DeleteStage(string id)
        {
            string folder = TempFolder(id);
            try
            {
                File.Delete(Path.Combine(folder, Exe));
                File.Delete(Path.Combine(folder, Asset));
                Directory.Delete(folder, false);
                return true;
            }
            catch (DirectoryNotFoundException) { return true; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private static void TryDelete(string path)
        {
            try { File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        private static string TempFolder(string id) { return Path.Combine(Path.GetTempPath(), TempPrefix + id); }
        private static string Quote(string value) { return "\"" + value.TrimEnd('\\') + "\""; }

        private static Process Launch(string executable, string args)
        {
            return Process.Start(new ProcessStartInfo(executable, args)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(executable)
            });
        }

        private static string RunGh(string arguments)
        {
            string executable = null;
            foreach (string entry in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator))
            {
                string directory = entry.Trim().Trim('"');
                if (!Path.IsPathRooted(directory)) continue;
                string candidate = Path.Combine(directory, "gh.exe");
                if (File.Exists(candidate)) { executable = candidate; break; }
            }
            if (executable == null) throw new InvalidOperationException("GitHub CLI(gh)를 설치하고 로그인하세요.");

            var start = new ProcessStartInfo(executable, arguments)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true
            };
            start.EnvironmentVariables["GH_PROMPT_DISABLED"] = "1";
            using (var process = Process.Start(start))
            {
                var output = process.StandardOutput.ReadToEndAsync();
                if (!process.WaitForExit(45000))
                {
                    try { process.Kill(); }
                    catch (InvalidOperationException) { }
                    throw new IOException("GitHub 요청 시간이 초과되었습니다.");
                }
                if (process.ExitCode != 0)
                    throw new IOException("GitHub 연결과 OffClock 저장소에 접근할 수 있는 gh 로그인을 확인하세요.");
                return output.GetAwaiter().GetResult();
            }
        }

        private sealed class ReleaseData
        {
            public ReleaseData() { }
            public string tag_name { get; set; }
            public string body { get; set; }
            public bool draft { get; set; }
            public bool prerelease { get; set; }
            public ReleaseAsset[] assets { get; set; }
        }

        private sealed class ReleaseAsset
        {
            public ReleaseAsset() { }
            public string name { get; set; }
            public string digest { get; set; }
        }
    }

    internal sealed class UpdateDialog : Form
    {
        private readonly Label status;
        private readonly TextBox notes;
        private readonly Button install;
        private readonly Button close;
        private ReleaseUpdate update;
        private bool busy;

        public UpdateDialog(bool installed = false)
        {
            Text = installed ? "퇴근시계 릴리스 노트" : "퇴근시계 업데이트";
            Font = new Font("Malgun Gothic", 9);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(462, 318);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MinimizeBox = MaximizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            status = new Label { Location = new Point(18, 16), Size = new Size(426, 54) };
            notes = new TextBox
            {
                Name = "ReleaseNotes", AccessibleName = "변경 내용", Multiline = true, ReadOnly = true,
                Location = new Point(18, 76), Size = new Size(426, 182), ScrollBars = ScrollBars.Vertical
            };
            install = new Button { Text = "지금 설치", Location = new Point(353, 273), Size = new Size(91, 29), Enabled = false };
            close = new Button { Text = "닫기", Location = new Point(installed ? 353 : 254, 273), Size = new Size(91, 29), DialogResult = DialogResult.Cancel };
            Controls.AddRange(new Control[] { status, notes, close });
            CancelButton = close;
            if (installed)
            {
                status.Text = "v" + typeof(ReleaseUpdate).Assembly.GetName().Version.ToString(3) + " 설치 완료 · 변경 내용";
                ShowNotes(ReleaseUpdate.InstalledNotes());
                close.Click += delegate { Close(); };
                AcceptButton = close;
                ActiveControl = close;
                install.Dispose();
                return;
            }
            Controls.Add(install);
            AcceptButton = install;
            Shown += async delegate
            {
                status.Text = "GitHub에서 업데이트를 확인하는 중입니다.";
                SetBusy(true);
                try
                {
                    update = await Task.Run((Func<ReleaseUpdate>)ReleaseUpdate.Check);
                    status.Text = update == null ? "최신 버전을 사용 중입니다." :
                        "새 버전 " + update.Tag + "을 사용할 수 있습니다.\r\n설치하면 앱이 다시 실행되며, 저장하지 않은 설정은 사라집니다.";
                    if (update != null) ShowNotes(update.Notes);
                }
                catch (Exception ex) { status.Text = ex.Message; }
                finally { SetBusy(false); }
            };
            install.Click += async delegate
            {
                status.Text = "업데이트를 내려받고 확인하는 중입니다.";
                SetBusy(true);
                string id = null;
                try
                {
                    id = await Task.Run((Func<string>)update.Download);
                    ReleaseUpdate.StartInstaller(id);
                    SetBusy(false);
                    DialogResult = DialogResult.OK;
                }
                catch (Exception ex)
                {
                    if (id != null) ReleaseUpdate.DeleteStage(id);
                    status.Text = ex.Message;
                    SetBusy(false);
                }
            };
            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (busy && e.CloseReason == CloseReason.UserClosing) e.Cancel = true;
            };
        }

        private void ShowNotes(string text)
        {
            notes.Text = string.IsNullOrWhiteSpace(text) ? "등록된 변경 내용이 없습니다." :
                text.Trim().Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);
        }

        private void SetBusy(bool value)
        {
            busy = value;
            install.Enabled = !busy && update != null;
            close.Enabled = !busy;
        }
    }
}
