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
                Require(savedLines.Length == 12 && savedLines[0] == "09:20" && savedLines[1] == "18:30" &&
                    savedLines[2] == "Earnings" && savedLines[3] == target.DeviceName && savedLines[11] == "Theme",
                    "Existing settings fields must keep their positions and line 11 must default to Theme");
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
                Require(lines.Length == 12 && lines[11] == "#123456", "Custom progress color must be saved as #RRGGBB");
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
                Require(notes.ReadOnly && notes.Multiline && notes.Text.Contains("캐릭터 테마") &&
                    notes.Text.Contains("진행 막대 색"), "Show bundled release notes after installing");
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
