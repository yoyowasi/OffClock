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
[assembly: System.Reflection.AssemblyVersion("1.1.0.0")]

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
            if (args.Length == 2 && args[0] == "--clean-update") ReleaseUpdate.CleanAfterInstall(args[1]);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            bool firstInstance;
            using (var instance = new Mutex(true, @"Local\OffClock.Countdown", out firstInstance))
            {
                if (!firstInstance) return 0;

                string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OffClock", "schedule.txt");
                using (var app = new ClockApplication(new SettingsFile(path)))
                    Application.Run(app);
            }
            return 0;
        }
    }

    internal sealed class ClockApplication : ApplicationContext
    {
        private readonly SettingsFile store;
        private readonly Dictionary<string, ClockStrip> strips = new Dictionary<string, ClockStrip>();
        private readonly float dpi;
        private readonly TaskbarDock dock = new TaskbarDock();
        private readonly Timer animation = new Timer { Interval = 32 };
        private readonly Timer placement = new Timer { Interval = 400 };
        private readonly FinishReminder reminder = new FinishReminder(DateTime.Now);
        private readonly Icon icon;
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly NotifyIcon tray;
        private readonly ToolStripMenuItem updateMenu;
        private ClockSettings settings;
        private ClockSettingsDialog editor;
        private FinishNotice notice;
        private bool paused;
        private bool refreshOnTick;
        private bool resumePending;
        private DateTime nextUpdateCheck;
        private Task<ReleaseUpdate> updateCheck;
        private ReleaseUpdate availableUpdate;
        private bool updateDialogOpen;

        public ClockApplication(SettingsFile store)
        {
            this.store = store;
            settings = store.Load();
            using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero))
                dpi = screen.DpiX / 96f;

            icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            menu.Items.Add("퇴근시계 설정…", null, delegate { EditSettings(); });
            var visibility = new ToolStripMenuItem("카운트다운 숨기기");
            visibility.Click += delegate
            {
                paused = !paused;
                visibility.Text = paused ? "카운트다운 표시하기" : "카운트다운 숨기기";
                UpdatePlacement();
            };
            menu.Items.Add(visibility);
            updateMenu = new ToolStripMenuItem("업데이트 확인…", null, delegate { ShowUpdateDialog(); });
            menu.Items.Add(updateMenu);
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("종료", null, delegate { ExitThread(); });
            tray = new NotifyIcon { Icon = icon ?? SystemIcons.Application, ContextMenuStrip = menu };
            tray.MouseDoubleClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) EditSettings();
            };
            UpdateTrayText();
            tray.Visible = true;

            animation.Tick += delegate { UpdateCountdown(); };
            placement.Tick += delegate
            {
                bool resumed = resumePending;
                resumePending = false;
                refreshOnTick = !refreshOnTick;
                if ((refreshOnTick || resumed) && !paused) dock.RefreshInlineSpace();
                UpdatePlacement();
                if (resumed && !paused)
                {
                    UpdateCountdown();
                    foreach (ClockStrip strip in strips.Values)
                        if (strip.Visible) strip.Invalidate();
                }
                if (settings.NotifyAtEnd && reminder.ShouldNotify(settings.Schedule, DateTime.Now))
                    ShowFinishNotice();
                CheckForUpdates();
            };
            dock.RefreshInlineSpace();
            UpdatePlacement();
            placement.Start();
        }

        private void UpdateTrayText()
        {
            tray.Text = "퇴근시계  ·  " + Schedule.Format(settings.Schedule.Start) +
                "–" + Schedule.Format(settings.Schedule.End);
        }

        private void UpdateCountdown()
        {
            DateTime now = DateTime.Now;
            Countdown countdown = settings.Schedule.At(now);
            long earned = EarnedAt(now, countdown);
            foreach (ClockStrip strip in strips.Values)
                if (strip.Visible) strip.Display(countdown, settings.Display, earned);
        }

        private long EarnedAt(DateTime now, Countdown countdown)
        {
            return settings.Display == DisplayMode.Earnings
                ? PayEstimate.At(settings.AnnualSalary, settings.Schedule.WorkDateAt(now), countdown.Progress)
                : 0;
        }

        private void CheckForUpdates()
        {
            if (updateCheck != null)
            {
                if (!updateCheck.IsCompleted) return;
                if (!updateCheck.IsFaulted && !updateCheck.IsCanceled)
                {
                    availableUpdate = updateCheck.Result;
                    updateMenu.Text = availableUpdate == null ? "업데이트 확인…" :
                        "업데이트 설치 (" + availableUpdate.Tag + ")…";
                }
                else if (updateCheck.IsFaulted)
                {
                    System.Diagnostics.Trace.WriteLine(updateCheck.Exception.GetBaseException().Message);
                }
                updateCheck = null;
            }

            DateTime now = DateTime.UtcNow;
            if (!settings.AutoCheckUpdates || updateDialogOpen || now < nextUpdateCheck) return;
            nextUpdateCheck = now.AddHours(6);
            updateCheck = Task.Run((Func<ReleaseUpdate>)ReleaseUpdate.Check);
        }

        private void ShowUpdateDialog()
        {
            if (updateDialogOpen || editor != null) return;
            updateDialogOpen = true;
            try
            {
                using (var dialog = new UpdateDialog(availableUpdate, updateCheck))
                    if (dialog.ShowDialog() == DialogResult.OK) ExitThread();
            }
            finally { updateDialogOpen = false; }
        }

        private void UpdatePlacement()
        {
            if (paused)
            {
                animation.Stop();
                foreach (ClockStrip strip in strips.Values) strip.Hide();
                return;
            }

            var attached = new HashSet<string>();
            bool anyVisible = false;
            foreach (Taskbar taskbar in TaskbarDock.FindForDisplay(settings.Monitor))
            {
                string monitor = taskbar.Screen.DeviceName;
                if (!attached.Add(monitor)) continue;

                ClockStrip strip;
                if (!strips.TryGetValue(monitor, out strip))
                {
                    strip = new ClockStrip(dpi);
                    if (editor == null)
                        strip.SetAppearance(settings.Transparency, settings.ShowProgress, settings.Theme);
                    else
                        strip.SetAppearance(editor.SelectedTransparency, editor.ShowProgress, editor.SelectedTheme);
                    strip.MouseClick += delegate(object sender, MouseEventArgs e)
                    {
                        if (e.Button == MouseButtons.Left) strip.BeginInvoke((Action)EditSettings);
                    };
                    strip.TaskbarCreated += RestoreTrayIcon;
                    strip.PowerResumed += delegate { resumePending = true; };
                    strips.Add(monitor, strip);
                }

                Rectangle target = dock.Place(taskbar, new Size(strip.Scale(235), strip.Scale(34)),
                    strip.Scale(7), settings.PinInside);
                if (target.IsEmpty)
                {
                    strip.Hide();
                    continue;
                }

                if (strip.Bounds != target) strip.Bounds = target;
                if (!strip.Visible)
                {
                    DateTime now = DateTime.Now;
                    Countdown countdown = settings.Schedule.At(now);
                    strip.Display(countdown, settings.Display, EarnedAt(now, countdown));
                    strip.Show();
                }
                if (target.IntersectsWith(taskbar.Bounds))
                    Native.KeepAboveTaskbar(strip.Handle, taskbar.Window);
                anyVisible = true;
            }

            foreach (string monitor in strips.Keys.Where(name => !attached.Contains(name)).ToArray())
            {
                strips[monitor].Dispose();
                strips.Remove(monitor);
            }
            animation.Enabled = anyVisible;
        }

        private void RestoreTrayIcon(object sender, EventArgs e)
        {
            if (!ReferenceEquals(sender, strips.Values.FirstOrDefault(item => item.IsHandleCreated))) return;
            tray.Visible = false;
            tray.Visible = true;
            UpdatePlacement();
        }

        private void EditSettings()
        {
            if (editor != null)
            {
                editor.Activate();
                return;
            }

            var startup = new LoginStartup(Environment.GetFolderPath(Environment.SpecialFolder.Startup,
                Environment.SpecialFolderOption.DoNotVerify),
                Application.ExecutablePath);
            using (var dialog = new ClockSettingsDialog(settings, icon, startup.Registered))
            {
                editor = dialog;
                try
                {
                    dialog.PreviewAppearance = UpdateAppearance;
                    dialog.StoreSettings = updated =>
                    {
                        startup.Save(dialog.StartAtLogin, delegate { store.Save(updated); });
                        if (updated.NotifyAtEnd != settings.NotifyAtEnd ||
                            updated.Schedule.Start != settings.Schedule.Start || updated.Schedule.End != settings.Schedule.End)
                            reminder.Reset(DateTime.Now);
                        if (updated.AutoCheckUpdates && !settings.AutoCheckUpdates) nextUpdateCheck = DateTime.MinValue;
                        settings = updated;
                        UpdateTrayText();
                        UpdatePlacement();
                        UpdateCountdown();
                    };
                    dialog.ShowDialog();
                }
                finally
                {
                    editor = null;
                    UpdateAppearance(settings.Transparency, settings.ShowProgress, settings.Theme);
                }
            }
        }

        private void UpdateAppearance(int transparency, bool showProgress, ClockTheme theme)
        {
            foreach (ClockStrip strip in strips.Values)
                strip.SetAppearance(transparency, showProgress, theme);
        }

        private void ShowFinishNotice()
        {
            Taskbar taskbar = TaskbarDock.FindForDisplay(settings.Monitor).First();
            if (Native.IsFullscreen(taskbar.Screen.Bounds)) return;
            if (notice != null) notice.Close();
            notice = new FinishNotice(dpi, settings.Theme, taskbar.Screen.WorkingArea);
            ClockStrip strip;
            if (strips.TryGetValue(taskbar.Screen.DeviceName, out strip) && strip.Visible &&
                notice.Bounds.IntersectsWith(strip.Bounds))
            {
                notice.Top = strip.Top - notice.Height - strip.Scale(7);
            }
            notice.FormClosed += delegate { notice = null; };
            notice.Show();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                animation.Dispose();
                placement.Dispose();
                tray.Dispose();
                menu.Dispose();
                if (notice != null) notice.Dispose();
                foreach (ClockStrip strip in strips.Values) strip.Dispose();
                if (icon != null) icon.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal abstract class TaskbarWindow : Form
    {
        protected TaskbarWindow()
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                const int toolWindow = 0x80, noActivate = 0x08000000;
                CreateParams result = base.CreateParams;
                result.ExStyle |= toolWindow | noActivate;
                return result;
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int mouseActivate = 0x0021, noActivate = 3;
            if (m.Msg == mouseActivate)
            {
                m.Result = new IntPtr(noActivate);
                return;
            }
            base.WndProc(ref m);
        }
    }

    internal sealed class ClockPalette
    {
        private static readonly ClockPalette Dark = new ClockPalette(
            0x1A202B, 0x3F4B5D, 0xF3F8FF, 0xB1BFD3, 0x7BE8B8, 0xFFC286, 0xFF6A70, 0x3C485A);
        private static readonly ClockPalette Light = new ClockPalette(
            0xF6F8FB, 0xB8C6D6, 0x18283D, 0x43536A, 0x167256, 0x9A4812, 0xB51E36, 0xD6E0EB);

        public readonly Color Background, Border, Digits, Muted, Normal, Soon, Urgent, Track;

        private ClockPalette(int background, int border, int digits, int muted, int normal, int soon, int urgent, int track)
        {
            Background = Opaque(background);
            Border = Opaque(border);
            Digits = Opaque(digits);
            Muted = Opaque(muted);
            Normal = Opaque(normal);
            Soon = Opaque(soon);
            Urgent = Opaque(urgent);
            Track = Opaque(track);
        }

        private static Color Opaque(int rgb) { return Color.FromArgb(unchecked((int)0xFF000000) | rgb); }

        public static ClockPalette For(ClockTheme theme) { return theme == ClockTheme.Light ? Light : Dark; }
    }

    internal sealed class FinishNotice : TaskbarWindow
    {
        private readonly ClockPalette palette;
        private readonly float dpi;
        private readonly Timer dismissal = new Timer { Interval = 4000 };
        private readonly Font titleFont = new Font("Malgun Gothic", 16, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font bodyFont = new Font("Malgun Gothic", 12, FontStyle.Regular, GraphicsUnit.Pixel);

        public FinishNotice(float dpi, ClockTheme theme, Rectangle workArea)
        {
            this.dpi = dpi;
            palette = ClockPalette.For(theme);
            Text = "퇴근 시간이에요";
            BackColor = palette.Background;
            ClientSize = new Size((int)Math.Round(248 * dpi), (int)Math.Round(65 * dpi));
            int margin = (int)Math.Round(10 * dpi);
            Location = new Point(workArea.Right - Width - margin, workArea.Bottom - Height - margin);
            dismissal.Tick += delegate { Close(); };
            Shown += delegate { dismissal.Start(); };
            MouseClick += delegate { Close(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.ScaleTransform(dpi, dpi);
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var border = new Pen(palette.Border))
            using (var accent = new SolidBrush(palette.Normal))
            using (var title = new SolidBrush(palette.Digits))
            using (var body = new SolidBrush(palette.Muted))
            {
                graphics.DrawRectangle(border, 0.5f, 0.5f, 247, 64);
                graphics.FillRectangle(accent, 9, 13, 3, 40);
                graphics.DrawString(Text, titleFont, title, 21, 8);
                graphics.DrawString("설정한 퇴근 시간이 되었습니다.", bodyFont, body, 22, 34);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                dismissal.Dispose();
                titleFont.Dispose();
                bodyFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class ClockStrip : TaskbarWindow
    {
        private readonly Font labelFont = new Font("Malgun Gothic", 12, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font timeFont = new Font("Consolas", 20, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font msFont = new Font("Consolas", 14, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font totalFont = new Font("Consolas", 17, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font compactFont = new Font("Consolas", 11.5f, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly float dpi;
        private readonly int taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
        private Countdown countdown;
        private DisplayMode display;
        private long earnings;
        private bool showProgress = true;
        private ClockTheme theme;
        public event EventHandler TaskbarCreated;
        public event EventHandler PowerResumed;

        public ClockStrip(float dpi)
        {
            this.dpi = dpi;
            ClientSize = new Size(Scale(235), Scale(34));
            Text = "퇴근시계 카운트다운";
            Cursor = Cursors.Hand;
            BackColor = TransparencyKey = Color.Magenta;
        }

        public int Scale(int value) { return (int)Math.Round(value * dpi); }
        protected override void WndProc(ref Message m)
        {
            const int powerBroadcast = 0x0218;
            const int resumeAutomatic = 0x12, resumeFromSuspend = 0x07;
            if (m.Msg == powerBroadcast && PowerResumed != null &&
                (m.WParam.ToInt64() == resumeAutomatic || m.WParam.ToInt64() == resumeFromSuspend))
            {
                PowerResumed(this, EventArgs.Empty);
            }
            base.WndProc(ref m);
            if (m.Msg == taskbarCreated && TaskbarCreated != null)
                TaskbarCreated(this, EventArgs.Empty);
        }

        public void Display(Countdown value, DisplayMode mode, long earned = 0)
        {
            if (value.Remaining == countdown.Remaining && value.Progress == countdown.Progress &&
                mode == display && earned == earnings)
                return;
            countdown = value;
            display = mode;
            earnings = earned;
            Invalidate();
        }

        public void SetAppearance(int transparency, bool showProgress, ClockTheme theme = ClockTheme.Dark)
        {
            Opacity = 1 - transparency / 100.0;
            if (this.showProgress == showProgress && this.theme == theme) return;
            this.showProgress = showProgress;
            this.theme = theme;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            float scale = Math.Min(ClientSize.Width / 235f, ClientSize.Height / 34f);
            g.ScaleTransform(scale, scale);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            ClockPalette palette = ClockPalette.For(theme);
            Color accent = AccentColor(palette);
            using (var panel = RoundedRectangle(0.5f, 0.5f, 234, 33, 9))
            using (var panelFill = new SolidBrush(palette.Background))
            using (var outline = new Pen(palette.Border))
            using (var accentBrush = new SolidBrush(accent))
            using (var accentPen = new Pen(accent, 1.35f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var dim = new SolidBrush(palette.Muted))
            using (var bright = new SolidBrush(palette.Digits))
            {
                // Keep chroma-key edges crisp, including on light taskbars.
                g.SmoothingMode = SmoothingMode.None;
                g.FillPath(panelFill, panel);
                g.DrawPath(outline, panel);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawEllipse(accentPen, 9, 7, 15, 15);
                if (countdown.Finished)
                    g.DrawLines(accentPen, new[] { new PointF(13, 14.5f), new PointF(16, 17.5f), new PointF(20.5f, 12) });
                else
                    g.DrawLines(accentPen, new[] { new PointF(16.5f, 10.5f), new PointF(16.5f, 14.5f), new PointF(19.5f, 16) });

                string label = display == DisplayMode.Earnings ? "오늘 급여" :
                    (countdown.Finished ? "퇴근 완료" : "퇴근까지");
                g.DrawString(label, labelFont, dim, 31, 7.5f);
                DrawTime(g, accent == palette.Urgent ? accentBrush : bright, accentBrush);
                if (showProgress)
                {
                    using (var track = new SolidBrush(palette.Track))
                        g.FillRectangle(track, 10, 27, 215, 2);
                    g.FillRectangle(accentBrush, 10, 27, (float)(215 * countdown.Progress), 2);
                }
            }
        }

        private Color AccentColor(ClockPalette palette)
        {
            if (countdown.Finished) return palette.Normal;
            if (countdown.Remaining <= TimeSpan.FromMinutes(10)) return palette.Urgent;
            if (countdown.Remaining <= TimeSpan.FromMinutes(30)) return palette.Soon;
            return palette.Normal;
        }

        private void DrawTime(Graphics g, Brush digits, Brush accent)
        {
            var format = StringFormat.GenericTypographic;
            if (display == DisplayMode.Clock)
            {
                g.DrawString(countdown.Seconds, timeFont, digits, 91, 2.5f, format);
                g.DrawString(countdown.Milliseconds, msFont, accent, 182, 8, format);
                return;
            }

            string unit;
            string total;
            if (display == DisplayMode.Earnings)
            {
                unit = "원";
                total = earnings.ToString("N0", CultureInfo.InvariantCulture);
            }
            else
            {
                total = countdown.FormatTotal(display, out unit);
            }
            float unitX = 224 - g.MeasureString(unit, labelFont, PointF.Empty, format).Width;
            float digitsRight = unitX - 5;
            Font font = totalFont;
            float digitsX = digitsRight - g.MeasureString(total, font, PointF.Empty, format).Width;
            if (digitsX < 84)
            {
                font = compactFont;
                digitsX = digitsRight - g.MeasureString(total, font, PointF.Empty, format).Width;
            }
            g.DrawString(total, font, digits, digitsX, font == totalFont ? 4.5f : 8, format);
            g.DrawString(unit, labelFont, accent, unitX, 8, format);
        }

        private static GraphicsPath RoundedRectangle(float x, float y, float width, float height, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2;
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + width - d, y, d, d, 270, 90);
            path.AddArc(x + width - d, y + height - d, d, d, 0, 90);
            path.AddArc(x, y + height - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                labelFont.Dispose();
                timeFont.Dispose();
                msFont.Dispose();
                totalFont.Dispose();
                compactFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class ClockSettingsDialog : Form
    {
        private readonly TrackBar transparency;
        private readonly CheckBox progress;
        private readonly ComboBox theme;
        private readonly Label feedback;
        private readonly CheckBox startAtLogin;
        public bool StartAtLogin { get { return startAtLogin.Checked; } }
        public Action<ClockSettings> StoreSettings { get; set; }
        public Action<int, bool, ClockTheme> PreviewAppearance { get; set; }
        public int SelectedTransparency { get { return transparency.Value; } }
        public bool ShowProgress { get { return progress.Checked; } }
        public ClockTheme SelectedTheme { get { return (ClockTheme)theme.SelectedIndex; } }

        public ClockSettingsDialog(ClockSettings settings, Icon icon, bool registeredAtLogin = false)
        {
            Text = "퇴근시계 설정";
            if (icon != null) Icon = icon;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(324, 547);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            Font = new Font("Malgun Gothic", 9);
            BackColor = SystemColors.Window;
            ForeColor = SystemColors.WindowText;
            var start = TimePicker(settings.Schedule.Start, "출근 시간", 22);
            var end = TimePicker(settings.Schedule.End, "퇴근 시간", 63);
            var display = AddDropDown("DisplayMode", "표시 형식", 104);
            display.Items.AddRange(new object[]
            {
                "시:분:초.밀리초", "시간만", "분만", "초만", "밀리초만", "2진수", "8진수", "16진수", "오늘 급여"
            });
            display.SelectedIndex = (int)settings.Display;
            var monitor = AddDropDown("Monitor", "표시할 모니터", 145);
            PopulateMonitors(monitor, settings.Monitor);
            theme = AddDropDown("Theme", "테마", 186);
            theme.Items.AddRange(new object[] { "다크", "라이트" });
            theme.SelectedIndex = (int)settings.Theme;
            theme.SelectedIndexChanged += delegate { Preview(); };
            var transparencyLabel = new Label { Text = "투명도", AutoSize = true, Location = new Point(18, 231) };
            transparency = new TrackBar
            {
                Name = "Transparency", AccessibleName = "투명도", TickStyle = TickStyle.None,
                Minimum = 0, Maximum = ClockSettings.MaxTransparency, Value = settings.Transparency,
                SmallChange = 1, LargeChange = 10, AutoSize = false, Location = new Point(145, 224),
                Size = new Size(118, 32), TabIndex = 5
            };
            var percentage = new Label
            {
                Text = settings.Transparency + "%", TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(265, 227), Size = new Size(39, 23)
            };
            progress = new CheckBox
            {
                Name = "ShowProgress", Text = "진행 막대 표시", Checked = settings.ShowProgress,
                AutoSize = true, Location = new Point(18, 266), TabIndex = 6
            };
            var notify = new CheckBox
            {
                Name = "NotifyAtEnd", Text = "퇴근 시간에 화면 알림 (무음)", Checked = settings.NotifyAtEnd,
                AutoSize = true, Location = new Point(18, 298), TabIndex = 7
            };
            var pinInside = new CheckBox
            {
                Name = "PinInside", Text = "가로 작업표시줄 안에 고정", Checked = settings.PinInside,
                AutoSize = true, Location = new Point(18, 330), TabIndex = 8
            };
            startAtLogin = new CheckBox
            {
                Name = "StartAtLogin", Text = "Windows 로그인 시 자동 시작", Checked = registeredAtLogin,
                AutoSize = true, Location = new Point(18, 362), TabIndex = 9
            };
            var salaryLabel = new Label { Text = "세전 연봉 (원)", AutoSize = true, Location = new Point(18, 403) };
            var salary = new TextBox
            {
                Name = "AnnualSalary", AccessibleName = "세전 연봉 (원)", Location = new Point(150, 398),
                Width = 154, MaxLength = 20, TextAlign = HorizontalAlignment.Right, TabIndex = 10,
                Text = settings.AnnualSalary == 0 ? "" : settings.AnnualSalary.ToString("N0", CultureInfo.InvariantCulture)
            };
            var autoUpdates = new CheckBox
            {
                Name = "AutoUpdates", Text = "새 버전 자동 확인 (GitHub CLI)", Checked = settings.AutoCheckUpdates,
                AutoSize = true, Location = new Point(18, 432), TabIndex = 11
            };
            transparency.ValueChanged += delegate
            {
                percentage.Text = transparency.Value + "%";
                Preview();
            };
            progress.CheckedChanged += delegate { Preview(); };
            start.TabIndex = 0;
            end.TabIndex = 1;
            display.TabIndex = 2;
            monitor.TabIndex = 3;
            theme.TabIndex = 4;
            feedback = new Label
            {
                Name = "Feedback",
                Text = "퇴근 시간이 출근보다 이르면 다음 날로 계산합니다.",
                AutoSize = true,
                Location = new Point(18, 472)
            };
            var save = new Button { Text = "저장", Location = new Point(225, 506), Size = new Size(79, 28), TabIndex = 13 };
            var cancel = new Button
            {
                Text = "취소", Location = new Point(138, 506), Size = new Size(79, 28), TabIndex = 12,
                DialogResult = DialogResult.Cancel
            };
            save.Click += delegate
            {
                Save(start.Value.TimeOfDay, end.Value.TimeOfDay, (DisplayMode)display.SelectedIndex,
                    ((MonitorOption)monitor.SelectedItem).Device, transparency.Value, progress.Checked,
                    notify.Checked, SelectedTheme, pinInside.Checked, salary.Text, autoUpdates.Checked);
            };
            Controls.AddRange(new Control[]
            {
                start, end, transparencyLabel, transparency, percentage, progress, notify, pinInside, startAtLogin,
                salaryLabel, salary, autoUpdates, feedback, cancel, save
            });
            AcceptButton = save;
            CancelButton = cancel;
        }

        private void Preview()
        {
            if (PreviewAppearance != null) PreviewAppearance(SelectedTransparency, ShowProgress, SelectedTheme);
        }

        private DateTimePicker TimePicker(TimeSpan time, string name, int y)
        {
            Controls.Add(new Label { Text = name, AutoSize = true, Location = new Point(18, y + 4) });
            return new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "HH:mm",
                ShowUpDown = true,
                AccessibleName = name,
                Location = new Point(150, y),
                Size = new Size(154, 26),
                Value = DateTime.Today.Add(time)
            };
        }

        private ComboBox AddDropDown(string name, string label, int y)
        {
            Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(18, y + 4) });
            var control = new ComboBox
            {
                Name = name, AccessibleName = label, DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(150, y), Size = new Size(154, 26)
            };
            Controls.Add(control);
            return control;
        }

        private static void PopulateMonitors(ComboBox control, string selected)
        {
            control.Items.Add(new MonitorOption("", "모든 모니터"));
            Screen primary = Screen.PrimaryScreen;
            int secondary = 0;
            foreach (Screen screen in Screen.AllScreens.OrderByDescending(item => item.Primary)
                .ThenBy(item => item.Bounds.Left).ThenBy(item => item.Bounds.Top))
            {
                string label = screen.Primary ? "기본 모니터" :
                    "보조 " + (++secondary) + " (" + RelativePosition(screen.Bounds, primary.Bounds) + ")";
                control.Items.Add(new MonitorOption(screen.DeviceName, label));
            }

            MonitorOption choice = control.Items.Cast<MonitorOption>().FirstOrDefault(item => item.Device == selected);
            if (choice == null)
            {
                choice = new MonitorOption(selected, "연결되지 않은 모니터");
                control.Items.Add(choice);
            }
            control.SelectedItem = choice;
        }

        private static string RelativePosition(Rectangle screen, Rectangle primary)
        {
            int x = screen.Left + screen.Width / 2 - primary.Left - primary.Width / 2;
            int y = screen.Top + screen.Height / 2 - primary.Top - primary.Height / 2;
            if (Math.Abs(x) >= Math.Abs(y)) return x < 0 ? "왼쪽" : "오른쪽";
            return y < 0 ? "위" : "아래";
        }

        private sealed class MonitorOption
        {
            public readonly string Device;
            private readonly string label;

            public MonitorOption(string device, string label)
            {
                Device = device;
                this.label = label;
            }

            public override string ToString() { return label; }
        }

        private void Save(TimeSpan start, TimeSpan end, DisplayMode display, string monitor,
            int translucency, bool showProgress, bool notifyAtEnd, ClockTheme selectedTheme, bool pinInside,
            string salaryText, bool autoUpdates)
        {
            try
            {
                long salary = 0;
                if (!string.IsNullOrWhiteSpace(salaryText) && (!long.TryParse(salaryText.Trim(), NumberStyles.AllowThousands,
                    CultureInfo.InvariantCulture, out salary) || salary < 0 || salary > 999999999999L))
                    throw new ArgumentException("세전 연봉을 원 단위 숫자로 입력하세요.");
                if (display == DisplayMode.Earnings && salary == 0)
                    throw new ArgumentException("오늘 급여를 표시하려면 세전 연봉을 입력하세요.");
                StoreSettings(new ClockSettings(new Schedule(start, end), display, monitor,
                    translucency, showProgress, notifyAtEnd, selectedTheme, pinInside, salary, autoUpdates));
                DialogResult = DialogResult.OK;
            }
            catch (ArgumentException e) { ShowError(e.Message); }
            catch (IOException) { ShowSaveError(); }
            catch (UnauthorizedAccessException) { ShowSaveError(); }
            catch (SecurityException) { ShowSaveError(); }
            catch (COMException) { ShowError("자동 시작 바로 가기를 저장할 수 없습니다."); }
        }

        private void ShowSaveError()
        {
            ShowError("설정 파일을 저장할 수 없습니다.");
        }

        private void ShowError(string message)
        {
            feedback.ForeColor = ClockPalette.For(ClockTheme.Light).Urgent;
            feedback.Text = message;
        }
    }
}
