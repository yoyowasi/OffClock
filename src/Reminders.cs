using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;

namespace OffClock
{
    internal enum ReminderAnchor { Clock, Leave, Start }

    // One line of reminders.txt: a clock time, or minutes before/after the effective leave or start time.
    internal sealed class Reminder
    {
        public readonly ReminderAnchor Anchor;
        public readonly TimeSpan Clock;
        public readonly int Minutes;
        public readonly string Text;

        public Reminder(ReminderAnchor anchor, TimeSpan clock, int minutes, string text)
        {
            Anchor = anchor;
            Clock = clock;
            Minutes = minutes;
            Text = text;
        }

        // The notice body: "17:30 알림", "퇴근 30분 전", "출근 10분 후", "퇴근 시각".
        public string Describe()
        {
            if (Anchor == ReminderAnchor.Clock) return Schedule.Format(Clock) + " 알림";
            string name = Anchor == ReminderAnchor.Leave ? "퇴근" : "출근";
            if (Minutes == 0) return name + " 시각";
            return name + " " + Math.Abs(Minutes) + (Minutes < 0 ? "분 전" : "분 후");
        }
    }

    // A reminder resolved to a concrete moment. Key identifies the occurrence across file reloads.
    internal sealed class ReminderOccurrence
    {
        public readonly Reminder Reminder;
        public readonly DateTime Due;
        // The work date whose day-off state decides whether this fires.
        public readonly DateTime WorkDate;

        public ReminderOccurrence(Reminder reminder, DateTime due, DateTime workDate)
        {
            Reminder = reminder;
            Due = due;
            WorkDate = workDate;
        }

        public string Key { get { return Reminder.Text + "|" + Due.Ticks; } }
    }

    internal sealed class ReminderList
    {
        public const int MaxReminders = 50;
        public const int MaxTextLength = 40;
        public const int MaxMinutes = 720;
        // A reminder missed by sleep or a fullscreen app still fires for this long after its due time.
        public static readonly TimeSpan Grace = TimeSpan.FromMinutes(10);
        public static readonly string DefaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OffClock", "reminders.txt");
        public static readonly ReminderList Empty = new ReminderList(new string[0]);

        private const string UserFileTemplate =
            "# 퇴근시계 알림 목록 (저장하면 1분 안에 반영됩니다)\r\n" +
            "# 근무일에만 무음 알림을 띄웁니다. 한 줄에 하나씩 씁니다. 맨 앞의 #을 지우면 켜집니다.\r\n" +
            "# 정한 시각:\r\n" +
            "#17:30 일일 보고 쓰기\r\n" +
            "# 퇴근 30분 전 (반차·오늘만 변경도 따라갑니다):\r\n" +
            "#퇴근-30 타임시트 입력\r\n" +
            "# 출근 10분 뒤:\r\n" +
            "#출근+10 메일 확인\r\n";

        private readonly List<Reminder> items = new List<Reminder>();

        public ReminderList(IEnumerable<string> lines)
        {
            foreach (string raw in lines)
            {
                if (items.Count >= MaxReminders) break;
                Reminder parsed = Parse(raw);
                if (parsed != null) items.Add(parsed);
            }
        }

        public int Count { get { return items.Count; } }

        public static ReminderList Load(string path)
        {
            ReminderList list;
            return TryLoad(path, out list) ? list : Empty;
        }

        // True with the parsed file, or with Empty when the file or folder does not exist; false when the
        // file exists but could not be read now (the caller should retry).
        public static bool TryLoad(string path, out ReminderList list)
        {
            try
            {
                list = new ReminderList(File.ReadAllLines(path));
                return true;
            }
            catch (FileNotFoundException) { list = Empty; return true; }
            catch (DirectoryNotFoundException) { list = Empty; return true; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }
            list = null;
            return false;
        }

        // Creates the commented template the first time someone opens the file to edit it.
        public static void EnsureUserFile(string path)
        {
            if (File.Exists(path)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, UserFileTemplate);
        }

