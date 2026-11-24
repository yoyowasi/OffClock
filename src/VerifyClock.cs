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
                VerifyUpdateFailure(args[0]);
                VerifySettings(args[0]);
                VerifyLoginStartup(args[0]);
                VerifyMonitorSelection();
                VerifyPinnedPlacement();
                VerifyPowerResume();
                VerifyColors();
                VerifyAppearance();
                VerifyThemes();
                VerifyProgressColor(args[0]);
                VerifyFinishReminder();
                VerifyFinishNotice();
                VerifyLunchBreak();
                VerifyLunchSettings(args[0]);
                VerifyLunchDialog();
                VerifyLunchReminder();
                VerifyLunchNotice();
                VerifyMilestones();
                VerifyLunchStrip();
                Console.WriteLine("Verified countdown, lunch break, settings, placement, power-resume messages, appearance, and silent notifications.");
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
                    string[] themeLabels = { "다크", "라이트", "벚꽃", "바다", "숲", "노을", "고양이", "곰", "토끼", "펭귄", "병아리" };
                    Require(theme.Items.Count == themeLabels.Length, "Theme combo must list every theme");
                    for (int i = 0; i < themeLabels.Length; i++)
                        Require(theme.Items[i].ToString() == themeLabels[i], "Theme label " + i);
                    var previewChanges = new List<Tuple<int, bool, ClockTheme, int>>();
                    dialog.PreviewAppearance = (value, visible, selected, barColor) =>
                        previewChanges.Add(Tuple.Create(value, visible, selected, barColor.ToArgb()));
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
                        Tuple.Create(70, true, ClockTheme.Dark, Color.Empty.ToArgb()),
                        Tuple.Create(70, false, ClockTheme.Dark, Color.Empty.ToArgb()),
                        Tuple.Create(70, false, ClockTheme.Light, Color.Empty.ToArgb())
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
                Require(savedLines.Length == 13 && savedLines[0] == "09:20" && savedLines[1] == "18:30" &&
                    savedLines[2] == "Earnings" && savedLines[3] == target.DeviceName && savedLines[11] == "Theme" &&
                    savedLines[12] == "Off 12:00-13:00",
                    "Existing settings fields must keep their positions, line 11 must default to Theme and line 12 to the lunch default");
                string savedUpdateMode = savedLines[10];
                savedLines[10] = "True";
                File.WriteAllLines(path, savedLines);
                Require(!store.Load().AutoUpdate, "Old check-only preference must not opt in to installation");
                savedLines[10] = savedUpdateMode;
                File.WriteAllLines(path, savedLines);

                File.WriteAllLines(path, savedLines.Take(11));
                RequireSameExceptColor(store.Load(), saved, "Eleven-line settings");
                foreach (string badColor in new[] { "#ZZZ", "red" })
                {
                    savedLines[11] = badColor;
                    File.WriteAllLines(path, savedLines);
                    RequireSameExceptColor(store.Load(), saved, "Malformed progress color " + badColor);
                }
                savedLines[11] = "#123456";
                File.WriteAllLines(path, savedLines);
                ClockSettings coloredSettings = store.Load();
                Require(coloredSettings.ProgressColor.ToArgb() == Color.FromArgb(255, 18, 52, 86).ToArgb() &&
                    coloredSettings.Theme == ClockTheme.Light && coloredSettings.Transparency == 70,
                    "A hex progress color must load as an opaque color without disturbing other fields");
                savedLines[11] = "Theme";
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
                settings.Theme == ClockTheme.Dark && settings.ProgressColor.IsEmpty,
                scenario + " should use default appearance and reminder settings");
        }

        private static void RequireSameExceptColor(ClockSettings actual, ClockSettings expected, string scenario)
        {
            Require(actual.ProgressColor.IsEmpty, scenario + " must load an empty progress color");
            Require(actual.Schedule.Start == expected.Schedule.Start && actual.Schedule.End == expected.Schedule.End &&
                actual.Display == expected.Display && actual.Monitor == expected.Monitor &&
                actual.AnnualSalary == expected.AnnualSalary && actual.AutoUpdate == expected.AutoUpdate &&
                actual.Transparency == expected.Transparency && actual.ShowProgress == expected.ShowProgress &&
                actual.NotifyAtEnd == expected.NotifyAtEnd && actual.Theme == expected.Theme,
                scenario + " must not disturb the other settings");
        }

        private static Color Accent(ClockPalette palette, TimeSpan remaining)
        {
            if (remaining > TimeSpan.Zero && remaining <= TimeSpan.FromMinutes(10)) return palette.Urgent;
            if (remaining > TimeSpan.Zero && remaining <= TimeSpan.FromMinutes(30)) return palette.Soon;
            return palette.Normal;
        }

        private static void VerifyThemes()
        {
            var themes = (ClockTheme[])Enum.GetValues(typeof(ClockTheme));
            Require(themes.Length == 11 && (int)ClockTheme.Dark == 0 && (int)ClockTheme.Light == 1 &&
                (int)ClockTheme.Sakura == 2 && (int)ClockTheme.Cat == 6 && (int)ClockTheme.Chick == 10,
                "Theme enum order");
            ClockPalette dark = ClockPalette.For(ClockTheme.Dark);
            ClockPalette light = ClockPalette.For(ClockTheme.Light);
            Require(dark.Background.ToArgb() == Color.FromArgb(26, 32, 43).ToArgb() &&
                dark.Track.ToArgb() == Color.FromArgb(60, 72, 90).ToArgb() &&
                dark.Normal.ToArgb() == Color.FromArgb(123, 232, 184).ToArgb() &&
                dark.Soon.ToArgb() == Color.FromArgb(255, 194, 134).ToArgb() &&
                dark.Urgent.ToArgb() == Color.FromArgb(255, 106, 112).ToArgb(), "Dark palette is unchanged");
            Require(light.Background.ToArgb() == Color.FromArgb(246, 248, 251).ToArgb() &&
                light.Urgent.ToArgb() == Color.FromArgb(181, 30, 54).ToArgb(), "Light palette is unchanged");

            var characters = new[] { ClockTheme.Cat, ClockTheme.Bear, ClockTheme.Rabbit, ClockTheme.Penguin, ClockTheme.Chick };
            var backgrounds = new HashSet<int>();
            Color custom = Color.FromArgb(18, 52, 86);
            var states = new[]
            {
                TimeSpan.FromMinutes(90), TimeSpan.FromMinutes(30), TimeSpan.FromMinutes(10), TimeSpan.Zero
            };
            using (var strip = new ClockStrip(1))
            using (var bitmap = new Bitmap(strip.Width, strip.Height))
            using (var running = new Bitmap(strip.Width, strip.Height))
            using (var finished = new Bitmap(strip.Width, strip.Height))
            {
                foreach (ClockTheme theme in themes)
                {
                    ClockPalette palette = ClockPalette.For(theme);
                    Require(backgrounds.Add(palette.Background.ToArgb()), theme + " must have its own background");

                    strip.SetAppearance(0, true, theme);
                    foreach (TimeSpan remaining in states)
                    {
                        strip.Display(new Countdown(remaining, 0.75), DisplayMode.Clock);
                        strip.DrawToBitmap(bitmap, strip.ClientRectangle);
                        Require(bitmap.GetPixel(47, 4).ToArgb() == palette.Background.ToArgb(), theme + " background at " + remaining);
                        Require(bitmap.GetPixel(210, 28).ToArgb() == palette.Track.ToArgb(), theme + " rail at " + remaining);
                        Require(bitmap.GetPixel(20, 28).ToArgb() == Accent(palette, remaining).ToArgb(),
                            theme + " state accent at " + remaining);
                    }

                    strip.SetAppearance(0, true, theme, custom);
                    foreach (TimeSpan remaining in states)
                    {
                        strip.Display(new Countdown(remaining, 0.75), DisplayMode.Clock);
                        strip.DrawToBitmap(bitmap, strip.ClientRectangle);
                        Require(bitmap.GetPixel(20, 28).ToArgb() == Color.FromArgb(255, 18, 52, 86).ToArgb(),
                            theme + " custom fill at " + remaining);
                        Require(bitmap.GetPixel(210, 28).ToArgb() == palette.Track.ToArgb(),
                            theme + " custom rail at " + remaining);
                        Require(bitmap.GetPixel(47, 4).ToArgb() == palette.Background.ToArgb(),
                            theme + " custom background at " + remaining);
                    }
                    strip.SetAppearance(0, true, theme, Color.Empty);
                    strip.Display(new Countdown(TimeSpan.FromMinutes(10), 0.75), DisplayMode.Clock);
                    strip.DrawToBitmap(bitmap, strip.ClientRectangle);
                    Require(bitmap.GetPixel(20, 28).ToArgb() == palette.Urgent.ToArgb(),
                        theme + " must return to the state accent when the custom color is cleared");

                    strip.Display(new Countdown(TimeSpan.FromHours(1), 0.75), DisplayMode.Clock);
                    strip.DrawToBitmap(running, strip.ClientRectangle);
                    strip.Display(new Countdown(TimeSpan.Zero, 1), DisplayMode.Clock);
                    strip.DrawToBitmap(finished, strip.ClientRectangle);
                    int different = 0, drawn = 0;
                    for (int x = 6; x <= 28; x++)
                        for (int y = 2; y <= 24; y++)
                        {
                            if (running.GetPixel(x, y).ToArgb() != finished.GetPixel(x, y).ToArgb()) different++;
                            if (running.GetPixel(x, y).ToArgb() != palette.Background.ToArgb()) drawn++;
                        }
                    Require(different > 0, theme + " icon must look different when finished");
                    if (Array.IndexOf(characters, theme) >= 0)
                    {
                        Require(drawn > 0, theme + " must draw a mascot in the icon area");
                        VerifyMascotStages(strip, theme);
                    }
                }
            }
        }

        private static void VerifyMascotStages(ClockStrip strip, ClockTheme theme)
        {
            strip.SetAppearance(0, true, theme);
            var remainings = new[]
            {
                TimeSpan.FromHours(1), TimeSpan.FromMinutes(25), TimeSpan.FromMinutes(5), TimeSpan.Zero
            };
            var names = new[] { "calm", "blush", "sweat", "finished" };
            var renders = new List<int[]>();
            var sweatFound = new bool[remainings.Length];
            using (var bitmap = new Bitmap(strip.Width, strip.Height))
            {
                for (int stage = 0; stage < remainings.Length; stage++)
                {
                    double progress = remainings[stage] == TimeSpan.Zero ? 1 : 0.75;
                    strip.Display(new Countdown(remainings[stage], progress), DisplayMode.Clock);
                    strip.DrawToBitmap(bitmap, strip.ClientRectangle);
                    var pixels = new List<int>();
                    for (int x = 6; x <= 28; x++)
                        for (int y = 2; y <= 24; y++)
                            pixels.Add(bitmap.GetPixel(x, y).ToArgb());
                    renders.Add(pixels.ToArray());
                    for (int x = 23; x <= 28; x++)
                        for (int y = 7; y <= 13; y++)
                        {
                            Color pixel = bitmap.GetPixel(x, y);
                            if (Math.Abs(pixel.R - 91) <= 12 && Math.Abs(pixel.G - 184) <= 12 && Math.Abs(pixel.B - 245) <= 12)
                                sweatFound[stage] = true;
                        }
                }
            }
            for (int a = 0; a < renders.Count; a++)
                for (int b = a + 1; b < renders.Count; b++)
                    Require(!renders[a].SequenceEqual(renders[b]),
                        theme + " mascot must differ between " + names[a] + " and " + names[b]);
            Require(!sweatFound[0] && !sweatFound[1] && sweatFound[2],
                theme + " sweat drop must appear only in the last ten minutes");
        }

        private static void VerifyProgressColor(string folder)
        {
            string path = Path.Combine(folder, Guid.NewGuid().ToString("N") + ".txt");
            var store = new SettingsFile(path);
            Color custom = Color.FromArgb(18, 52, 86);
            int customArgb = Color.FromArgb(255, 18, 52, 86).ToArgb();
            int rabbitNormal = ClockPalette.For(ClockTheme.Rabbit).Normal.ToArgb();
            try
            {
                var previews = new List<Tuple<int, bool, ClockTheme, int>>();
                Color requested = Color.Empty;
                using (var dialog = new ClockSettingsDialog(ClockSettings.Default, null))
                {
                    var theme = (ComboBox)dialog.Controls["Theme"];
                    var showProgress = (CheckBox)dialog.Controls["ShowProgress"];
                    var pick = (Button)dialog.Controls["ProgressColor"];
                    var reset = (Button)dialog.Controls["ResetProgressColor"];
                    var swatch = (Label)dialog.Controls["ProgressColorSwatch"];
                    Require(pick.Text == "색 변경" && reset.Text == "기본값", "Progress color button labels");
                    Require(dialog.SelectedProgressColor.IsEmpty && pick.Enabled && !reset.Enabled &&
                        swatch.BackColor.ToArgb() == ClockPalette.For(ClockTheme.Dark).Normal.ToArgb(),
                        "Default progress color state");
                    dialog.PreviewAppearance = (value, visible, selected, barColor) =>
                        previews.Add(Tuple.Create(value, visible, selected, barColor.ToArgb()));
                    dialog.StoreSettings = store.Save;
                    dialog.Shown += delegate
                    {
                        theme.SelectedIndex = (int)ClockTheme.Rabbit;
                        Require(previews.Count == 1 && swatch.BackColor.ToArgb() == rabbitNormal,
                            "Swatch follows the theme while no custom color is set");

                        dialog.PickProgressColor = delegate(Color shown) { requested = shown; return null; };
                        pick.PerformClick();
                        Require(requested.ToArgb() == rabbitNormal && previews.Count == 1 &&
                            dialog.SelectedProgressColor.IsEmpty && swatch.BackColor.ToArgb() == rabbitNormal,
                            "Cancelling the picker must change nothing and fire no preview");

                        dialog.PickProgressColor = delegate(Color shown) { requested = shown; return custom; };
                        pick.PerformClick();
                        Require(requested.ToArgb() == rabbitNormal && previews.Count == 2 &&
                            dialog.SelectedProgressColor.ToArgb() == customArgb && swatch.BackColor.ToArgb() == customArgb &&
                            reset.Enabled &&
                            previews[1].Equals(Tuple.Create(0, true, ClockTheme.Rabbit, customArgb)),
                            "Picking a color must update the swatch and preview it");

                        theme.SelectedIndex = (int)ClockTheme.Penguin;
                        Require(previews.Count == 3 && swatch.BackColor.ToArgb() == customArgb &&
                            previews[2].Equals(Tuple.Create(0, true, ClockTheme.Penguin, customArgb)),
                            "A custom color must survive a theme change");
                        theme.SelectedIndex = (int)ClockTheme.Rabbit;

                        showProgress.Checked = false;
                        Require(!pick.Enabled && !reset.Enabled, "Color buttons are disabled while the progress bar is hidden");
                        showProgress.Checked = true;
                        Require(pick.Enabled && reset.Enabled, "Color buttons return with the progress bar");
                        dialog.AcceptButton.PerformClick();
                    };
                    Require(dialog.ShowDialog() == DialogResult.OK, "Save did not close settings");
                }
                string[] lines = File.ReadAllLines(path);
                Require(lines.Length == 13 && lines[11] == "#123456", "Custom progress color must be saved as #RRGGBB");
                ClockSettings saved = store.Load();
                Require(saved.Theme == ClockTheme.Rabbit && saved.ProgressColor.ToArgb() == customArgb,
                    "Character theme and custom color must round trip");

                bool storeCalled = false;
                var secondPreviews = new List<Tuple<int, bool, ClockTheme, int>>();
                using (var dialog = new ClockSettingsDialog(saved, null))
                {
                    var theme = (ComboBox)dialog.Controls["Theme"];
                    var showProgress = (CheckBox)dialog.Controls["ShowProgress"];
                    var pick = (Button)dialog.Controls["ProgressColor"];
                    var reset = (Button)dialog.Controls["ResetProgressColor"];
                    var swatch = (Label)dialog.Controls["ProgressColorSwatch"];
                    Require(theme.SelectedIndex == (int)ClockTheme.Rabbit && swatch.BackColor.ToArgb() == customArgb &&
                        dialog.SelectedProgressColor.ToArgb() == customArgb, "Reopening must show the saved color");
                    dialog.PreviewAppearance = (value, visible, selected, barColor) =>
                        secondPreviews.Add(Tuple.Create(value, visible, selected, barColor.ToArgb()));
                    dialog.StoreSettings = delegate { storeCalled = true; };
                    dialog.Shown += delegate
                    {
                        Require(pick.Enabled && reset.Enabled, "Saved custom color enables both buttons");
                        reset.PerformClick();
                        Require(dialog.SelectedProgressColor.IsEmpty && swatch.BackColor.ToArgb() == rabbitNormal &&
                            !reset.Enabled && secondPreviews.Count == 1 &&
                            secondPreviews[0].Equals(Tuple.Create(0, true, ClockTheme.Rabbit, Color.Empty.ToArgb())),
                            "Reset must return to the theme color and preview it");
                        showProgress.Checked = false;
                        Require(!pick.Enabled && !reset.Enabled, "Buttons disabled while progress is hidden");
                        dialog.CancelButton.PerformClick();
                    };
                    Require(dialog.ShowDialog() == DialogResult.Cancel && !storeCalled, "Cancel must not store anything");
                }
                Require(store.Load().ProgressColor.ToArgb() == customArgb && File.ReadAllLines(path)[11] == "#123456",
                    "Cancel must keep the saved progress color");

                using (var dialog = new ClockSettingsDialog(saved, null))
                {
                    var reset = (Button)dialog.Controls["ResetProgressColor"];
                    dialog.StoreSettings = store.Save;
                    dialog.Shown += delegate
                    {
                        reset.PerformClick();
                        dialog.AcceptButton.PerformClick();
                    };
                    Require(dialog.ShowDialog() == DialogResult.OK, "Save after reset did not close settings");
                }
                Require(store.Load().ProgressColor.IsEmpty && File.ReadAllLines(path)[11] == "Theme" &&
                    store.Load().Theme == ClockTheme.Rabbit, "Reset then Save must store the theme default");
            }
            finally { File.Delete(path); }
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
            string metadata = "{\"tag_name\":\"v1.2.0\",\"body\":\"- 새로운 기능\\n- 오류 수정\",\"assets\":[{\"name\":\"OffClock.zip\",\"digest\":\"sha256:" + sha + "\"}]}";
            var update = ReleaseUpdate.FromJson(metadata, new Version(1, 1, 0, 0));
            Require(update != null && update.Version == new Version(1, 2, 0, 0) && update.Digest == sha,
                "A newer release must match the package and include a digest");
            Require(update.Notes == "- 새로운 기능\n- 오류 수정", "A new release preserves its GitHub notes");
            using (var dialog = new UpdateDialog(true))
            {
                var notes = (TextBox)dialog.Controls["ReleaseNotes"];
                Require(notes.ReadOnly && notes.Multiline && notes.Text.Contains("점심시간") &&
                    notes.Text.Contains("점심 끝까지"), "Show bundled release notes after installing");
            }
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

        private static void VerifyUpdateFailure(string folder)
        {
            string root = Path.Combine(folder, "failure-" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(root, "update-failed.txt");
            string nested = Path.Combine(Path.Combine(Path.Combine(root, "a"), "b"), "update-failed.txt");
            var version = new Version(1, 3, 0, 0);
            try
            {
                Require(UpdateFailure.Load(path) == null, "A missing record must load as null");
                UpdateFailure.Clear(path);

                UpdateFailure.Record(path, version, "다운로드 실패");
                UpdateFailure loaded = UpdateFailure.Load(path);
                Require(loaded != null && loaded.Version == version && loaded.Reason == "다운로드 실패",
                    "Single-line failure must round trip");

                string multiLine = "첫 줄" + Environment.NewLine + "둘째 줄" + Environment.NewLine + "셋째 줄";
                UpdateFailure.Record(path, new Version(1, 4, 0, 0), multiLine);
                loaded = UpdateFailure.Load(path);
                Require(loaded != null && loaded.Version == new Version(1, 4, 0, 0) && loaded.Reason == multiLine,
                    "A newer record must overwrite the older one and keep a multi-line reason");

                UpdateFailure.Record(path, version, null);
                loaded = UpdateFailure.Load(path);
                Require(loaded != null && loaded.Version == version && loaded.Reason == "", "A null reason must load as empty");

                Require(loaded.IsFor(new Version(1, 3, 0, 0)) && !loaded.IsFor(new Version(1, 3, 1, 0)) &&
                    !loaded.IsFor(new Version(1, 4, 0, 0)) && !loaded.IsFor(new Version(1, 2, 0, 0)),
                    "A failure must only match its own version");

                File.WriteAllText(path, "abc" + Environment.NewLine + "reason");
                Require(UpdateFailure.Load(path) == null, "A garbage version line must load as null");
                File.WriteAllText(path, "");
                Require(UpdateFailure.Load(path) == null, "An empty record must load as null");

                UpdateFailure.Record(nested, version, "중첩 폴더");
                loaded = UpdateFailure.Load(nested);
                Require(loaded != null && loaded.Version == version && loaded.Reason == "중첩 폴더",
                    "Record must create missing folders");

                UpdateFailure.Record(path, version, "삭제 대상");
                UpdateFailure.Clear(path);
                Require(!File.Exists(path) && UpdateFailure.Load(path) == null, "Clear must remove the record");
                UpdateFailure.Clear(path);
                UpdateFailure.Clear(Path.Combine(Path.Combine(root, "missing-folder"), "update-failed.txt"));

                UpdateFailure.Record(path, version, "서버 오류" + Environment.NewLine + "세부 내용");
                UpdateFailure failure = UpdateFailure.Load(path);
                Require(failure != null, "The dialog failure must load from a recorded file");
                using (var dialog = new UpdateDialog(failure))
                {
                    var status = (Label)dialog.Controls["Status"];
                    var notes = (TextBox)dialog.Controls["ReleaseNotes"];
                    Require(dialog.Text == "퇴근시계 업데이트 실패", "Failure dialog title");
                    Require(status.Text.Contains("v1.3.0") && status.Text.Contains("설치하지 못했습니다"),
                        "Failure dialog must name the version and the failure");
                    Require(notes.ReadOnly && notes.Multiline && notes.Text.Contains("서버 오류") &&
                        notes.Text.Contains("세부 내용") && notes.Text.Contains("업데이트 확인"),
                        "Failure dialog must show the reason and how to retry");
                    var buttons = dialog.Controls.OfType<Button>().ToList();
                    Require(buttons.All(button => button.Text != "지금 설치") && buttons.Any(button => button.Text == "닫기"),
                        "Failure dialog must offer only close");
                }
                using (var dialog = new UpdateDialog(true))
                    Require(dialog.Controls["Status"] is Label, "Every update dialog mode must name its status label");
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        private static TimeSpan T(int hour, int minute)
        {
            return new TimeSpan(hour, minute, 0);
        }

        private static bool ThrowsArgument(Action action)
        {
            try { action(); }
            catch (ArgumentException) { return true; }
            return false;
        }

        private static void CheckAt(string name, Schedule schedule, DateTime now, CountdownTarget target,
            TimeSpan remaining, double progress)
        {
            Countdown countdown = schedule.At(now);
            Require(countdown.Target == target && countdown.Remaining == remaining &&
                Math.Abs(countdown.Progress - progress) < 0.0001 &&
                countdown.Finished == (target == CountdownTarget.Leave && remaining == TimeSpan.Zero),
                name + " (target " + countdown.Target + ", remaining " + countdown.Remaining + ", progress " + countdown.Progress + ")");
        }

        private static void RequireLunch(LunchBreak lunch, bool enabled, TimeSpan start, TimeSpan end, string scenario)
        {
            Require(lunch != null && lunch.Enabled == enabled && lunch.Start == start && lunch.End == end, scenario);
        }

        private static void VerifyLunchBreak()
        {
            LunchBreak standard = LunchBreak.Default;
            RequireLunch(standard, false, T(12, 0), T(13, 0), "Default lunch is disabled 12:00-13:00");
            Require(standard.Format() == "Off 12:00-13:00", "Default lunch format");
            Require(new LunchBreak(true, T(11, 30), T(12, 45)).Format() == "On 11:30-12:45", "Enabled lunch format");
            Require(new LunchBreak(false, T(9, 5), T(10, 0)).Format() == "Off 09:05-10:00", "Disabled lunch format pads HH:mm");
            RequireLunch(LunchBreak.Parse("On 11:30-12:45"), true, T(11, 30), T(12, 45), "Parse an enabled lunch");
            RequireLunch(LunchBreak.Parse("Off 09:05-10:00"), false, T(9, 5), T(10, 0), "Parse a disabled lunch");
            RequireLunch(LunchBreak.Parse(new LunchBreak(true, T(2, 0), T(2, 30)).Format()), true, T(2, 0), T(2, 30),
                "Format then Parse round trip");
            Require(standard.Length == TimeSpan.FromHours(1) && new LunchBreak(true, T(11, 30), T(12, 45)).Length == T(1, 15) &&
                new LunchBreak(true, T(23, 30), T(0, 30)).Length == TimeSpan.FromHours(1), "Lunch length wraps across midnight");
            foreach (string bad in new[] { null, "", "garbage", "On 12:00", "12:00-13:00", "On 25:00-26:00", "On 12:00-13:99", "On 12:00-13:00 extra", "Maybe 12:00-13:00" })
                RequireLunch(LunchBreak.Parse(bad), false, T(12, 0), T(13, 0), "Invalid lunch text must parse as Default: " + (bad ?? "null"));

            // Schedule constructor validation
            var lunchNoon = new LunchBreak(true, T(12, 0), T(13, 0));
            var day = new Schedule(T(9, 0), T(18, 0), lunchNoon);
            Require(day.Lunch.Enabled && day.Lunch.Start == T(12, 0) && day.Lunch.End == T(13, 0), "Schedule keeps its lunch");
            RequireLunch(new Schedule(T(9, 0), T(18, 0)).Lunch, false, T(12, 0), T(13, 0), "Schedule without lunch uses Default");
            RequireLunch(new Schedule(T(9, 0), T(18, 0), null).Lunch, false, T(12, 0), T(13, 0), "Null lunch uses Default");
            RequireLunch(Schedule.Default.Lunch, false, T(12, 0), T(13, 0), "Schedule.Default has the default lunch");
            Require(ThrowsArgument(delegate { new Schedule(T(9, 0), T(18, 0), new LunchBreak(true, T(12, 0), T(12, 0))); }),
                "Zero-length lunch must throw");
            Require(ThrowsArgument(delegate { new Schedule(T(9, 0), T(18, 0), new LunchBreak(true, T(8, 0), T(9, 30))); }),
                "Lunch starting before the shift must throw");
            Require(ThrowsArgument(delegate { new Schedule(T(9, 0), T(18, 0), new LunchBreak(true, T(17, 30), T(18, 30))); }),
                "Lunch ending after the shift must throw");
            Require(ThrowsArgument(delegate { new Schedule(T(9, 0), T(18, 0), new LunchBreak(true, T(19, 0), T(20, 0))); }),
                "Lunch after the shift must throw");
            Require(ThrowsArgument(delegate { new Schedule(T(9, 0), T(18, 0), new LunchBreak(true, T(13, 0), T(12, 0))); }),
                "Lunch wrapping past the shift must throw");
            Require(ThrowsArgument(delegate { new Schedule(T(9, 0), T(18, 0), new LunchBreak(true, T(9, 0), T(18, 0))); }),
                "Lunch as long as the whole shift must throw");
            Require(ThrowsArgument(delegate { new Schedule(T(9, 0), T(18, 0), new LunchBreak(true, T(10, 0), T(19, 0))); }),
                "Lunch crossing the shift end must throw");
            var night = new Schedule(T(22, 0), T(6, 0), new LunchBreak(true, T(2, 0), T(2, 30)));
            Require(night.Lunch.Enabled, "Lunch after midnight inside an overnight shift is valid");
            Require(new Schedule(T(22, 0), T(6, 0), new LunchBreak(true, T(23, 30), T(0, 30))).Lunch.Enabled,
                "Lunch across midnight inside an overnight shift is valid");
            Require(ThrowsArgument(delegate { new Schedule(T(22, 0), T(6, 0), new LunchBreak(true, T(7, 0), T(8, 0))); }),
                "Lunch outside an overnight shift must throw");
            Require(ThrowsArgument(delegate { new Schedule(T(22, 0), T(6, 0), new LunchBreak(true, T(21, 30), T(22, 30))); }),
                "Lunch starting before an overnight shift must throw");
            Require(!ThrowsArgument(delegate { new Schedule(T(9, 0), T(18, 0), new LunchBreak(false, T(7, 0), T(8, 0))); }) &&
                !ThrowsArgument(delegate { new Schedule(T(9, 0), T(18, 0), new LunchBreak(false, T(12, 0), T(12, 0))); }) &&
                !ThrowsArgument(delegate { new Schedule(T(22, 0), T(6, 0), new LunchBreak(false, T(7, 0), T(8, 0))); }),
                "A disabled lunch never throws");

            // CountdownTarget and stages
            Require((int)CountdownTarget.Leave == 0 && Enum.GetValues(typeof(CountdownTarget)).Length == 3, "CountdownTarget order");
            Require(new Countdown(TimeSpan.FromHours(1), 0.5).Target == CountdownTarget.Leave, "Default target is Leave");
            Require(new Countdown(TimeSpan.FromHours(1), 0.5, CountdownTarget.Lunch).Target == CountdownTarget.Lunch, "Lunch target");
            Require(new Countdown(TimeSpan.Zero, 1).Finished && new Countdown(TimeSpan.Zero, 1, CountdownTarget.Leave).Finished,
                "Leave with nothing left is finished");
            Require(!new Countdown(TimeSpan.Zero, 0.5, CountdownTarget.Lunch).Finished &&
                !new Countdown(TimeSpan.Zero, 0.5, CountdownTarget.LunchEnd).Finished,
                "Only the Leave target can be finished");
            Require(!new Countdown(TimeSpan.FromMinutes(1), 0.5, CountdownTarget.Leave).Finished, "Time left is not finished");
            Require(new Countdown(TimeSpan.FromMinutes(31), 0.5, CountdownTarget.Lunch).Stage == CountdownStage.Running &&
                new Countdown(TimeSpan.FromMinutes(30), 0.5, CountdownTarget.Lunch).Stage == CountdownStage.Soon &&
                new Countdown(TimeSpan.FromMinutes(11), 0.5, CountdownTarget.Lunch).Stage == CountdownStage.Soon &&
                new Countdown(TimeSpan.FromMinutes(10), 0.5, CountdownTarget.Lunch).Stage == CountdownStage.Soon &&
                new Countdown(TimeSpan.FromMinutes(5), 0.5, CountdownTarget.Lunch).Stage == CountdownStage.Soon &&
                new Countdown(TimeSpan.FromSeconds(1), 0.5, CountdownTarget.Lunch).Stage == CountdownStage.Soon,
                "A Lunch target never becomes Urgent");
            foreach (CountdownTarget kept in new[] { CountdownTarget.Leave, CountdownTarget.LunchEnd })
                Require(new Countdown(TimeSpan.FromMinutes(31), 0.5, kept).Stage == CountdownStage.Running &&
                    new Countdown(TimeSpan.FromMinutes(30), 0.5, kept).Stage == CountdownStage.Soon &&
                    new Countdown(TimeSpan.FromMinutes(11), 0.5, kept).Stage == CountdownStage.Soon &&
                    new Countdown(TimeSpan.FromMinutes(10), 0.5, kept).Stage == CountdownStage.Urgent &&
                    new Countdown(TimeSpan.FromMinutes(5), 0.5, kept).Stage == CountdownStage.Urgent,
                    kept + " target keeps the existing stage thresholds");
            Require(new Countdown(TimeSpan.Zero, 1, CountdownTarget.Leave).Stage == CountdownStage.Finished, "Finished stage");

            // Schedule.At with a daytime lunch
            foreach (DateTime date in new[] { new DateTime(2026, 11, 20), new DateTime(2026, 11, 21), new DateTime(2027, 2, 28) })
            {
                string day0 = date.ToString("yyyy-MM-dd") + " ";
                CheckAt(day0 + "08:00", day, date.AddHours(8), CountdownTarget.Lunch, TimeSpan.FromHours(4), 0);
                CheckAt(day0 + "09:00", day, date.AddHours(9), CountdownTarget.Lunch, TimeSpan.FromHours(3), 0);
                CheckAt(day0 + "10:30", day, date.AddHours(10.5), CountdownTarget.Lunch, TimeSpan.FromMinutes(90), 1.5 / 8);
                CheckAt(day0 + "12:00", day, date.AddHours(12), CountdownTarget.LunchEnd, TimeSpan.FromHours(1), 3.0 / 8);
                CheckAt(day0 + "12:30", day, date.AddHours(12.5), CountdownTarget.LunchEnd, TimeSpan.FromMinutes(30), 3.0 / 8);
                CheckAt(day0 + "13:00", day, date.AddHours(13), CountdownTarget.Leave, TimeSpan.FromHours(5), 3.0 / 8);
                CheckAt(day0 + "15:00", day, date.AddHours(15), CountdownTarget.Leave, TimeSpan.FromHours(3), 5.0 / 8);
                CheckAt(day0 + "18:00", day, date.AddHours(18), CountdownTarget.Leave, TimeSpan.Zero, 1);
                CheckAt(day0 + "20:00", day, date.AddHours(20), CountdownTarget.Leave, TimeSpan.Zero, 1);
            }

            // Overnight lunch
            var d = new DateTime(2026, 11, 20);
            CheckAt("Overnight 23:00", night, d.AddHours(23), CountdownTarget.Lunch, TimeSpan.FromHours(3), 1.0 / 7.5);
            CheckAt("Overnight 02:15 next day", night, d.AddDays(1).AddMinutes(135), CountdownTarget.LunchEnd,
                TimeSpan.FromMinutes(15), 4.0 / 7.5);
            CheckAt("Overnight 03:00 next day", night, d.AddDays(1).AddHours(3), CountdownTarget.Leave, TimeSpan.FromHours(3), 4.5 / 7.5);
            CheckAt("Overnight 06:00 next day", night, d.AddDays(1).AddHours(6), CountdownTarget.Leave, TimeSpan.Zero, 1);

            // Disabled or absent lunch changes nothing
            var disabled = new Schedule(T(9, 0), T(18, 0), new LunchBreak(false, T(12, 0), T(13, 0)));
            foreach (Schedule plain in new[] { disabled, new Schedule(T(9, 0), T(18, 0)) })
            {
                CheckAt("No lunch 08:00", plain, d.AddHours(8), CountdownTarget.Leave, TimeSpan.FromHours(10), 0);
                CheckAt("No lunch 12:30", plain, d.AddHours(12.5), CountdownTarget.Leave, TimeSpan.FromHours(5.5), 3.5 / 9);
                CheckAt("No lunch 14:00", plain, d.AddHours(14), CountdownTarget.Leave, TimeSpan.FromHours(4), 5.0 / 9);
                CheckAt("No lunch 18:00", plain, d.AddHours(18), CountdownTarget.Leave, TimeSpan.Zero, 1);
            }

            // Pay follows progress, which pauses during lunch
            const long salary = 50400000;
            var workday = new DateTime(2026, 11, 23);
            Require(PayEstimate.At(salary, workday, day.At(workday.AddHours(14)).Progress) == 100000, "Pay at 14:00 with lunch");
            Require(PayEstimate.At(salary, workday, day.At(workday.AddHours(12.5)).Progress) == 75000, "Pay pauses during lunch");
            Require(PayEstimate.At(salary, workday, day.At(workday.AddHours(18)).Progress) == 200000, "Full pay at the end with lunch");
        }

        private static void VerifyLunchSettings(string folder)
        {
            string path = Path.Combine(folder, "lunch-" + Guid.NewGuid().ToString("N") + ".txt");
            var store = new SettingsFile(path);
            try
            {
                var lunch = new LunchBreak(true, T(11, 30), T(12, 30));
                var original = new ClockSettings(new Schedule(T(9, 0), T(18, 0), lunch), DisplayMode.Minutes, "lunch-monitor");
                store.Save(original);
                string[] lines = File.ReadAllLines(path);
                Require(lines.Length == 13 && lines[0] == "09:00" && lines[1] == "18:00" && lines[2] == "Minutes" &&
                    lines[3] == "lunch-monitor" && lines[12] == "On 11:30-12:30", "Lunch must be saved on line 12");
                ClockSettings loaded = store.Load();
                RequireLunch(loaded.Schedule.Lunch, true, T(11, 30), T(12, 30), "Saved lunch must round trip");
                RequireSameExceptColor(loaded, original, "Lunch round trip");

                File.WriteAllLines(path, lines.Take(12));
                ClockSettings oldFile = store.Load();
                RequireLunch(oldFile.Schedule.Lunch, false, T(12, 0), T(13, 0), "A twelve-line file loads the default lunch");
                RequireSameExceptColor(oldFile, original, "Twelve-line settings");

                File.WriteAllLines(path, new[] { "09:20", "18:30" });
                RequireLunch(store.Load().Schedule.Lunch, false, T(12, 0), T(13, 0), "A legacy file loads the default lunch");

                string[] broken = (string[])lines.Clone();
                broken[12] = "garbage";
                File.WriteAllLines(path, broken);
                ClockSettings invalid = store.Load();
                RequireLunch(invalid.Schedule.Lunch, false, T(12, 0), T(13, 0), "Invalid lunch text loads the default lunch");
                RequireSameExceptColor(invalid, original, "Invalid lunch text");

                broken[12] = "On 07:00-08:00";
                File.WriteAllLines(path, broken);
                ClockSettings outside = store.Load();
                RequireLunch(outside.Schedule.Lunch, false, T(7, 0), T(8, 0),
                    "A stored lunch outside the shift loads disabled with its times");
                RequireSameExceptColor(outside, original, "Lunch outside the shift");

                broken[12] = "On 12:00-12:00";
                File.WriteAllLines(path, broken);
                ClockSettings empty = store.Load();
                Require(!empty.Schedule.Lunch.Enabled && empty.Schedule.Start == T(9, 0), "A zero-length stored lunch loads disabled");

                var overnight = new ClockSettings(new Schedule(T(22, 0), T(6, 0), new LunchBreak(true, T(2, 0), T(2, 30))), DisplayMode.Clock);
                store.Save(overnight);
                RequireLunch(store.Load().Schedule.Lunch, true, T(2, 0), T(2, 30), "An overnight lunch must round trip");

                store.Save(ClockSettings.Default);
                string[] defaults = File.ReadAllLines(path);
                Require(defaults.Length == 13 && defaults[12] == "Off 12:00-13:00", "Default settings write the default lunch");
            }
            finally { File.Delete(path); }
        }

        private static ClockSettings LunchSettings(bool enabled, TimeSpan start, TimeSpan end)
        {
            return new ClockSettings(new Schedule(T(9, 0), T(18, 0), new LunchBreak(enabled, start, end)), DisplayMode.Clock);
        }

        private static void VerifyLunchDialog()
        {
            int storeCalls = 0;
            ClockSettings stored = null;
            DialogResult firstResult;
            bool pickersFollowCheck = false, pickersFollowUncheck = false, stayedOpen = false;
            int callsAfterInvalid = -1;
            string feedbackAfterInvalid = "";
            using (var dialog = new ClockSettingsDialog(ClockSettings.Default, null))
            {
                var enabled = (CheckBox)dialog.Controls["LunchEnabled"];
                var lunchStart = (DateTimePicker)dialog.Controls["LunchStart"];
                var lunchEnd = (DateTimePicker)dialog.Controls["LunchEnd"];
                var feedback = (Label)dialog.Controls["Feedback"];
                Require(enabled.Text == "점심시간" && !enabled.Checked && !lunchStart.Enabled && !lunchEnd.Enabled &&
                    lunchStart.Value.TimeOfDay == T(12, 0) && lunchEnd.Value.TimeOfDay == T(13, 0),
                    "Default dialog shows a disabled lunch 12:00-13:00");
                dialog.StoreSettings = delegate(ClockSettings settings) { storeCalls++; stored = settings; };
                dialog.Shown += delegate
                {
                    enabled.Checked = true;
                    pickersFollowCheck = lunchStart.Enabled && lunchEnd.Enabled;
                    lunchStart.Value = DateTime.Today.Add(T(7, 0));
                    lunchEnd.Value = DateTime.Today.Add(T(8, 0));
                    dialog.AcceptButton.PerformClick();
                    stayedOpen = dialog.DialogResult == DialogResult.None;
                    callsAfterInvalid = storeCalls;
                    feedbackAfterInvalid = feedback.Text;
                    enabled.Checked = false;
                    pickersFollowUncheck = !lunchStart.Enabled && !lunchEnd.Enabled;
                    enabled.Checked = true;
                    lunchStart.Value = DateTime.Today.Add(T(11, 30));
                    lunchEnd.Value = DateTime.Today.Add(T(12, 30));
                    dialog.AcceptButton.PerformClick();
                };
                firstResult = dialog.ShowDialog();
            }
            Require(pickersFollowCheck && pickersFollowUncheck, "Lunch pickers are enabled only while the checkbox is checked");
            Require(stayedOpen && callsAfterInvalid == 0, "An out-of-shift lunch must keep the dialog open and not store");
            Require(feedbackAfterInvalid.Contains("점심"), "An out-of-shift lunch must explain itself in the feedback label");
            Require(firstResult == DialogResult.OK && storeCalls == 1 && stored != null, "A valid lunch must be stored once");
            RequireLunch(stored.Schedule.Lunch, true, T(11, 30), T(12, 30), "Saved lunch values");

            ClockSettings second = null;
            bool reopened = false, reopenedPickersOff = false;
            DialogResult secondResult;
            using (var dialog = new ClockSettingsDialog(stored, null))
            {
                var enabled = (CheckBox)dialog.Controls["LunchEnabled"];
                var lunchStart = (DateTimePicker)dialog.Controls["LunchStart"];
                var lunchEnd = (DateTimePicker)dialog.Controls["LunchEnd"];
                reopened = enabled.Checked && lunchStart.Enabled && lunchEnd.Enabled &&
                    lunchStart.Value.TimeOfDay == T(11, 30) && lunchEnd.Value.TimeOfDay == T(12, 30);
                dialog.StoreSettings = delegate(ClockSettings settings) { second = settings; };
                dialog.Shown += delegate
                {
                    enabled.Checked = false;
                    reopenedPickersOff = !lunchStart.Enabled && !lunchEnd.Enabled;
                    dialog.AcceptButton.PerformClick();
                };
                secondResult = dialog.ShowDialog();
            }
            Require(reopened, "Reopening must show the saved lunch");
            Require(secondResult == DialogResult.OK && second != null && reopenedPickersOff, "Unchecked lunch must save");
            RequireLunch(second.Schedule.Lunch, false, T(11, 30), T(12, 30), "An unchecked lunch keeps its picker times");

            ClockSettings third = null;
            DialogResult thirdResult;
            using (var dialog = new ClockSettingsDialog(ClockSettings.Default, null))
            {
                var lunchStart = (DateTimePicker)dialog.Controls["LunchStart"];
                var lunchEnd = (DateTimePicker)dialog.Controls["LunchEnd"];
                dialog.StoreSettings = delegate(ClockSettings settings) { third = settings; };
                dialog.Shown += delegate
                {
                    lunchStart.Value = DateTime.Today.Add(T(7, 0));
                    lunchEnd.Value = DateTime.Today.Add(T(8, 0));
                    dialog.AcceptButton.PerformClick();
                };
                thirdResult = dialog.ShowDialog();
            }
            Require(thirdResult == DialogResult.OK && third != null, "A disabled lunch outside the shift must not block saving");
            RequireLunch(third.Schedule.Lunch, false, T(7, 0), T(8, 0), "A disabled lunch keeps its times");

            ClockSettings fourth = null;
            DialogResult fourthResult;
            bool disabledShown = false;
            using (var dialog = new ClockSettingsDialog(LunchSettings(false, T(10, 0), T(10, 45)), null))
            {
                var enabled = (CheckBox)dialog.Controls["LunchEnabled"];
                var lunchStart = (DateTimePicker)dialog.Controls["LunchStart"];
                var lunchEnd = (DateTimePicker)dialog.Controls["LunchEnd"];
                disabledShown = !enabled.Checked && !lunchStart.Enabled && !lunchEnd.Enabled &&
                    lunchStart.Value.TimeOfDay == T(10, 0) && lunchEnd.Value.TimeOfDay == T(10, 45);
                dialog.StoreSettings = delegate(ClockSettings settings) { fourth = settings; };
                dialog.Shown += delegate { dialog.AcceptButton.PerformClick(); };
                fourthResult = dialog.ShowDialog();
            }
            Require(disabledShown, "A disabled saved lunch must show its times with unchecked, disabled controls");
            Require(fourthResult == DialogResult.OK && fourth != null, "Save with an untouched disabled lunch");
            RequireLunch(fourth.Schedule.Lunch, false, T(10, 0), T(10, 45), "An untouched disabled lunch is kept");
        }

        private static void VerifyLunchReminder()
        {
            TimeSpan noon = T(12, 0);
            var day = new DateTime(2026, 11, 20);
            DateTime at = day.Add(noon);
            var reminder = new FinishReminder(at.AddMinutes(-5));
            Require(!reminder.ShouldNotify(noon, at.AddTicks(-1)), "Time reminder fired before the time");
            Require(reminder.ShouldNotify(noon, at), "Time reminder did not fire at the time");
            Require(!reminder.ShouldNotify(noon, at), "Time reminder repeated at the same instant");
            Require(!reminder.ShouldNotify(noon, at.AddSeconds(1)), "Time reminder repeated after the time");
            Require(!reminder.ShouldNotify(noon, at.AddMinutes(-2)), "Time reminder fired on clock rollback");
            Require(!reminder.ShouldNotify(noon, at.AddSeconds(2)), "Time reminder repeated after crossing the same time again");
            Require(!reminder.ShouldNotify(noon, at.AddDays(1).AddMinutes(-1)), "Time reminder fired before the next day's time");
            Require(reminder.ShouldNotify(noon, at.AddDays(1)), "Time reminder did not fire again on the next day");

            var startedAtTime = new FinishReminder(at);
            Require(!startedAtTime.ShouldNotify(noon, at.AddSeconds(10)), "A launch at the time fired a time reminder");
            var startedLate = new FinishReminder(at.AddSeconds(10));
            Require(!startedLate.ShouldNotify(noon, at.AddSeconds(20)), "A late launch fired a time reminder");
            var resumedInTime = new FinishReminder(at.AddMinutes(-5));
            Require(resumedInTime.ShouldNotify(noon, at.AddMinutes(1)), "Time reminder missed the one-minute grace boundary");
            var resumedTooLate = new FinishReminder(at.AddMinutes(-5));
            Require(!resumedTooLate.ShouldNotify(noon, at.AddMinutes(1).AddTicks(1)), "Time reminder fired after the grace period");
            var reset = new FinishReminder(at.AddMinutes(-5));
            reset.Reset(at.AddSeconds(10));
            Require(!reset.ShouldNotify(noon, at.AddSeconds(20)), "Reset caused a time reminder for a past time");

            DateTime midnight = day.AddDays(1);
            var overMidnight = new FinishReminder(midnight.AddTicks(-1));
            Require(overMidnight.ShouldNotify(TimeSpan.Zero, midnight.AddSeconds(10)), "Midnight time reminder missed");
            Require(!overMidnight.ShouldNotify(TimeSpan.Zero, midnight.AddSeconds(20)), "Midnight time reminder repeated");
            var lateTime = new FinishReminder(midnight.AddMinutes(-1).AddSeconds(-1));
            Require(lateTime.ShouldNotify(T(23, 59), midnight), "Time reminder lost a time exactly one minute before midnight");

            // The schedule overload keeps working next to the time overload
            var viaSchedule = new FinishReminder(at.AddMinutes(-5));
            Require(viaSchedule.ShouldNotify(new Schedule(T(9, 0), T(12, 0)), at), "Schedule overload still fires at the shift end");
        }

        private static void VerifyLunchNotice()
        {
            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            using (var notice = new FinishNotice(1, ClockTheme.Light, area))
                Require(notice.Text == "퇴근 시간이에요", "Default notice title");
            using (var notice = new FinishNotice(1, ClockTheme.Light, area, "점심시간이에요", "점심시간이 되었습니다."))
                Require(notice.Text == "점심시간이에요", "Notice must use the given title");
            using (var notice = new FinishNotice(1.5f, ClockTheme.Dark, area, "점심 끝", "다시 시작해요."))
                Require(notice.Text == "점심 끝", "Notice title with a scaled dpi");
        }

        private static void RequireMilestone(MilestoneReminder reminder, Schedule schedule, DateTime now,
            Milestone expected, string scenario)
        {
            Milestone actual = reminder.Check(schedule, now);
            Require(actual == expected, scenario + " (expected " + expected + ", got " + actual + ")");
        }

        private static void VerifyMilestones()
        {
            var d = new DateTime(2026, 11, 20);
            var day = new Schedule(T(9, 0), T(18, 0), new LunchBreak(true, T(12, 0), T(13, 0)));

            var all = new MilestoneReminder(d.Add(T(11, 55)));
            RequireMilestone(all, day, d.Add(new TimeSpan(11, 59, 59)), Milestone.None, "Before the lunch start");
            RequireMilestone(all, day, d.Add(T(12, 0)), Milestone.LunchStarts, "At the lunch start");
            RequireMilestone(all, day, d.Add(new TimeSpan(12, 0, 1)), Milestone.None, "One second after the lunch start");
            RequireMilestone(all, day, d.Add(new TimeSpan(12, 54, 59)), Milestone.None, "Before five minutes to lunch end");
            RequireMilestone(all, day, d.Add(T(12, 55)), Milestone.LunchEnding, "Five minutes before the lunch end");
            RequireMilestone(all, day, d.Add(new TimeSpan(12, 55, 1)), Milestone.None, "LunchEnding must not repeat");
            RequireMilestone(all, day, d.Add(new TimeSpan(17, 59, 59)), Milestone.None, "Before leaving time");
            RequireMilestone(all, day, d.Add(T(18, 0)), Milestone.Leave, "At leaving time");
            RequireMilestone(all, day, d.Add(new TimeSpan(18, 0, 1)), Milestone.None, "Leave must not repeat");
            RequireMilestone(all, day, d.AddDays(1).Add(new TimeSpan(11, 59, 59)), Milestone.None, "Next day before the lunch start");
            RequireMilestone(all, day, d.AddDays(1).Add(T(12, 0)), Milestone.LunchStarts, "LunchStarts fires again the next day");
            RequireMilestone(all, day, d.AddDays(1).Add(T(12, 55)), Milestone.LunchEnding, "LunchEnding fires again the next day");
            RequireMilestone(all, day, d.AddDays(1).Add(T(18, 0)), Milestone.Leave, "Leave fires again the next day");

            var disabled = new Schedule(T(9, 0), T(18, 0), new LunchBreak(false, T(12, 0), T(13, 0)));
            var off = new MilestoneReminder(d.Add(T(11, 55)));
            RequireMilestone(off, disabled, d.Add(T(12, 0)), Milestone.None, "A disabled lunch never starts");
            RequireMilestone(off, disabled, d.Add(T(12, 55)), Milestone.None, "A disabled lunch never ends");
            RequireMilestone(off, disabled, d.Add(new TimeSpan(17, 59, 59)), Milestone.None, "Disabled lunch before leaving time");
            RequireMilestone(off, disabled, d.Add(T(18, 0)), Milestone.Leave, "Leave still fires with a disabled lunch");
            var plain = new MilestoneReminder(d.Add(T(17, 55)));
            RequireMilestone(plain, Schedule.Default, d.Add(T(18, 0)), Milestone.Leave, "Leave fires without any lunch");

            var tenMinutes = new Schedule(T(9, 0), T(18, 0), new LunchBreak(true, T(12, 0), T(12, 10)));
            var ten = new MilestoneReminder(d.Add(T(11, 55)));
            RequireMilestone(ten, tenMinutes, d.Add(T(12, 0)), Milestone.LunchStarts, "A ten-minute lunch still starts");
            RequireMilestone(ten, tenMinutes, d.Add(T(12, 5)), Milestone.None, "A ten-minute lunch has no LunchEnding");
            RequireMilestone(ten, tenMinutes, d.Add(T(12, 10)), Milestone.None, "A ten-minute lunch has no end notice");

            var elevenMinutes = new Schedule(T(9, 0), T(18, 0), new LunchBreak(true, T(12, 0), T(12, 11)));
            var eleven = new MilestoneReminder(d.Add(T(11, 55)));
            RequireMilestone(eleven, elevenMinutes, d.Add(T(12, 0)), Milestone.LunchStarts, "An eleven-minute lunch starts");
            RequireMilestone(eleven, elevenMinutes, d.Add(new TimeSpan(12, 5, 59)), Milestone.None, "Before the eleven-minute LunchEnding");
            RequireMilestone(eleven, elevenMinutes, d.Add(T(12, 6)), Milestone.LunchEnding, "An eleven-minute lunch ends at 12:06");

            var night = new Schedule(T(22, 0), T(6, 0), new LunchBreak(true, T(23, 30), T(0, 2)));
            var overnight = new MilestoneReminder(d.Add(T(23, 29)));
            RequireMilestone(overnight, night, d.Add(T(23, 30)), Milestone.LunchStarts, "Overnight lunch start");
            RequireMilestone(overnight, night, d.Add(new TimeSpan(23, 56, 59)), Milestone.None, "Before the wrapped LunchEnding");
            RequireMilestone(overnight, night, d.Add(T(23, 57)), Milestone.LunchEnding, "LunchEnding wraps to 23:57");
            RequireMilestone(overnight, night, d.AddDays(1).Add(new TimeSpan(5, 59, 59)), Milestone.None, "Before the overnight leaving time");
            RequireMilestone(overnight, night, d.AddDays(1).Add(T(6, 0)), Milestone.Leave, "Overnight leaving time");

            var grace = new MilestoneReminder(d.Add(T(11, 55)));
            RequireMilestone(grace, day, d.Add(T(12, 1)), Milestone.LunchStarts, "The one-minute grace boundary still fires");
            var tooLate = new MilestoneReminder(d.Add(T(11, 55)));
            RequireMilestone(tooLate, day, d.Add(T(12, 1)).AddTicks(1), Milestone.None, "A check past the grace period is silent");

            var slept = new MilestoneReminder(d.Add(T(11, 55)));
            RequireMilestone(slept, day, d.Add(T(14, 0)), Milestone.None, "A check that slept past the lunch times is silent");
            RequireMilestone(slept, day, d.Add(new TimeSpan(17, 59, 59)), Milestone.None, "After sleep, before leaving time");
            RequireMilestone(slept, day, d.Add(T(18, 0)), Milestone.Leave, "Leave still fires after a sleep");

            var resumed = new MilestoneReminder(d.Add(T(11, 59)));
            RequireMilestone(resumed, day, d.Add(T(12, 6)), Milestone.None,
                "A late check misses the LunchStarts of six minutes ago");

            var reset = new MilestoneReminder(d.Add(T(11, 55)));
            reset.Reset(d.Add(new TimeSpan(12, 0, 10)));
            RequireMilestone(reset, day, d.Add(new TimeSpan(12, 0, 20)), Milestone.None, "Reset after the time passed is silent");
            RequireMilestone(reset, day, d.Add(T(12, 55)), Milestone.LunchEnding, "Reset keeps later milestones");

            var late = new MilestoneReminder(d.Add(new TimeSpan(12, 0, 10)));
            RequireMilestone(late, day, d.Add(new TimeSpan(12, 0, 20)), Milestone.None, "Constructed after the lunch start");
            RequireMilestone(late, day, d.Add(T(12, 55)), Milestone.LunchEnding, "Constructed after the lunch start still gets LunchEnding");
            RequireMilestone(late, day, d.AddDays(1).Add(T(12, 0)), Milestone.LunchStarts, "Next day after a late construction");

            var clockBack = new MilestoneReminder(d.Add(T(11, 55)));
            RequireMilestone(clockBack, day, d.Add(T(12, 0)), Milestone.LunchStarts, "Before a clock rollback");
            RequireMilestone(clockBack, day, d.Add(T(11, 58)), Milestone.None, "Clock rollback is silent");
            RequireMilestone(clockBack, day, d.Add(new TimeSpan(12, 0, 2)), Milestone.None, "Crossing the same time again is silent");
        }

        private static bool HasSweat(Bitmap bitmap)
        {
            for (int x = 23; x <= 28; x++)
                for (int y = 7; y <= 13; y++)
                {
                    Color pixel = bitmap.GetPixel(x, y);
                    if (Math.Abs(pixel.R - 91) <= 12 && Math.Abs(pixel.G - 184) <= 12 && Math.Abs(pixel.B - 245) <= 12)
                        return true;
                }
            return false;
        }

        private static bool SameRegion(Bitmap a, Bitmap b, int left, int right, int top, int bottom)
        {
            for (int x = left; x <= right; x++)
                for (int y = top; y <= bottom; y++)
                    if (a.GetPixel(x, y).ToArgb() != b.GetPixel(x, y).ToArgb()) return false;
            return true;
        }

        private static void VerifyLunchStrip()
        {
            ClockPalette palette = ClockPalette.For(ClockTheme.Dark);
            ClockPalette light = ClockPalette.For(ClockTheme.Light);
            using (var strip = new ClockStrip(1))
            using (var leave = new Bitmap(strip.Width, strip.Height))
            using (var lunch = new Bitmap(strip.Width, strip.Height))
            using (var lunchEnd = new Bitmap(strip.Width, strip.Height))
            using (var bitmap = new Bitmap(strip.Width, strip.Height))
            {
                strip.SetAppearance(0, true, ClockTheme.Dark);
                foreach (DisplayMode mode in new[] { DisplayMode.Clock, DisplayMode.Hours, DisplayMode.Seconds })
                {
                    strip.Display(new Countdown(TimeSpan.FromHours(1), 0.5, CountdownTarget.Leave), mode);
                    strip.DrawToBitmap(leave, strip.ClientRectangle);
                    strip.Display(new Countdown(TimeSpan.FromHours(1), 0.5, CountdownTarget.Lunch), mode);
                    strip.DrawToBitmap(lunch, strip.ClientRectangle);
                    strip.Display(new Countdown(TimeSpan.FromHours(1), 0.5, CountdownTarget.LunchEnd), mode);
                    strip.DrawToBitmap(lunchEnd, strip.ClientRectangle);
                    Require(!SameRegion(leave, lunch, 31, 88, 6, 22), mode + " label for Lunch must differ from Leave");
                    Require(!SameRegion(leave, lunchEnd, 31, 88, 6, 22), mode + " label for LunchEnd must differ from Leave");
                    Require(!SameRegion(lunch, lunchEnd, 31, 88, 6, 22), mode + " label for LunchEnd must differ from Lunch");
                }

                strip.Display(new Countdown(TimeSpan.FromHours(1), 0.5, CountdownTarget.Leave), DisplayMode.Clock);
                strip.DrawToBitmap(leave, strip.ClientRectangle);
                strip.Display(new Countdown(TimeSpan.FromHours(1), 0.5, CountdownTarget.Lunch), DisplayMode.Clock);
                strip.DrawToBitmap(lunch, strip.ClientRectangle);
                strip.Display(new Countdown(TimeSpan.FromHours(1), 0.5, CountdownTarget.LunchEnd), DisplayMode.Clock);
                strip.DrawToBitmap(lunchEnd, strip.ClientRectangle);
                Require(SameRegion(leave, lunchEnd, 89, 90, 6, 22), "A long lunch label must not reach into the clock digits (LunchEnd)");
                Require(SameRegion(leave, lunch, 89, 90, 6, 22), "A lunch label must not reach into the clock digits (Lunch)");

                foreach (DisplayMode mode in Enum.GetValues(typeof(DisplayMode)))
                {
                    strip.Display(new Countdown(TimeSpan.FromMinutes(5), 0.5, CountdownTarget.Lunch), mode);
                    strip.DrawToBitmap(bitmap, strip.ClientRectangle);
                    Require(bitmap.GetPixel(20, 28).ToArgb() == palette.Soon.ToArgb(),
                        mode + " Lunch target with 5 minutes left must use the Soon color");
                    strip.Display(new Countdown(TimeSpan.FromMinutes(5), 0.5, CountdownTarget.LunchEnd), mode);
                    strip.DrawToBitmap(bitmap, strip.ClientRectangle);
                    Require(bitmap.GetPixel(20, 28).ToArgb() == palette.Urgent.ToArgb(),
                        mode + " LunchEnd target with 5 minutes left must stay Urgent");
                }
                strip.Display(new Countdown(TimeSpan.FromMinutes(40), 0.5, CountdownTarget.Lunch), DisplayMode.Clock);
                strip.DrawToBitmap(bitmap, strip.ClientRectangle);
                Require(bitmap.GetPixel(20, 28).ToArgb() == palette.Normal.ToArgb(), "Lunch target with 40 minutes left is Normal");

                strip.SetAppearance(0, true, ClockTheme.Light);
                strip.Display(new Countdown(TimeSpan.FromMinutes(5), 0.5, CountdownTarget.Lunch), DisplayMode.Clock);
                strip.DrawToBitmap(bitmap, strip.ClientRectangle);
                Require(bitmap.GetPixel(20, 28).ToArgb() == light.Soon.ToArgb(), "Light theme Lunch target uses Soon");

                foreach (ClockTheme theme in new[] { ClockTheme.Cat, ClockTheme.Bear, ClockTheme.Rabbit, ClockTheme.Penguin, ClockTheme.Chick })
                {
                    strip.SetAppearance(0, true, theme);
                    strip.Display(new Countdown(TimeSpan.FromMinutes(5), 0.5, CountdownTarget.Lunch), DisplayMode.Clock);
                    strip.DrawToBitmap(bitmap, strip.ClientRectangle);
                    Require(!HasSweat(bitmap), theme + " mascot must not sweat before lunch");
                    strip.Display(new Countdown(TimeSpan.FromMinutes(5), 0.5, CountdownTarget.LunchEnd), DisplayMode.Clock);
                    strip.DrawToBitmap(bitmap, strip.ClientRectangle);
                    Require(HasSweat(bitmap), theme + " mascot must still sweat in the last minutes of lunch");
                }
            }
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
