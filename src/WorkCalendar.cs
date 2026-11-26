using System;
using System.Globalization;

namespace OffClock
{
    // Work-day mask: bit 0 is Monday ... bit 6 is Sunday.
    internal static class WorkWeek
    {
        public const int Default = 0x1F;

        public static int Bit(DayOfWeek day)
        {
            return 1 << (((int)day + 6) % 7);
        }

        public static bool Includes(int mask, DayOfWeek day)
        {
            return (mask & Bit(day)) != 0;
        }

        public static string Format(int mask)
        {
            var chars = new char[7];
            for (int i = 0; i < 7; i++) chars[i] = (mask & (1 << i)) != 0 ? '1' : '0';
            return new string(chars);
        }

        public static bool TryParse(string text, out int mask)
        {
            mask = Default;
            if (text == null || text.Length != 7) return false;
            int result = 0;
            for (int i = 0; i < 7; i++)
            {
                if (text[i] == '1') result |= 1 << i;
                else if (text[i] != '0') return false;
            }
            mask = result;
            return true;
        }
    }

    // Which dates are work days: the weekday mask, minus public holidays when the user skips them.
    internal sealed class WorkCalendar
    {
        private readonly int mask;
        private readonly bool skipHolidays;
        private readonly HolidayCalendar holidays;

        public WorkCalendar(int mask, bool skipHolidays, HolidayCalendar holidays)
        {
            this.mask = mask;
            this.skipHolidays = skipHolidays;
            this.holidays = holidays ?? HolidayCalendar.Builtin;
        }

        public bool IsWorkDay(DateTime date)
        {
            return DayOffName(date) == null;
        }

        // Null on a work day.
        public string DayOffName(DateTime date)
        {
            if (skipHolidays)
            {
                string holiday = holidays.NameOn(date);
                if (holiday != null) return holiday;
            }
            if (WorkWeek.Includes(mask, date.DayOfWeek)) return null;
            return date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday ? "주말" : "휴무일";
        }

        public int WorkDaysInMonth(int year, int month)
        {
            int count = 0;
            for (int day = 1, days = DateTime.DaysInMonth(year, month); day <= days; day++)
                if (IsWorkDay(new DateTime(year, month, day))) count++;
            return count;
        }
    }

    // A one-day change to the schedule: a day off, or other hours. Lunch follows the base schedule.
    internal sealed class DayOverride
    {
        public readonly DateTime Date;
        public readonly bool Off;
        public readonly TimeSpan Start;
        public readonly TimeSpan End;

        private DayOverride(DateTime date, bool off, TimeSpan start, TimeSpan end)
        {
            Date = date.Date;
            Off = off;
            Start = start;
            End = end;
        }

        public static DayOverride Rest(DateTime date)
        {
            return new DayOverride(date, true, TimeSpan.Zero, TimeSpan.Zero);
        }

        public static DayOverride Hours(DateTime date, TimeSpan start, TimeSpan end)
        {
            return new DayOverride(date, false, start, end);
        }

        public string Format()
        {
            string date = Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            return Off ? date + " Off" : date + " " + Schedule.Format(Start) + "-" + Schedule.Format(End);
        }

        public static DayOverride Parse(string text)
        {
            string[] parts = (text ?? "").Split(' ');
            DateTime date;
            if (parts.Length != 2 ||
                !DateTime.TryParseExact(parts[0], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                return null;
            if (parts[1] == "Off") return Rest(date);
            string[] times = parts[1].Split('-');
            TimeSpan start, end;
            if (times.Length != 2 ||
                !TimeSpan.TryParseExact(times[0], @"hh\:mm", CultureInfo.InvariantCulture, out start) ||
                !TimeSpan.TryParseExact(times[1], @"hh\:mm", CultureInfo.InvariantCulture, out end) || start == end)
                return null;
            return Hours(date, start, end);
        }
    }

    internal static class PaydayRule
    {
        // The payday falls back to the last day before it that is neither a weekend nor a public holiday.
        public static DateTime In(int payday, int year, int month, HolidayCalendar holidays)
        {
            if (holidays == null) holidays = HolidayCalendar.Builtin;
            DateTime date = new DateTime(year, month, Math.Min(payday, DateTime.DaysInMonth(year, month)));
            while (date.DayOfWeek == DayOfWeek.Saturday || date.DayOfWeek == DayOfWeek.Sunday || holidays.IsHoliday(date))
                date = date.AddDays(-1);
            return date;
        }

        // The first payday on or after today. A month's payday can be moved back into the month before,
        // so look at the current month and the next three.
        public static DateTime Next(int payday, DateTime today, HolidayCalendar holidays)
        {
            today = today.Date;
            DateTime first = new DateTime(today.Year, today.Month, 1);
            DateTime result = today;
            for (int i = 0; i < 4; i++)
            {
                DateTime month = first.AddMonths(i);
                result = In(payday, month.Year, month.Month, holidays);
                if (result >= today) return result;
            }
            return result;
        }
    }
}
