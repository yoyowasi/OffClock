using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

[assembly: System.Reflection.AssemblyTitle("퇴근시계")]
[assembly: System.Reflection.AssemblyDescription("Windows 작업표시줄 퇴근 카운트다운")]
[assembly: System.Reflection.AssemblyVersion("1.6.0.0")]

namespace OffClock
{
    internal sealed class LoginStartup
    {
        private readonly string folder;
        private readonly string executable;
        private readonly string shortcut;

        public LoginStartup(string folder, string executable)
        {
            this.folder = folder;
            this.executable = executable;
            shortcut = Path.Combine(folder, "OffClock.lnk");
        }

        public bool Registered { get { return File.Exists(shortcut); } }

        public void Save(bool enabled, Action savePreferences)
        {
            byte[] previous = Registered ? File.ReadAllBytes(shortcut) : null;
            bool changed = false;
            try
            {
                if (enabled)
                {
                    Directory.CreateDirectory(folder);
                    changed = WriteShortcut();
                }
                else if (previous != null)
                {
                    File.Delete(shortcut);
                    changed = true;
                }
                savePreferences();
            }
            catch
            {
                if (changed)
                {
                    if (previous == null) File.Delete(shortcut);
                    else File.WriteAllBytes(shortcut, previous);
                }
                throw;
            }
        }

        private bool WriteShortcut()
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
            try
            {
                dynamic link = shell.CreateShortcut(shortcut);
                try
                {
                    if (string.Equals((string)link.TargetPath, executable, StringComparison.OrdinalIgnoreCase) &&
                        string.IsNullOrEmpty((string)link.Arguments)) return false;
                    link.TargetPath = executable;
                    link.Arguments = "";
                    link.WorkingDirectory = Path.GetDirectoryName(executable);
                    link.Description = "퇴근시계";
                    link.Save();
                    return true;
                }
                finally { Marshal.ReleaseComObject((object)link); }
            }
            finally { Marshal.ReleaseComObject((object)shell); }
        }
    }

    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--install-update") return ReleaseUpdate.Install(args);
            bool restartingAfterUpdate = args.Length >= 2 && args[0] == "--clean-update";
            bool showReleaseNotes = restartingAfterUpdate && args.Length == 3 && args[2] == "--show-release-notes";
            bool updateFailed = restartingAfterUpdate && args.Length == 3 && args[2] == "--update-failed";
            if (restartingAfterUpdate) ReleaseUpdate.CleanAfterInstall(args[1]);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool firstInstance;
            using (var instance = new Mutex(true, @"Local\OffClock.Countdown", out firstInstance))
            {
                if (!firstInstance) return 0;

                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OffClock");
                FileLog.Start(Path.Combine(folder, "offclock.log"));
                // Right after an update the installer may still need its .bak to roll back: clean up next start.
                if (!restartingAfterUpdate) ReleaseUpdate.CleanStaleStages();
                // A widget should keep counting: log a failing tick instead of showing the crash dialog every 400 ms.
                Application.ThreadException += LogOnce;
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                {
                    System.Diagnostics.Trace.WriteLine("Fatal: " + e.ExceptionObject);
                };
                using (var app = new ClockApplication(new SettingsFile(Path.Combine(folder, "schedule.txt")),
                    restartingAfterUpdate, showReleaseNotes, updateFailed))
                    Application.Run(app);
            }
            return 0;
        }

        private static readonly Dictionary<string, DateTime> loggedAt = new Dictionary<string, DateTime>();

        // Each distinct failure is written at most once an hour, however many ticks repeat it.
        private static void LogOnce(object sender, ThreadExceptionEventArgs e)
        {
            string text = e.Exception.ToString();
            DateTime now = DateTime.UtcNow;
            DateTime last;
            if (loggedAt.TryGetValue(text, out last) && now - last < TimeSpan.FromHours(1)) return;
            if (loggedAt.Count >= 100) loggedAt.Clear();
            loggedAt[text] = now;
            System.Diagnostics.Trace.WriteLine(text);
        }
    }
}
