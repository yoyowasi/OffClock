using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Security;
using System.Text;

namespace OffClock
{
    // Saved by name, and the settings combo uses the index: only append.
    internal enum ClockTheme
    {
        Dark,
        Light,
        Sakura,
        Ocean,
        Forest,
        Sunset,
        Cat,
        Bear,
        Rabbit,
        Penguin,
        Chick
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
        public readonly bool AutoUpdate;
        // Empty: the bar follows the theme's state color.
        public readonly Color ProgressColor;

        public ClockSettings(Schedule schedule, DisplayMode display, string monitor = "",
            int transparency = 0, bool showProgress = true, bool notifyAtEnd = false, ClockTheme theme = ClockTheme.Dark,
            bool pinInside = true, long annualSalary = 0, bool autoUpdate = false, Color progressColor = default(Color))
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
            AutoUpdate = autoUpdate;
            ProgressColor = BarColor.Opaque(progressColor);
        }
    }

    internal static class BarColor
    {
        private const string ThemeDefault = "Theme";

        public static Color Opaque(Color color)
        {
            return color.IsEmpty ? Color.Empty : Color.FromArgb(255, color.R, color.G, color.B);
        }

        public static string Format(Color color)
        {
            return color.IsEmpty ? ThemeDefault : "#" + (color.ToArgb() & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture);
        }

        public static Color Parse(string text)
        {
            int rgb;
            if (text == null || text.Length != 7 || text[0] != '#' ||
                !int.TryParse(text.Substring(1), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out rgb))
                return Color.Empty;
            return Color.FromArgb(unchecked((int)0xFF000000) | rgb);
        }
    }

    internal static class PayEstimate
    {
        private const long MaxAnnualSalary = 999999999999L;

        public static bool TryParseSalary(string text, out long salary)
        {
            return long.TryParse(text.Trim(), NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out salary) &&
                salary >= 0 && salary <= MaxAnnualSalary;
        }

        public static string InKorean(long amount)
        {
            if (amount == 0) return "영 원";
            const string digits = "영일이삼사오육칠팔구";
            string[] groups = { "", "만", "억" };
            string[] positions = { "", "십", "백", "천" };
            var text = new StringBuilder();
            for (int group = groups.Length - 1; group >= 0; group--)
            {
                int divisor = group == 2 ? 100000000 : group == 1 ? 10000 : 1;
                int value = (int)(amount / divisor % 10000);
                if (value == 0) continue;
                if (text.Length > 0) text.Append(' ');
                for (int place = 3, unit = 1000; place >= 0; place--, unit /= 10)
                {
                    int digit = value / unit % 10;
                    if (digit == 0) continue;
                    if (digit != 1 || place == 0) text.Append(digits[digit]);
                    text.Append(positions[place]);
                }
                text.Append(groups[group]);
            }
            return text.Append(" 원").ToString();
        }

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

    internal sealed class LunchBreak
    {
        public static readonly LunchBreak Default = new LunchBreak(false, TimeSpan.FromHours(12), TimeSpan.FromHours(13));
        public readonly bool Enabled;
        public readonly TimeSpan Start;
        public readonly TimeSpan End;

        public LunchBreak(bool enabled, TimeSpan start, TimeSpan end)
        {
            Enabled = enabled;
            Start = start;
            End = end;
        }

        public TimeSpan Length { get { return Schedule.Wrap(End - Start); } }

        public string Format()
        {
            return (Enabled ? "On " : "Off ") + Schedule.Format(Start) + "-" + Schedule.Format(End);
        }

        public static LunchBreak Parse(string text)
        {
            string[] parts = (text ?? "").Split(' ');
            string[] times = parts.Length == 2 ? parts[1].Split('-') : new string[0];
            TimeSpan start, end;
            if ((parts[0] != "On" && parts[0] != "Off") || times.Length != 2 ||
                !TimeSpan.TryParseExact(times[0], @"hh\:mm", CultureInfo.InvariantCulture, out start) ||
                !TimeSpan.TryParseExact(times[1], @"hh\:mm", CultureInfo.InvariantCulture, out end))
                return Default;
            return new LunchBreak(parts[0] == "On", start, end);
        }
    }

    internal sealed class Schedule
    {
        private static readonly TimeSpan Day = TimeSpan.FromDays(1);
        public static readonly Schedule Default = new Schedule(TimeSpan.FromHours(9), TimeSpan.FromHours(18));
        public readonly TimeSpan Start;
        public readonly TimeSpan End;
        public readonly LunchBreak Lunch;

        public Schedule(TimeSpan start, TimeSpan end, LunchBreak lunch = null)
        {
            if (start == end) throw new ArgumentException("출근 시간과 퇴근 시간을 다르게 설정하세요.");
            Start = start;
            End = end;
            Lunch = lunch ?? LunchBreak.Default;
            TimeSpan shift = Wrap(end - start);
            if (Lunch.Enabled && (LunchLength == TimeSpan.Zero || LunchLength >= shift || LunchOffset + LunchLength > shift))
                throw new ArgumentException("점심시간은 근무 시간 안에서 시작과 끝을 다르게 설정하세요.");
        }

        // Lunch position measured from the shift start, so overnight shifts work across midnight.
        private TimeSpan LunchOffset { get { return Wrap(Lunch.Start - Start); } }
        private TimeSpan LunchLength { get { return Lunch.Length; } }

        internal static TimeSpan Wrap(TimeSpan time)
        {
            return TimeSpan.FromTicks((time.Ticks % Day.Ticks + Day.Ticks) % Day.Ticks);
        }

        public Countdown At(DateTime now)
        {
            DateTime end = DeadlineAt(now);
            DateTime start = WorkDateAt(now).Add(Start);

            TimeSpan remaining = now < end ? end - now : TimeSpan.Zero;
            if (!Lunch.Enabled) return new Countdown(remaining, Ratio(now - start, end - start));

            // Progress, and so the day's pay, pauses over lunch.
            DateTime lunchStart = start.Add(LunchOffset);
            DateTime lunchEnd = lunchStart.Add(LunchLength);
            TimeSpan worked = now - start;
            if (now > lunchStart) worked -= (now < lunchEnd ? now : lunchEnd) - lunchStart;
            double progress = Ratio(worked, end - start - LunchLength);
            if (now < lunchStart) return new Countdown(lunchStart - now, progress, CountdownTarget.Lunch);
            if (now < lunchEnd) return new Countdown(lunchEnd - now, progress, CountdownTarget.LunchEnd);
            return new Countdown(remaining, progress);
        }

        private static double Ratio(TimeSpan part, TimeSpan whole)
        {
            return Math.Max(0, Math.Min(1, part.TotalSeconds / whole.TotalSeconds));
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
            return Crossed(schedule.DeadlineAt(now), now);
        }

        public bool ShouldNotify(TimeSpan timeOfDay, DateTime now)
        {
            return Crossed(now.Date.Add(timeOfDay), now);
        }

        private bool Crossed(DateTime deadline, DateTime now)
        {
            if (deadline > now) deadline = deadline.AddDays(-1);
            bool crossedDeadline = previousCheck < deadline;
            previousCheck = now;
            return crossedDeadline && now - deadline <= MaxDelay && notifiedDeadlines.Add(deadline);
        }
    }

    internal enum CountdownStage
    {
        Running,
        Soon,
        Urgent,
        Finished
    }

    internal enum Milestone
    {
        None,
        Leave,
        LunchStarts,
        LunchEnding
    }

    // Which silent notice is due now: leaving, lunch starting, or lunch ending in 5 minutes.
    internal sealed class MilestoneReminder
    {
        private static readonly TimeSpan EndingWarning = TimeSpan.FromMinutes(5);
        private readonly FinishReminder leave;
        private readonly FinishReminder lunchStarts;
        private readonly FinishReminder lunchEnding;

        public MilestoneReminder(DateTime now)
        {
            leave = new FinishReminder(now);
            lunchStarts = new FinishReminder(now);
            lunchEnding = new FinishReminder(now);
        }

        public void Reset(DateTime now)
        {
            leave.Reset(now);
            lunchStarts.Reset(now);
            lunchEnding.Reset(now);
        }

        // Call on every tick so each reminder keeps its own last-check time.
        public Milestone Check(Schedule schedule, DateTime now)
        {
            LunchBreak lunch = schedule.Lunch;
            bool leaving = leave.ShouldNotify(schedule, now);
            bool starts = lunch.Enabled && lunchStarts.ShouldNotify(lunch.Start, now);
            bool ending = lunch.Enabled && lunch.Length > EndingWarning + EndingWarning &&
                lunchEnding.ShouldNotify(Schedule.Wrap(lunch.End - EndingWarning), now);
            if (leaving) return Milestone.Leave;
            if (starts) return Milestone.LunchStarts;
            return ending ? Milestone.LunchEnding : Milestone.None;
        }
    }

    internal enum CountdownTarget
    {
        Leave,
        Lunch,
        LunchEnd
    }

    internal struct Countdown
    {
        // Time left to Target: leaving work, or the start or end of lunch.
        public readonly TimeSpan Remaining;
        public readonly double Progress;
        public readonly CountdownTarget Target;
        public bool Finished { get { return Target == CountdownTarget.Leave && Remaining == TimeSpan.Zero; } }

        public CountdownStage Stage
        {
            get
            {
                if (Finished) return CountdownStage.Finished;
                // Lunch coming up is good news: no red alert for it.
                if (Remaining <= TimeSpan.FromMinutes(10))
                    return Target == CountdownTarget.Lunch ? CountdownStage.Soon : CountdownStage.Urgent;
                if (Remaining <= TimeSpan.FromMinutes(30)) return CountdownStage.Soon;
                return CountdownStage.Running;
            }
        }

        public Countdown(TimeSpan remaining, double progress, CountdownTarget target = CountdownTarget.Leave)
        {
            Remaining = remaining;
            Progress = progress;
            Target = target;
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
                    if (lines.Length < 10 || !PayEstimate.TryParseSalary(lines[9], out salary))
                        salary = 0;
                    // Older versions could opt in to checking only.
                    bool autoUpdate = lines.Length >= 11 && lines[10] == "AutoInstall";
                    Color progressColor = lines.Length >= 12 ? BarColor.Parse(lines[11]) : Color.Empty;
                    LunchBreak lunch = lines.Length >= 13 ? LunchBreak.Parse(lines[12]) : LunchBreak.Default;
                    Schedule schedule;
                    try { schedule = new Schedule(start, end, lunch); }
                    catch (ArgumentException) { schedule = new Schedule(start, end, new LunchBreak(false, lunch.Start, lunch.End)); }
                    return new ClockSettings(schedule, display, lines.Length >= 4 ? lines[3] : "",
                        transparency, showProgress, notifyAtEnd, theme, pinInside, salary, autoUpdate, progressColor);
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
                settings.AutoUpdate ? "AutoInstall" : "Off",
                BarColor.Format(settings.ProgressColor),
                settings.Schedule.Lunch.Format()
            });
            if (File.Exists(path))
                File.Replace(temporaryPath, path, null);
            else
                File.Move(temporaryPath, path);
        }
    }
}
