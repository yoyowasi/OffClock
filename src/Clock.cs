using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security;

namespace OffClock
{
    internal enum ClockTheme
    {
        Dark,
        Light
    }

    internal enum DisplayMode
    {
        Clock,
        Hours,
        Minutes,
        Seconds,
        Milliseconds,
        Binary,
        Octal,
        Hexadecimal,
        Earnings
    }

    internal sealed class ClockSettings
    {
        public const int MaxTransparency = 70;
        public static readonly ClockSettings Default = new ClockSettings(Schedule.Default, DisplayMode.Clock);
        public readonly Schedule Schedule;
        public readonly DisplayMode Display;
        public readonly string Monitor;
        public readonly int Transparency;
        public readonly bool ShowProgress;
        public readonly bool NotifyAtEnd;
        public readonly ClockTheme Theme;
        public readonly bool PinInside;
        public readonly long AnnualSalary;
        public readonly bool AutoCheckUpdates;

        public ClockSettings(Schedule schedule, DisplayMode display, string monitor = "",
            int transparency = 0, bool showProgress = true, bool notifyAtEnd = false, ClockTheme theme = ClockTheme.Dark,
            bool pinInside = true, long annualSalary = 0, bool autoCheckUpdates = false)
        {
            Schedule = schedule;
            Display = display == DisplayMode.Earnings && annualSalary <= 0 ? DisplayMode.Clock : display;
            Monitor = monitor;
            Transparency = Math.Max(0, Math.Min(MaxTransparency, transparency));
            ShowProgress = showProgress;
            NotifyAtEnd = notifyAtEnd;
            Theme = theme;
            PinInside = pinInside;
            AnnualSalary = Math.Max(0, annualSalary);
            AutoCheckUpdates = autoCheckUpdates;
        }
    }

    internal static class PayEstimate
    {
        public static long At(long annualSalary, DateTime workDate, double progress)
        {
            if (workDate.DayOfWeek == DayOfWeek.Saturday || workDate.DayOfWeek == DayOfWeek.Sunday) return 0;
            DateTime first = new DateTime(workDate.Year, workDate.Month, 1);
            int days = DateTime.DaysInMonth(workDate.Year, workDate.Month);
            int weekdays = days / 7 * 5;
            for (int i = 0; i < days % 7; i++)
            {
                DayOfWeek weekday = first.AddDays(i).DayOfWeek;
                if (weekday != DayOfWeek.Saturday && weekday != DayOfWeek.Sunday) weekdays++;
            }
            return (long)(annualSalary / 12m / weekdays * (decimal)progress);
        }
    }

    internal sealed class Schedule
    {
        public static readonly Schedule Default = new Schedule(TimeSpan.FromHours(9), TimeSpan.FromHours(18));
        public readonly TimeSpan Start;
        public readonly TimeSpan End;

        public Schedule(TimeSpan start, TimeSpan end)
        {
            if (start == end) throw new ArgumentException("출근 시간과 퇴근 시간을 다르게 설정하세요.");
            Start = start;
            End = end;
        }

        public Countdown At(DateTime now)
        {
            DateTime end = DeadlineAt(now);
            DateTime start = WorkDateAt(now).Add(Start);

            TimeSpan remaining = now < end ? end - now : TimeSpan.Zero;
            double progress = (now - start).TotalSeconds / (end - start).TotalSeconds;
            return new Countdown(remaining, Math.Max(0, Math.Min(1, progress)));
        }

        public DateTime WorkDateAt(DateTime now)
        {
            DateTime date = DeadlineAt(now).Date;
            return End < Start ? date.AddDays(-1) : date;
        }

        internal DateTime DeadlineAt(DateTime now)
        {
            DateTime end = now.Date.Add(End);
            if (End < Start && now.TimeOfDay >= Start) end = end.AddDays(1);
            return end;
        }

        public static string Format(TimeSpan time)
        {
            return time.ToString(@"hh\:mm", CultureInfo.InvariantCulture);
        }
    }

    internal sealed class FinishReminder
    {
        private static readonly TimeSpan MaxDelay = TimeSpan.FromMinutes(1);
        private readonly HashSet<DateTime> notifiedDeadlines = new HashSet<DateTime>();
        private DateTime previousCheck;

        public FinishReminder(DateTime now)
        {
            Reset(now);
        }

        public void Reset(DateTime now)
        {
            previousCheck = now;
        }

        public bool ShouldNotify(Schedule schedule, DateTime now)
        {
            DateTime deadline = schedule.DeadlineAt(now);
            if (deadline > now) deadline = deadline.AddDays(-1);
            bool crossedDeadline = previousCheck < deadline;
            previousCheck = now;
            return crossedDeadline && now - deadline <= MaxDelay && notifiedDeadlines.Add(deadline);
        }
    }

    internal struct Countdown
    {
        public readonly TimeSpan Remaining;
        public readonly double Progress;
        public bool Finished { get { return Remaining == TimeSpan.Zero; } }