        // Every occurrence that can matter at now, each with the work date it belongs to: the shift instances
        // before, of and after the current work date, so a reminder before the shift start (the work date
        // flips there on overnight shifts) or after its end is not lost. Each instance uses the schedule of
        // its own work date. A clock time belongs to the instance whose window holds it, else to its own date.
        public List<ReminderOccurrence> Occurrences(ClockSettings settings, DateTime now)
        {
            var result = new List<ReminderOccurrence>();
            DateTime baseDate = settings.Schedule.WorkDateAt(now).Date;
            var starts = new DateTime[3];
            var ends = new DateTime[3];
            for (int i = 0; i < 3; i++)
            {
                DateTime workDate = baseDate.AddDays(i - 1);
                Schedule schedule = settings.ScheduleFor(workDate);
                starts[i] = workDate.Add(schedule.Start);
                ends[i] = starts[i] + Schedule.Wrap(schedule.End - schedule.Start);
                foreach (Reminder item in items)
                {
                    if (item.Anchor == ReminderAnchor.Clock) continue;
                    DateTime anchor = item.Anchor == ReminderAnchor.Leave ? ends[i] : starts[i];
                    result.Add(new ReminderOccurrence(item, anchor.AddMinutes(item.Minutes), workDate));
                }
            }
            foreach (Reminder item in items)
            {
                if (item.Anchor != ReminderAnchor.Clock) continue;
                for (int back = 1; back >= 0; back--)
                {
                    DateTime due = now.Date.AddDays(-back).Add(item.Clock);
                    DateTime workDate = due.Date;
                    for (int i = 0; i < 3; i++)
                        if (due >= starts[i] && due < ends[i]) workDate = baseDate.AddDays(i - 1);
                    result.Add(new ReminderOccurrence(item, due, workDate));
                }
            }
            return result.OrderBy(o => o.Due).ToList();
        }

        // The occurrences due now on a work day: reached, and not older than the grace period.
        public IEnumerable<ReminderOccurrence> DueAt(ClockSettings settings, HolidayCalendar holidays, DateTime now)
        {
            return Occurrences(settings, now).Where(
                o => o.Due <= now && now <= o.Due + Grace && !settings.IsDayOff(o.WorkDate, holidays));
        }

        // The first reminder still to come today on a work day, or null.
        public ReminderOccurrence NextToday(ClockSettings settings, HolidayCalendar holidays, DateTime now)
        {
            return Occurrences(settings, now).FirstOrDefault(
                o => o.Due > now && o.Due.Date == now.Date && !settings.IsDayOff(o.WorkDate, holidays));
        }

        // "HH:mm text", "퇴근-N text", "퇴근+N text", "출근-N text", "출근+N text"; null for anything else.
        private static Reminder Parse(string raw)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line[0] == '#') return null;
            int space = IndexOfWhitespace(line);
            if (space < 0) return null;
            string head = line.Substring(0, space);
            string text = line.Substring(space + 1).Trim();
            if (text.Length == 0) return null;
            if (text.Length > MaxTextLength) text = text.Substring(0, MaxTextLength).TrimEnd();

            ReminderAnchor anchor;
            if (head.StartsWith("퇴근", StringComparison.Ordinal)) anchor = ReminderAnchor.Leave;
            else if (head.StartsWith("출근", StringComparison.Ordinal)) anchor = ReminderAnchor.Start;
            else
            {
                TimeSpan clock;
                if (!TimeSpan.TryParseExact(head, new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out clock))
                    return null;
                return new Reminder(ReminderAnchor.Clock, clock, 0, text);
            }

            // Sign, then 1 to 3 digits.
            if (head.Length < 4 || head.Length > 6 || (head[2] != '-' && head[2] != '+')) return null;
            int minutes = 0;
            for (int i = 3; i < head.Length; i++)
            {
                if (head[i] < '0' || head[i] > '9') return null;
                minutes = minutes * 10 + (head[i] - '0');
            }
            if (minutes > MaxMinutes) return null;
            return new Reminder(anchor, TimeSpan.Zero, head[2] == '-' ? -minutes : minutes, text);
        }

        private static int IndexOfWhitespace(string value)
        {
            for (int i = 0; i < value.Length; i++)
                if (char.IsWhiteSpace(value[i])) return i;
            return -1;
        }
    }
}
