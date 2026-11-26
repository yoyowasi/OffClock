using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security;

namespace OffClock
{
    // Korean public holidays (관공서의 공휴일 with 대체·임시공휴일), weekends included so Saturday shifts see them too.
    // 2026-2027 follow the official 월력요항 plus the April 2026 노동절·제헌절 designation. 2028 is provisional KASI
    // data: recheck it against the 2028 월력요항 (published around June 2027). Users add or drop days in holidays.txt.
    internal sealed class HolidayCalendar
    {
        public const int LastBuiltinYear = 2028;
        public static readonly string DefaultPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "OffClock", "holidays.txt");
        public static readonly HolidayCalendar Builtin = new HolidayCalendar(new string[0]);

        private const string BuiltinTable = @"
2026-01-01 신정
2026-02-16 설날 연휴
2026-02-17 설날
2026-02-18 설날 연휴
2026-03-01 삼일절
2026-03-02 대체공휴일
2026-05-01 노동절
2026-05-05 어린이날
2026-05-24 부처님오신날
2026-05-25 대체공휴일
2026-06-03 지방선거일
2026-06-06 현충일
2026-07-17 제헌절
2026-08-15 광복절
2026-08-17 대체공휴일
2026-09-24 추석 연휴
2026-09-25 추석
2026-09-26 추석 연휴
2026-10-03 개천절
2026-10-05 대체공휴일
2026-10-09 한글날
2026-12-25 성탄절
2027-01-01 신정
2027-02-06 설날 연휴
2027-02-07 설날
2027-02-08 설날 연휴
2027-02-09 대체공휴일
2027-03-01 삼일절
2027-05-01 노동절
2027-05-03 대체공휴일
2027-05-05 어린이날
2027-05-13 부처님오신날
2027-06-06 현충일
2027-07-17 제헌절
2027-07-19 대체공휴일
2027-08-15 광복절
2027-08-16 대체공휴일
2027-09-14 추석 연휴
2027-09-15 추석
2027-09-16 추석 연휴
2027-10-03 개천절
2027-10-04 대체공휴일
2027-10-09 한글날
2027-10-11 대체공휴일
2027-12-25 성탄절
2027-12-27 대체공휴일
2028-01-01 신정
2028-01-26 설날 연휴
2028-01-27 설날
2028-01-28 설날 연휴
2028-03-01 삼일절
2028-04-12 국회의원선거일
2028-05-01 노동절
2028-05-02 부처님오신날
2028-05-05 어린이날
2028-06-06 현충일
2028-07-17 제헌절
2028-08-15 광복절
2028-10-02 추석 연휴
2028-10-03 추석
2028-10-04 추석 연휴
2028-10-05 대체공휴일
2028-10-09 한글날
2028-12-25 성탄절
";

        private const string UserFileTemplate =
            "# 퇴근시계 휴일 목록 (저장하면 다음 확인 때 반영됩니다)\r\n" +
            "# 쉬는 날 추가: 한 줄에 날짜와 이름. 예) 2027-03-15 창립기념일\r\n" +
            "# 기본 공휴일에 출근하면 앞에 -를 붙이세요. 예) -2027-06-03\r\n";

        private readonly Dictionary<DateTime, string> names = new Dictionary<DateTime, string>();

        // User lines are applied after the built-in table: "yyyy-MM-dd 이름" adds a day, "-yyyy-MM-dd" drops one.
        public HolidayCalendar(IEnumerable<string> userLines)
        {
            Apply(BuiltinTable.Split('\n'));
            Apply(userLines);
        }

        public static HolidayCalendar Load(string path)
        {
            HolidayCalendar calendar;
            return TryLoad(path, out calendar) ? calendar : Builtin;
        }

        // True with the parsed file, or with Builtin when the file or folder does not exist; false when the
        // file exists but could not be read now (the caller should retry).
        public static bool TryLoad(string path, out HolidayCalendar calendar)
        {
            try
            {
                calendar = new HolidayCalendar(File.ReadAllLines(path));
                return true;
            }
            catch (FileNotFoundException) { calendar = Builtin; return true; }
            catch (DirectoryNotFoundException) { calendar = Builtin; return true; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }
            calendar = null;
            return false;
        }

        // Creates the commented template the first time someone opens the file to edit it.
        public static void EnsureUserFile(string path)
        {
            if (File.Exists(path)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, UserFileTemplate);
        }

        public bool IsHoliday(DateTime date)
        {
            return names.ContainsKey(date.Date);
        }

        // Null when the date is not a holiday.
        public string NameOn(DateTime date)
        {
            string name;
            return names.TryGetValue(date.Date, out name) ? name : null;
        }

        private void Apply(IEnumerable<string> lines)
        {
            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                bool drop = line[0] == '-';
                if (drop) line = line.Substring(1).TrimStart();
                int space = line.IndexOf(' ');
                DateTime date;
                if (!DateTime.TryParseExact(space < 0 ? line : line.Substring(0, space), "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                    continue;
                if (drop) names.Remove(date);
                else names[date] = space < 0 ? "휴일" : line.Substring(space + 1).Trim();
            }
        }
    }
}