        public Countdown(TimeSpan remaining, double progress)
        {
            Remaining = remaining;
            Progress = progress;
        }

        public string Seconds { get { return Remaining.ToString(@"hh\:mm\:ss", CultureInfo.InvariantCulture); } }
        public string Milliseconds { get { return Remaining.ToString(@"\.fff", CultureInfo.InvariantCulture); } }

        public string FormatTotal(DisplayMode mode, out string unit)
        {
            double total;
            switch (mode)
            {
                case DisplayMode.Hours:
                    unit = "시";
                    double hours = Math.Round(Remaining.TotalHours, 1, MidpointRounding.AwayFromZero);
                    if (hours == 0 && !Finished) return "<0.1";
                    return hours.ToString("0.0", CultureInfo.InvariantCulture);
                case DisplayMode.Minutes:
                    unit = "분";
                    total = Remaining.TotalMinutes;
                    break;
                case DisplayMode.Seconds:
                    unit = "초";
                    total = Remaining.TotalSeconds;
                    break;
                case DisplayMode.Milliseconds:
                    unit = "밀리초";
                    total = Remaining.TotalMilliseconds;
                    break;
                case DisplayMode.Binary:
                    return FormatSeconds(2, "0b", out unit);
                case DisplayMode.Octal:
                    return FormatSeconds(8, "0o", out unit);
                case DisplayMode.Hexadecimal:
                    return FormatSeconds(16, "0x", out unit);
                default:
                    throw new ArgumentOutOfRangeException("mode");
            }
            return Math.Ceiling(total).ToString("N0", CultureInfo.InvariantCulture);
        }

        private string FormatSeconds(int radix, string prefix, out string unit)
        {
            unit = "초";
            int seconds = (int)Math.Ceiling(Remaining.TotalSeconds);
            return prefix + Convert.ToString(seconds, radix).ToUpperInvariant();
        }
    }

    internal sealed class SettingsFile
    {
        private readonly string path;

        public SettingsFile(string path)
        {
            this.path = path;
        }

        public ClockSettings Load()
        {
            try
            {
                string[] lines = File.ReadAllLines(path);
                TimeSpan start, end;
                if (lines.Length >= 2 &&
                    TimeSpan.TryParseExact(lines[0], @"hh\:mm", CultureInfo.InvariantCulture, out start) &&
                    TimeSpan.TryParseExact(lines[1], @"hh\:mm", CultureInfo.InvariantCulture, out end) &&
                    start != end)
                {
                    DisplayMode display;
                    if (lines.Length < 3 || !Enum.TryParse(lines[2], out display) ||
                        !Enum.IsDefined(typeof(DisplayMode), display))
                        display = DisplayMode.Clock;

                    int transparency;
                    bool showProgress, notifyAtEnd;
                    if (lines.Length < 5 || !int.TryParse(lines[4], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out transparency))
                        transparency = ClockSettings.Default.Transparency;
                    if (lines.Length < 6 || !bool.TryParse(lines[5], out showProgress))
                        showProgress = ClockSettings.Default.ShowProgress;
                    if (lines.Length < 7 || !bool.TryParse(lines[6], out notifyAtEnd))
                        notifyAtEnd = ClockSettings.Default.NotifyAtEnd;

                    ClockTheme theme;
                    if (lines.Length < 8 || !Enum.TryParse(lines[7], out theme) ||
                        !Enum.IsDefined(typeof(ClockTheme), theme))
                        theme = ClockTheme.Dark;
                    bool pinInside;
                    if (lines.Length < 9 || !bool.TryParse(lines[8], out pinInside)) pinInside = true;
                    long salary;
                    if (lines.Length < 10 || !long.TryParse(lines[9], NumberStyles.None, CultureInfo.InvariantCulture, out salary))
                        salary = 0;
                    bool autoCheck;
                    if (lines.Length < 11 || !bool.TryParse(lines[10], out autoCheck)) autoCheck = false;
                    return new ClockSettings(new Schedule(start, end), display, lines.Length >= 4 ? lines[3] : "",
                        transparency, showProgress, notifyAtEnd, theme, pinInside, salary, autoCheck);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }

            return ClockSettings.Default;
        }

        public void Save(ClockSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporaryPath = path + ".tmp";
            File.WriteAllLines(temporaryPath, new[]
            {
                Schedule.Format(settings.Schedule.Start),
                Schedule.Format(settings.Schedule.End),
                settings.Display.ToString(),
                settings.Monitor,
                settings.Transparency.ToString(CultureInfo.InvariantCulture),
                settings.ShowProgress.ToString(),
                settings.NotifyAtEnd.ToString(),
                settings.Theme.ToString(),
                settings.PinInside.ToString(),
                settings.AnnualSalary.ToString(CultureInfo.InvariantCulture),
                settings.AutoCheckUpdates.ToString()
            });
            if (File.Exists(path))
                File.Replace(temporaryPath, path, null);
            else
                File.Move(temporaryPath, path);
        }
    }
}
