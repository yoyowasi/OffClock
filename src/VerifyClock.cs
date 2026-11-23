using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace OffClock
{
    internal static class VerifyClock
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles();
                VerifyFormats();
                VerifySchedule();
                VerifyPayEstimate();
                VerifyReleaseCheck();
                VerifySettings(args[0]);
                VerifyLoginStartup(args[0]);
                VerifyMonitorSelection();
                VerifyPinnedPlacement();
                VerifyPowerResume();
                VerifyColors();
                VerifyAppearance();
                VerifyFinishReminder();
                VerifyFinishNotice();
                Console.WriteLine("Verified countdown, settings, placement, power-resume messages, appearance, and silent notifications.");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }

        private static void VerifyLoginStartup(string folder)
        {
            string testFolder = Path.Combine(folder, "startup-" + Guid.NewGuid().ToString("N"));
            string shortcut = Path.Combine(testFolder, "OffClock.lnk");
            var startup = new LoginStartup(testFolder, Application.ExecutablePath);
            try
            {
                Require(!startup.Registered, "Startup must default to off");
                using (var dialog = new ClockSettingsDialog(ClockSettings.Default, null, false))
                {
                    dialog.StoreSettings = delegate { startup.Save(dialog.StartAtLogin, delegate { }); };
                    dialog.Shown += delegate
                    {
                        ((CheckBox)dialog.Controls["StartAtLogin"]).Checked = true;
                        dialog.AcceptButton.PerformClick();
                    };
                    Require(dialog.ShowDialog() == DialogResult.OK && startup.Registered, "Register on Save");
                }
                dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
                try
                {
                    dynamic link = shell.CreateShortcut(shortcut);
                    try
                    {
                        Require(string.Equals((string)link.TargetPath, Application.ExecutablePath,
                            StringComparison.OrdinalIgnoreCase), "Shortcut must target the running executable");
                    }
                    finally { Marshal.ReleaseComObject((object)link); }
                }
                finally { Marshal.ReleaseComObject((object)shell); }
                using (var dialog = new ClockSettingsDialog(ClockSettings.Default, null, startup.Registered))
                {
                    Require(dialog.StartAtLogin, "Reopen must show registered state");
                    dialog.StoreSettings = delegate { throw new InvalidOperationException("Cancel saved"); };
                    dialog.Shown += delegate
                    {
                        ((CheckBox)dialog.Controls["StartAtLogin"]).Checked = false;
                        dialog.CancelButton.PerformClick();
                    };
                    Require(dialog.ShowDialog() == DialogResult.Cancel && startup.Registered, "Cancel kept startup");
                }
                try { startup.Save(false, delegate { throw new IOException("Test failed settings write"); }); }
                catch (IOException) { }
                Require(startup.Registered, "Restore startup if settings save fails");
                startup.Save(false, delegate { });
                Require(!startup.Registered, "Disable startup");
            }
            finally
            {
                File.Delete(shortcut);
                if (Directory.Exists(testFolder)) Directory.Delete(testFolder);
            }
        }

        private static void VerifyFormats()
        {
            var remaining = new Countdown(TimeSpan.FromHours(3.2), 0.5);
            Require(remaining.Seconds == "03:12:00" && remaining.Milliseconds == ".000", "Composite time");
            CheckTotal(remaining, DisplayMode.Hours, "3.2", "시");
            CheckTotal(remaining, DisplayMode.Minutes, "192", "분");
            CheckTotal(remaining, DisplayMode.Seconds, "11,520", "초");
            CheckTotal(remaining, DisplayMode.Milliseconds, "11,520,000", "밀리초");
            var fortyTwo = new Countdown(TimeSpan.FromSeconds(42), 0.75);
            CheckTotal(fortyTwo, DisplayMode.Binary, "0b101010", "초");
            CheckTotal(fortyTwo, DisplayMode.Octal, "0o52", "초");
            CheckTotal(fortyTwo, DisplayMode.Hexadecimal, "0x2A", "초");
            var lastTick = new Countdown(TimeSpan.FromTicks(1), 0.99);
            CheckTotal(lastTick, DisplayMode.Hours, "<0.1", "시");
            CheckTotal(lastTick, DisplayMode.Minutes, "1", "분");
            CheckTotal(lastTick, DisplayMode.Seconds, "1", "초");
            CheckTotal(lastTick, DisplayMode.Milliseconds, "1", "밀리초");
            CheckTotal(lastTick, DisplayMode.Binary, "0b1", "초");
            CheckTotal(lastTick, DisplayMode.Octal, "0o1", "초");
            CheckTotal(lastTick, DisplayMode.Hexadecimal, "0x1", "초");
            var finished = new Countdown(TimeSpan.Zero, 1);
            CheckTotal(finished, DisplayMode.Hours, "0.0", "시");
            CheckTotal(finished, DisplayMode.Milliseconds, "0", "밀리초");
            CheckTotal(finished, DisplayMode.Binary, "0b0", "초");
            CheckTotal(finished, DisplayMode.Octal, "0o0", "초");
            CheckTotal(finished, DisplayMode.Hexadecimal, "0x0", "초");
        }

        private static void CheckTotal(Countdown remaining, DisplayMode mode, string value, string expectedUnit)
        {
            string unit;
            Require(remaining.FormatTotal(mode, out unit) == value && unit == expectedUnit, "Total " + mode);
        }

        private static void VerifySchedule()
        {
            var date = new DateTime(2026, 11, 20);
            Countdown beforeWork = Schedule.Default.At(date.AddHours(8));
            Require(beforeWork.Remaining == TimeSpan.FromHours(10) && beforeWork.Progress == 0,
                "Countdown and progress before a daytime shift");
            Countdown daytime = Schedule.Default.At(date.AddHours(14));
            Require(daytime.Remaining == TimeSpan.FromHours(4) && Math.Abs(daytime.Progress - 5.0 / 9) < 0.0001,
                "Countdown and progress during a daytime shift");

            var nightShift = new Schedule(TimeSpan.FromHours(22), TimeSpan.FromHours(6));
            Countdown beforeMidnight = nightShift.At(date.AddHours(23));
            Countdown afterMidnight = nightShift.At(date.AddDays(1).AddHours(2));
            Countdown finished = nightShift.At(date.AddDays(1).AddHours(6));
            Require(beforeMidnight.Remaining == TimeSpan.FromHours(7) && beforeMidnight.Progress == 0.125 &&
                afterMidnight.Remaining == TimeSpan.FromHours(4) && afterMidnight.Progress == 0.5 &&
                finished.Finished && finished.Progress == 1, "Countdown and progress across midnight until the shift ends");
        }

        private static void VerifySettings(string folder)
        {
            string path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".txt");
            var store = new SettingsFile(path);
            try
            {
                File.WriteAllLines(path, new[] { "09:20", "18:30" });
                ClockSettings legacy = store.Load();
                Require(legacy.Schedule.Start == new TimeSpan(9, 20, 0) &&
                    legacy.Display == DisplayMode.Clock && legacy.Monitor == "",
                    "Legacy settings were not preserved");
                RequireDefaultAppearance(legacy, "Two-line settings");

                File.WriteAllLines(path, new[] { "09:20", "18:30", "Minutes", "legacy-monitor" });
                ClockSettings previous = store.Load();
                Require(previous.Display == DisplayMode.Minutes && previous.Monitor == "legacy-monitor",
                    "Four-line settings did not preserve display and monitor");
                RequireDefaultAppearance(previous, "Four-line settings");

                File.WriteAllLines(path, new[] { "09:20", "18:30", "Minutes", "legacy-monitor", "bad", "bad", "bad", "bad" });
                ClockSettings malformed = store.Load();
                Require(malformed.Schedule.End == legacy.Schedule.End && malformed.Display == previous.Display &&
                    malformed.Monitor == previous.Monitor, "Malformed appearance erased existing settings");
                RequireDefaultAppearance(malformed, "Malformed appearance settings");

                Screen target = FirstSecondary() ?? Screen.PrimaryScreen;
                using (var dialog = new ClockSettingsDialog(legacy, null))
                {
                    var display = (ComboBox)dialog.Controls["DisplayMode"];
                    var monitor = (ComboBox)dialog.Controls["Monitor"];
                    var theme = (ComboBox)dialog.Controls["Theme"];
                    var transparency = (TrackBar)dialog.Controls["Transparency"];
                    var showProgress = (CheckBox)dialog.Controls["ShowProgress"];
                    var notifyAtEnd = (CheckBox)dialog.Controls["NotifyAtEnd"];
                    Require(display.Items.Count == 9 && display.SelectedIndex == 0 &&
                        display.Items[1].ToString() == "시간만", "Format selector");
                    Require(monitor.Items.Count == Screen.AllScreens.Length + 1 && monitor.SelectedIndex == 0,
                        "Monitor selector");
                    Require(transparency.Minimum == 0 && transparency.Maximum == 70 && transparency.Value == 0 &&
                        showProgress.Checked && !notifyAtEnd.Checked && theme.SelectedIndex == (int)ClockTheme.Dark,
                        "Default appearance controls");
                    var previewChanges = new List<Tuple<int, bool, ClockTheme>>();
                    dialog.PreviewAppearance = (value, visible, selected) => previewChanges.Add(Tuple.Create(value, visible, selected));
                    dialog.StoreSettings = store.Save;
                    dialog.Shown += delegate
                    {
                        display.SelectedIndex = (int)DisplayMode.Earnings;
                        var salaryInput = (TextBox)dialog.Controls["AnnualSalary"];
                        var salaryText = (Label)dialog.Controls["AnnualSalaryInKorean"];
                        salaryInput.Text = "123456789";
                        Require(salaryText.Text == "일억 이천삼백사십오만 육천칠백팔십구 원", "Preview reads 억 and 만");
                        salaryInput.Text = "50,400,000";
                        Require(salaryText.Text == "오천사십만 원", "Preview updates while typing");
                        int updateClicks = 0;
                        dialog.CheckForUpdate = delegate { updateClicks++; };
                        ((Button)dialog.Controls["CheckForUpdate"]).PerformClick();
                        Require(updateClicks == 1, "Manual update button must invoke the action");
                        ((CheckBox)dialog.Controls["AutoUpdates"]).Checked = true;
                        monitor.SelectedIndex = target.Primary ? 1 : 2;
                        transparency.Value = ClockSettings.MaxTransparency;
                        showProgress.Checked = false;
                        theme.SelectedIndex = (int)ClockTheme.Light;
                        notifyAtEnd.Checked = true;
                        dialog.AcceptButton.PerformClick();
                    };
                    Require(dialog.ShowDialog() == DialogResult.OK, "Save did not close settings");
                    Require(previewChanges.SequenceEqual(new[]
                    {
                        Tuple.Create(70, true, ClockTheme.Dark), Tuple.Create(70, false, ClockTheme.Dark),
                        Tuple.Create(70, false, ClockTheme.Light)
                    }), "Transparency, progress and theme must preview their values");
                }
                ClockSettings saved = store.Load();
                Require(saved.Schedule.Start == legacy.Schedule.Start && saved.Schedule.End == legacy.Schedule.End &&
                    saved.Display == DisplayMode.Earnings && saved.Monitor == target.DeviceName &&
                    saved.AnnualSalary == 50400000 && saved.AutoUpdate,
                    "Saved display or schedule was lost");
                Require(saved.Transparency == 70 && !saved.ShowProgress && saved.NotifyAtEnd && saved.Theme == ClockTheme.Light,
                    "Saved appearance or reminder settings were lost");

                string[] savedLines = File.ReadAllLines(path);
                Require(savedLines.Length == 11 && savedLines[0] == "09:20" && savedLines[1] == "18:30" &&
                    savedLines[2] == "Earnings" && savedLines[3] == target.DeviceName,
                    "Existing settings fields must keep their positions");
                string savedUpdateMode = savedLines[10];
                savedLines[10] = "True";
                File.WriteAllLines(path, savedLines);
                Require(!store.Load().AutoUpdate, "Old check-only preference must not opt in to installation");
                savedLines[10] = savedUpdateMode;
                File.WriteAllLines(path, savedLines);
                using (var dialog = new ClockSettingsDialog(saved, null))
                {
                    var transparency = (TrackBar)dialog.Controls["Transparency"];
                    var theme = (ComboBox)dialog.Controls["Theme"];
                    var showProgress = (CheckBox)dialog.Controls["ShowProgress"];
                    var notifyAtEnd = (CheckBox)dialog.Controls["NotifyAtEnd"];
                    Require(transparency.Value == 70 && !showProgress.Checked && notifyAtEnd.Checked &&
                        theme.SelectedIndex == (int)ClockTheme.Light,
                        "Appearance and reminder settings did not load into the dialog");
                    bool storeCalled = false;
                    dialog.StoreSettings = delegate { storeCalled = true; };
                    dialog.Shown += delegate
                    {
                        transparency.Value = 0;
                        showProgress.Checked = true;
                        notifyAtEnd.Checked = false;
                        theme.SelectedIndex = (int)ClockTheme.Dark;
                        dialog.CancelButton.PerformClick();
                    };
                    Require(dialog.ShowDialog() == DialogResult.Cancel && !storeCalled,
                        "Cancel should close the dialog without committing changes");
                }
                Require(store.Load().Theme == ClockTheme.Light, "Cancel must keep the saved theme");
            }
            finally { File.Delete(path); }
        }

        private static void RequireDefaultAppearance(ClockSettings settings, string scenario)
        {
            Require(settings.Transparency == 0 && settings.ShowProgress && !settings.NotifyAtEnd &&
                settings.Theme == ClockTheme.Dark,
                scenario + " should use default appearance and reminder settings");
        }

        private static Screen FirstSecondary()
        {
            return Screen.AllScreens.Where(screen => !screen.Primary)
                .OrderBy(screen => screen.Bounds.Left).ThenBy(screen => screen.Bounds.Top).FirstOrDefault();
        }

        private static void VerifyMonitorSelection()
        {
            Screen target = FirstSecondary() ?? Screen.PrimaryScreen;
            Taskbar selected = TaskbarDock.FindForDisplay(target.DeviceName).Single();
            Require(selected.Screen.DeviceName == target.DeviceName, "Selected monitor");
            Require(TaskbarDock.FindForDisplay("disconnected-monitor").Single().Screen.Primary,
                "Missing monitor should fall back to the primary screen");
        }

        private static void VerifyPinnedPlacement()
        {
            Screen screen = Screen.PrimaryScreen;
            Rectangle bar = new Rectangle(screen.Bounds.Left, screen.Bounds.Bottom - 28, screen.Bounds.Width, 28);
            var taskbar = new Taskbar(IntPtr.Zero, bar, 3, screen, false);
            var dock = new TaskbarDock();
            Size widget = new Size(235, 34);
            Rectangle pinned = dock.Place(taskbar, widget, 7, true);
            Require(bar.Contains(pinned) && pinned.Height == 26,
                "Missing task list and short bar must keep the widget inside the taskbar");
            Require(!bar.IntersectsWith(dock.Place(taskbar, widget, 7, false)),
                "Automatic placement must still fall back outside an unmeasured bar");
            Rectangle tray = new Rectangle(bar.Right - 210, bar.Top, 210, bar.Height);
            pinned = TaskbarDock.PinnedPlacement(bar, tray, Rectangle.Empty, pinned.Size, 7);
            Require(bar.Contains(pinned) && pinned.Right == tray.Left - 7, "Keep tray icons clear");
        }

        private static void VerifyColors()
        {
            int mint = Color.FromArgb(123, 232, 184).ToArgb();
            int amber = Color.FromArgb(255, 194, 134).ToArgb();
            int red = Color.FromArgb(255, 106, 112).ToArgb();
            using (var strip = new ClockStrip(1))
            using (var bitmap = new Bitmap(strip.Width, strip.Height))
            using (var reference = new Bitmap(strip.Width, strip.Height))
            {
                var cases = new[]
                {
                    Tuple.Create(TimeSpan.FromMinutes(31), mint),
                    Tuple.Create(TimeSpan.FromMinutes(30), amber),
                    Tuple.Create(TimeSpan.FromMinutes(10).Add(TimeSpan.FromTicks(1)), amber),
                    Tuple.Create(TimeSpan.FromMinutes(10), red),
                    Tuple.Create(TimeSpan.FromMilliseconds(1), red),
                    Tuple.Create(TimeSpan.Zero, mint)
                };
                foreach (DisplayMode mode in Enum.GetValues(typeof(DisplayMode)))
                {
                    foreach (var entry in cases)
                    {
                        strip.Display(new Countdown(entry.Item1, 0.75), mode);
                        strip.DrawToBitmap(bitmap, strip.ClientRectangle);
                        Require(bitmap.GetPixel(20, 28).ToArgb() == entry.Item2, mode + " color at " + entry.Item1);
                    }
                }

                var longest = new Countdown(TimeSpan.FromDays(1) - TimeSpan.FromTicks(1), 0);
                strip.Display(longest, DisplayMode.Seconds);
                strip.DrawToBitmap(reference, strip.ClientRectangle);
                strip.Display(longest, DisplayMode.Binary);
                strip.DrawToBitmap(bitmap, strip.ClientRectangle);
                for (int x = 31; x < 83; x++)
                    for (int y = 4; y < 24; y++)
                        Require(bitmap.GetPixel(x, y) == reference.GetPixel(x, y), "Wide binary text overlaps the label");

                strip.SetAppearance(0, true, ClockTheme.Light);
                strip.Display(new Countdown(TimeSpan.FromMinutes(10), 0.75), DisplayMode.Clock);
                strip.DrawToBitmap(bitmap, strip.ClientRectangle);
                Require(bitmap.GetPixel(47, 4).ToArgb() == Color.FromArgb(246, 248, 251).ToArgb() &&
                    bitmap.GetPixel(20, 28).ToArgb() == Color.FromArgb(181, 30, 54).ToArgb(),
                    "Light theme must use a bright panel and dark red in the final ten minutes");
                strip.SetAppearance(0, true, ClockTheme.Dark);
                strip.DrawToBitmap(bitmap, strip.ClientRectangle);
                Require(bitmap.GetPixel(47, 4).ToArgb() == Color.FromArgb(26, 32, 43).ToArgb(),
                    "Switching back restores the dark panel");
            }
        }

        private static void VerifyFinishNotice()
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            using (var notice = new FinishNotice(1, ClockTheme.Light, area))
            using (var watchdog = new Timer { Interval = 6000 })
            {
                bool painted = false;
                bool timedOut = false;
                notice.Shown += delegate
                {
                    using (var bitmap = new Bitmap(notice.Width, notice.Height))
                    {
                        notice.DrawToBitmap(bitmap, notice.ClientRectangle);
                        painted = bitmap.GetPixel(238, 58).ToArgb() == Color.FromArgb(246, 248, 251).ToArgb();
                    }
                };
                watchdog.Tick += delegate { timedOut = true; notice.Close(); };
                watchdog.Start();
                Require(area.Contains(notice.Bounds) && !notice.ShowInTaskbar, "Notice must fit above the taskbar");
                Application.Run(notice);
                Require(painted && !timedOut, "Notice did not render and dismiss itself");
            }
        }

        private static void VerifyAppearance()
        {
            using (var strip = new ClockStrip(1))
            using (var visible = new Bitmap(strip.Width, strip.Height))
            using (var hidden = new Bitmap(strip.Width, strip.Height))
            {
                strip.Display(new Countdown(TimeSpan.FromMinutes(10), 0.5), DisplayMode.Clock);
                strip.DrawToBitmap(visible, strip.ClientRectangle);
                Require(strip.Opacity == 1, "Default opacity");
                Require(visible.GetPixel(20, 28).ToArgb() == Color.FromArgb(255, 106, 112).ToArgb(), "Progress fill");
                Require(visible.GetPixel(210, 28).ToArgb() == Color.FromArgb(60, 72, 90).ToArgb(), "Progress rail");

                Size clientSize = strip.ClientSize;
                strip.SetAppearance(35, false);
                Require(Math.Abs(strip.Opacity - 0.65) < 0.0001 && strip.ClientSize == clientSize,
                    "Transparency should change opacity while preserving widget dimensions");
                strip.DrawToBitmap(hidden, strip.ClientRectangle);
                int background = Color.FromArgb(26, 32, 43).ToArgb();
                Require(hidden.GetPixel(20, 28).ToArgb() == background && hidden.GetPixel(210, 28).ToArgb() == background,
                    "Hiding progress should remove both the rail and fill");
                for (int x = 8; x < 226; x++)
                    for (int y = 4; y < 25; y++)
                        Require(hidden.GetPixel(x, y) == visible.GetPixel(x, y), "Hiding progress changed the clock content");

                strip.SetAppearance(ClockSettings.MaxTransparency, true);
                Require(Math.Abs(strip.Opacity - 0.30) < 0.0001, "Maximum transparency");
                strip.DrawToBitmap(hidden, strip.ClientRectangle);
                Require(hidden.GetPixel(20, 28) == visible.GetPixel(20, 28) &&
                    hidden.GetPixel(210, 28) == visible.GetPixel(210, 28), "Reenabling progress should restore the rail and fill");

                IntPtr handle = strip.Handle;
                strip.SetAppearance(54, true);
                uint colorKey, flags;
                byte alpha;
                Require(GetLayeredWindowAttributes(handle, out colorKey, out alpha, out flags),
                    "A partially transparent strip should have layered window attributes");
                const uint useColorKey = 1, useAlpha = 2, magentaColorRef = 0x00ff00ff;
                Require(flags == (useColorKey | useAlpha) && colorKey == magentaColorRef && Math.Abs(alpha - 117) <= 1,
                    "The native window should use both magenta corner clipping and 46 percent opacity");
            }
        }

        private static void VerifyFinishReminder()
        {
            Schedule dayShift = Schedule.Default;
            var deadline = new DateTime(2026, 11, 20, 18, 0, 0);
            var reminder = new FinishReminder(deadline.AddMinutes(-5));
            Require(!reminder.ShouldNotify(dayShift, deadline.AddTicks(-1)), "Reminder fired before the deadline");
            Require(reminder.ShouldNotify(dayShift, deadline), "Reminder did not fire at the deadline");
            Require(!reminder.ShouldNotify(dayShift, deadline), "Reminder repeated at the same instant");
            Require(!reminder.ShouldNotify(dayShift, deadline.AddSeconds(1)), "Reminder repeated after the deadline");
            Require(!reminder.ShouldNotify(dayShift, deadline.AddMinutes(-2)), "Reminder fired on clock rollback");
            Require(!reminder.ShouldNotify(dayShift, deadline.AddSeconds(2)), "Reminder repeated after crossing the same deadline again");

            var startedAtDeadline = new FinishReminder(deadline);
            Require(!startedAtDeadline.ShouldNotify(dayShift, deadline.AddSeconds(10)), "A launch at the deadline fired a reminder");
            var startedLate = new FinishReminder(deadline.AddSeconds(10));
            Require(!startedLate.ShouldNotify(dayShift, deadline.AddSeconds(20)), "A late launch fired a reminder");

            var resumedInTime = new FinishReminder(deadline.AddMinutes(-5));
            Require(resumedInTime.ShouldNotify(dayShift, deadline.AddMinutes(1)), "Reminder did not fire at the one-minute grace boundary");
            var resumedTooLate = new FinishReminder(deadline.AddMinutes(-5));
            Require(!resumedTooLate.ShouldNotify(dayShift, deadline.AddMinutes(1).AddTicks(1)), "Reminder fired after the grace period");

            var reset = new FinishReminder(deadline.AddMinutes(-5));
            reset.Reset(deadline.AddSeconds(10));
            Require(!reset.ShouldNotify(dayShift, deadline.AddSeconds(20)), "Reset caused a reminder for a past deadline");
            var nextDeadline = deadline.AddDays(1);
            Require(!reminder.ShouldNotify(dayShift, nextDeadline.AddMinutes(-1)), "Reminder fired before the next workday finished");
            Require(reminder.ShouldNotify(dayShift, nextDeadline), "A prior workday's reminder blocked the next workday");

            var nightShift = new Schedule(TimeSpan.FromHours(22), TimeSpan.Zero);
            var midnight = deadline.Date.AddDays(1);
            var overnight = new FinishReminder(midnight.AddTicks(-1));
            Require(overnight.ShouldNotify(nightShift, midnight.AddSeconds(10)), "Overnight reminder missed the deadline across midnight");
            Require(!overnight.ShouldNotify(nightShift, midnight.AddSeconds(20)), "Overnight reminder repeated");

            var lateDayShift = new Schedule(TimeSpan.FromHours(9), new TimeSpan(23, 59, 0));
            var nextDate = new FinishReminder(midnight.AddMinutes(-1).AddSeconds(-1));
            Require(nextDate.ShouldNotify(lateDayShift, midnight), "Reminder lost a deadline exactly one minute before midnight");

            var longNightShift = new Schedule(TimeSpan.FromHours(18), new TimeSpan(17, 59, 0));
            var nextShift = new FinishReminder(deadline.AddMinutes(-1).AddSeconds(-1));
            Require(nextShift.ShouldNotify(longNightShift, deadline), "Reminder lost a deadline exactly one minute before the next shift");
        }

        private static void VerifyPowerResume()
        {
            using (var strip = new ClockStrip(1f))
            {
                IntPtr handle = strip.Handle;
                int resumes = 0;
                strip.PowerResumed += delegate { resumes++; };
                SendMessage(handle, 0x0218, new IntPtr(0x04), IntPtr.Zero);
                Require(resumes == 0, "Suspend triggered display recovery");
                SendMessage(handle, 0x0218, new IntPtr(0x12), IntPtr.Zero);
                Require(resumes == 1, "Automatic resume did not trigger display recovery");
                SendMessage(handle, 0x0218, new IntPtr(0x07), IntPtr.Zero);
                Require(resumes == 2, "User resume did not trigger display recovery");
            }
        }

        private static void VerifyPayEstimate()
        {
            const long salary = 50400000;
            var dayShift = Schedule.Default;
            var workday = new DateTime(2026, 11, 23);
            Require(PayEstimate.At(salary, workday, dayShift.At(workday.AddHours(8)).Progress) == 0, "Before work");
            Require(PayEstimate.At(salary, workday, dayShift.At(workday.AddHours(13.5)).Progress) == 100000, "Half day");
            Require(PayEstimate.At(salary, workday, dayShift.At(workday.AddHours(18)).Progress) == 200000, "November weekdays");
            Require(PayEstimate.At(salary, new DateTime(2027, 2, 1), 1) == 210000, "Different month");
            Require(PayEstimate.At(salary, workday.AddDays(-1), 1) == 0, "Sunday");
            var overnight = new Schedule(TimeSpan.FromHours(22), TimeSpan.FromHours(6));
            Require(overnight.WorkDateAt(new DateTime(2026, 5, 1, 2, 0, 0)) == new DateTime(2026, 4, 30),
                "An overnight shift should use its starting date");
        }

        private static void VerifyReleaseCheck()
        {
            const string sha = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
            string metadata = "{\"tag_name\":\"v1.2.0\",\"assets\":[{\"name\":\"OffClock.zip\",\"digest\":\"sha256:" + sha + "\"}]}";
            var update = ReleaseUpdate.FromJson(metadata, new Version(1, 1, 0, 0));
            Require(update != null && update.Version == new Version(1, 2, 0, 0) && update.Digest == sha,
                "A newer release must match the package and include a digest");
            Require(ReleaseUpdate.FromJson(metadata, new Version(1, 2, 0, 0)) == null, "Do not reinstall the same version");
            Require(ReleaseUpdate.FromJson(metadata.Replace("v1.2.0", "v1.0.0"), new Version(1, 1, 0, 0)) == null,
                "Do not downgrade");
            try
            {
                ReleaseUpdate.FromJson(metadata.Replace(sha, "invalid"), new Version(1, 1, 0, 0));
                throw new InvalidOperationException("Accepted an invalid update digest");
            }
            catch (InvalidDataException) { }
        }

        [DllImport("user32.dll", EntryPoint = "SendMessageW")]
        private static extern IntPtr SendMessage(IntPtr handle, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetLayeredWindowAttributes(IntPtr window, out uint colorKey, out byte alpha, out uint flags);

        private static void Require(bool value, string message)
        {
            if (!value) throw new InvalidOperationException(message);
        }
    }
}
