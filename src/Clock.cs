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
        Earnings,
        Payday
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
        public readonly bool ShowOvertime;
        // Work-day mask (see WorkWeek), whether public holidays are days off, payday 1..31 (0: off).
        public readonly int WorkDays;
        public readonly bool SkipHolidays;
        public readonly int Payday;
        // Kept as saved; only counts while its date is the current work date.
        public readonly DayOverride TodayOverride;

        public ClockSettings(Schedule schedule, DisplayMode display, string monitor = "",
            int transparency = 0, bool showProgress = true, bool notifyAtEnd = false, ClockTheme theme = ClockTheme.Dark,
            bool pinInside = true, long annualSalary = 0, bool autoUpdate = false, Color progressColor = default(Color),
            bool showOvertime = false, int workDays = WorkWeek.Default, bool skipHolidays = true, int payday = 0,
            DayOverride todayOverride = null)
        {
            ShowOvertime = showOvertime;
            WorkDays = (workDays & 0x7F) == 0 ? WorkWeek.Default : workDays & 0x7F;
            SkipHolidays = skipHolidays;
            Payday = Math.Max(0, Math.Min(31, payday));
            TodayOverride = todayOverride;
            Schedule = schedule;
            Display = (display == DisplayMode.Earnings && annualSalary <= 0) ||
                (display == DisplayMode.Payday && Payday == 0) ? DisplayMode.Clock : display;
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

        public ClockSettings WithDisplay(DisplayMode value)
        {
            return Copy(value, Theme, TodayOverride);
        }

        public ClockSettings WithTheme(ClockTheme value)
        {
            return Copy(Display, value, TodayOverride);
        }

        public ClockSettings WithOverride(DayOverride value)
        {
            return Copy(Display, Theme, value);
        }

        // The one place every field is copied: add new fields here so a copy never resets them.
        private ClockSettings Copy(DisplayMode display, ClockTheme theme, DayOverride todayOverride)
        {
            return new ClockSettings(Schedule, display, Monitor, Transparency, ShowProgress, NotifyAtEnd, theme,
                PinInside, AnnualSalary, AutoUpdate, ProgressColor, ShowOvertime, WorkDays, SkipHolidays, Payday,
                todayOverride);
        }

        public WorkCalendar Calendar(HolidayCalendar holidays)
        {
            return new WorkCalendar(WorkDays, SkipHolidays, holidays);
        }

        // Today-only changes are for day shifts that stay within one calendar day. Overnight shifts and
        // entries past midnight need per-shift windows, which proved error-prone; the menu refuses them.
        public bool SupportsOverride { get { return Schedule.Start < Schedule.End; } }

        public DateTime TargetDate(DateTime now)
        {
            return Schedule.WorkDateAt(now).Date;
        }

        // The override in force now: the one set for today, on a day-shift schedule.
        public DayOverride ActiveOverride(DateTime now)
        {
            if (TodayOverride == null || !SupportsOverride || TodayOverride.Date != TargetDate(now)) return null;
            // The settings file is editable by hand: ignore hours that are not a same-day window.
            if (!TodayOverride.Off && TodayOverride.End <= TodayOverride.Start) return null;
            return TodayOverride;
        }

        // The schedule in force now: the base one, or today's hours when overridden.
        public Schedule ScheduleAt(DateTime now)
        {
            DayOverride active = ActiveOverride(now);
            return active != null && !active.Off ? Schedule.WithWindow(active.Start, active.End) : Schedule;
        }

        // The schedule of one work date: today's hours only for the override's own date, else the base one.
        public Schedule ScheduleFor(DateTime workDate)
        {
            if (TodayOverride != null && SupportsOverride && TodayOverride.Date == workDate.Date &&
                !TodayOverride.Off && TodayOverride.End > TodayOverride.Start)
                return Schedule.WithWindow(TodayOverride.Start, TodayOverride.End);
            return Schedule;
        }

        // Whether a work date is a day off: its override first (rest: yes, hours: no), then the calendar.
        public bool IsDayOff(DateTime workDate, HolidayCalendar holidays)
        {
            if (TodayOverride != null && SupportsOverride && TodayOverride.Date == workDate.Date &&
                (TodayOverride.Off || TodayOverride.End > TodayOverride.Start))
                return TodayOverride.Off;
            return Calendar(holidays).DayOffName(workDate.Date) != null;
        }

        // What the widget shows now, in one place: a day off, or the countdown of today's schedule.
        public Countdown CountdownAt(DateTime now, HolidayCalendar holidays)
        {
            DayOverride active = ActiveOverride(now);
            if (active != null)
                return active.Off ? Countdown.DayOff("오늘 쉼") : ScheduleAt(now).At(now, active.Date);
            string name = Calendar(holidays).DayOffName(Schedule.WorkDateAt(now));
            return name != null ? Countdown.DayOff(name) : Schedule.At(now);
        }

        public long EarnedAt(DateTime now, Countdown countdown, HolidayCalendar holidays)
        {
            if (countdown.Target == CountdownTarget.DayOff) return 0;
            // Hours entered for today make it a work day even when the calendar says otherwise.
            DayOverride active = ActiveOverride(now);
            return PayEstimate.At(AnnualSalary, active != null ? active.Date : Schedule.WorkDateAt(now),
                countdown.Progress, Calendar(holidays), active != null);
        }

        // Days until the next payday (0: today); null when no payday is set.
        public int? PaydayDaysLeft(DateTime now, HolidayCalendar holidays)
        {
            if (Payday <= 0) return null;
            return Math.Max(0, (PaydayRule.Next(Payday, now.Date, holidays) - now.Date).Days);
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

        // Work days come from the user's calendar; a duty day is paid even if the calendar calls it a day off.
        public static long At(long annualSalary, DateTime workDate, double progress, WorkCalendar calendar,
            bool dutyDay = false)
        {
            if (!dutyDay && !calendar.IsWorkDay(workDate)) return 0;
            int days = calendar.WorkDaysInMonth(workDate.Year, workDate.Month);
            if (days == 0) return 0;
            return (long)(annualSalary / 12m / days * (decimal)progress);
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

        // The same lunch break stays on only if it lies fully inside the new hours.
        public Schedule WithWindow(TimeSpan start, TimeSpan end)
        {
            TimeSpan shift = Wrap(end - start);
            bool inside = Lunch.Enabled && LunchLength < shift && Wrap(Lunch.Start - start) + LunchLength <= shift;
            return new Schedule(start, end, new LunchBreak(inside, Lunch.Start, Lunch.End));
        }

        // Half of the paid time, rounded down to the minute.
        private TimeSpan HalfOfWork
        {
            get
            {
                TimeSpan net = Wrap(End - Start) - (Lunch.Enabled ? LunchLength : TimeSpan.Zero);
                return TimeSpan.FromMinutes(Math.Floor(net.TotalMinutes / 2));
            }
        }

        // 오후 반차: work the morning half, with lunch counted if it falls inside.
        public Schedule HalfDayAfternoonOff()
        {
            TimeSpan half = HalfOfWork;
            TimeSpan end = Start + half + (Lunch.Enabled && LunchOffset < half ? LunchLength : TimeSpan.Zero);
            return WithWindow(Start, Wrap(end));
        }

        // 오전 반차: work the afternoon half.
        public Schedule HalfDayMorningOff()
        {
            TimeSpan half = HalfOfWork;
            TimeSpan shift = Wrap(End - Start);
            TimeSpan lead = half + (Lunch.Enabled && LunchOffset + LunchLength > shift - half ? LunchLength : TimeSpan.Zero);
            return WithWindow(Wrap(End - lead), End);
        }

        internal static TimeSpan Wrap(TimeSpan time)
        {
            return TimeSpan.FromTicks((time.Ticks % Day.Ticks + Day.Ticks) % Day.Ticks);
        }

        public Countdown At(DateTime now)
        {
            return At(now, WorkDateAt(now));
        }

        // The shift instance that starts at workDate + Start (it may end the next day).
        public Countdown At(DateTime now, DateTime workDate)
        {
            DateTime start = workDate.Date.Add(Start);
            DateTime end = start + Wrap(End - Start);

            TimeSpan remaining = now < end ? end - now : TimeSpan.Zero;
            TimeSpan overtime = now < end ? TimeSpan.Zero : now - end;
            if (!Lunch.Enabled)
                return new Countdown(remaining, Ratio(now - start, end - start), CountdownTarget.Leave, overtime);

            // Progress, and so the day's pay, pauses over lunch.
            DateTime lunchStart = start.Add(LunchOffset);
            DateTime lunchEnd = lunchStart.Add(LunchLength);
            TimeSpan worked = now - start;
            if (now > lunchStart) worked -= (now < lunchEnd ? now : lunchEnd) - lunchStart;
            double progress = Ratio(worked, end - start - LunchLength);
            if (now < lunchStart) return new Countdown(lunchStart - now, progress, CountdownTarget.Lunch);
            if (now < lunchEnd) return new Countdown(lunchEnd - now, progress, CountdownTarget.LunchEnd);
            return new Countdown(remaining, progress, CountdownTarget.Leave, overtime);
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
        private readonly HashSet<DateTime> notifiedDeadlines = new HashSet<DateTime>();
        private DateTime previousCheck;
        // The deadline of the most recent notification, for the caller's relevance window.
        public DateTime LastDeadline { get; private set; }

        public FinishReminder(DateTime now)
        {
            Reset(now);
        }

        public void Reset(DateTime now)
        {
            previousCheck = now;
        }

        // A late first look (sleep, resume) still counts while the notice is relevant.
        public bool ShouldNotify(Schedule schedule, DateTime now, TimeSpan validFor)
        {
            return Crossed(schedule.DeadlineAt(now), now, validFor);
        }

        public bool ShouldNotify(TimeSpan timeOfDay, DateTime now, TimeSpan validFor)
        {
            return Crossed(now.Date.Add(timeOfDay), now, validFor);
        }

        private bool Crossed(DateTime deadline, DateTime now, TimeSpan validFor)
        {
            if (deadline > now) deadline = deadline.AddDays(-1);
            bool crossedDeadline = previousCheck < deadline;
            previousCheck = now;
            bool due = crossedDeadline && now - deadline <= validFor && notifiedDeadlines.Add(deadline);
            if (due) LastDeadline = deadline;
            return due;
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
        private static readonly TimeSpan LeaveRelevance = TimeSpan.FromMinutes(30);
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

        // The notice stays worth showing until this time (set when Check returns a milestone).
        public DateTime RelevantUntil { get; private set; }

        // Call on every tick so each reminder keeps its own last-check time.
        public Milestone Check(Schedule schedule, DateTime now)
        {
            LunchBreak lunch = schedule.Lunch;
            bool leaving = leave.ShouldNotify(schedule, now, LeaveRelevance);
            bool starts = lunch.Enabled && lunchStarts.ShouldNotify(lunch.Start, now, lunch.Length);
            bool ending = lunch.Enabled && lunch.Length > EndingWarning + EndingWarning &&
                lunchEnding.ShouldNotify(Schedule.Wrap(lunch.End - EndingWarning), now, EndingWarning);
            if (leaving)
            {
                RelevantUntil = leave.LastDeadline + LeaveRelevance;
                return Milestone.Leave;
            }
            if (starts)
            {
                RelevantUntil = lunchStarts.LastDeadline + lunch.Length;
                return Milestone.LunchStarts;
            }
            if (!ending) return Milestone.None;
            RelevantUntil = lunchEnding.LastDeadline + EndingWarning;
            return Milestone.LunchEnding;
        }
    }

    internal enum CountdownTarget
    {
        Leave,
        Lunch,
        LunchEnd,
        DayOff
    }

    internal struct Countdown
    {
        // Time left to Target: leaving work, or the start or end of lunch.
        public readonly TimeSpan Remaining;
        public readonly double Progress;
        public readonly CountdownTarget Target;
        // Time since the leave deadline once finished; zero otherwise.
        public readonly TimeSpan Overtime;
        // Name of the day off (holiday, weekend...) when Target is DayOff.
        public readonly string DayOffName;
        public bool Finished
        {
            get { return Target == CountdownTarget.DayOff || (Target == CountdownTarget.Leave && Remaining == TimeSpan.Zero); }
        }

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

        public Countdown(TimeSpan remaining, double progress, CountdownTarget target = CountdownTarget.Leave,
            TimeSpan overtime = default(TimeSpan), string dayOffName = null)
        {
            Remaining = remaining;
            Progress = progress;
            Target = target;
            Overtime = overtime;
            DayOffName = dayOffName;
        }

        public static Countdown DayOff(string name)
        {
            return new Countdown(TimeSpan.Zero, 0, CountdownTarget.DayOff, TimeSpan.Zero, name);
        }

        // The overtime as a running count, so it formats like any other remaining time.
        public Countdown AsOvertime()
        {
            return new Countdown(Overtime, Progress);
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
                    bool showOvertime;
                    if (lines.Length < 14 || !bool.TryParse(lines[13], out showOvertime)) showOvertime = false;
                    int workDays;
                    if (lines.Length < 15 || !WorkWeek.TryParse(lines[14], out workDays)) workDays = WorkWeek.Default;
                    bool skipHolidays;
                    if (lines.Length < 16 || !bool.TryParse(lines[15], out skipHolidays)) skipHolidays = true;
                    int payday;
                    if (lines.Length < 17 || !int.TryParse(lines[16], NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out payday) || payday < 0 || payday > 31)
                        payday = 0;
                    DayOverride todayOverride = lines.Length >= 18 ? DayOverride.Parse(lines[17]) : null;
                    return new ClockSettings(schedule, display, lines.Length >= 4 ? lines[3] : "",
                        transparency, showProgress, notifyAtEnd, theme, pinInside, salary, autoUpdate, progressColor,
                        showOvertime, workDays, skipHolidays, payday, todayOverride);
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
                settings.Schedule.Lunch.Format(),
                settings.ShowOvertime.ToString(),
                WorkWeek.Format(settings.WorkDays),
                settings.SkipHolidays.ToString(),
                settings.Payday.ToString(CultureInfo.InvariantCulture),
                // A stale override (another work date) is written back as empty.
                settings.ActiveOverride(DateTime.Now) == null ? "" : settings.TodayOverride.Format()
            });
            if (File.Exists(path))
                File.Replace(temporaryPath, path, null);
            else
                File.Move(temporaryPath, path);
        }
    }
}
