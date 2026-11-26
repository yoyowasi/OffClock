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

namespace OffClock
{
    internal sealed class ClockSettingsDialog : Form
    {
        private readonly TrackBar transparency;
        private readonly CheckBox progress;
        private readonly Font formFont;
        private readonly DayOverride todayOverride;
        private readonly ComboBox theme;
        private readonly Label swatch;
        private readonly Button pickColor;
        private readonly Button resetColor;
        private readonly Label feedback;
        private Color progressColor;
        private readonly CheckBox startAtLogin;
        public bool StartAtLogin { get { return startAtLogin.Checked; } }
        public Action<ClockSettings> StoreSettings { get; set; }
        public Action CheckForUpdate { get; set; }
        public Action<int, bool, ClockTheme, Color> PreviewAppearance { get; set; }
        // Receives the bar color shown now; returns null when the user cancels.
        public Func<Color, Color?> PickProgressColor { get; set; }
        public int SelectedTransparency { get { return transparency.Value; } }
        public bool ShowProgress { get { return progress.Checked; } }
        public ClockTheme SelectedTheme { get { return (ClockTheme)theme.SelectedIndex; } }
        public Color SelectedProgressColor { get { return progressColor; } }

        public static readonly string[] DisplayLabels =
        {
            "시:분:초.밀리초", "시간만", "분만", "초만", "밀리초만", "2진수", "8진수", "16진수", "오늘 급여", "월급날 D-day"
        };

        public static readonly string[] ThemeLabels =
        {
            "다크", "라이트", "벚꽃", "바다", "숲", "노을", "고양이", "곰", "토끼", "펭귄", "병아리"
        };

        public ClockSettingsDialog(ClockSettings settings, Icon icon, bool registeredAtLogin = false)
        {
            Text = "퇴근시계 설정";
            if (icon != null) Icon = icon;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(324, 424);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            formFont = new Font("Malgun Gothic", 9);
            Font = formFont;
            BackColor = SystemColors.Window;
            ForeColor = SystemColors.WindowText;

            // Work tab: three more rows (work days, holidays, payday) are planned below the lunch row.
            TabPage workPage = NewPage("WorkTab", "근무");
            var start = TimePicker(workPage, settings.Schedule.Start, "출근 시간", 14);
            var end = TimePicker(workPage, settings.Schedule.End, "퇴근 시간", 55);
            LunchBreak lunch = settings.Schedule.Lunch;
            var lunchEnabled = new CheckBox
            {
                Name = "LunchEnabled", Text = "점심시간", Checked = lunch.Enabled, AutoSize = true,
                Location = new Point(12, 99), TabIndex = 2
            };
            var lunchStart = LunchPicker("LunchStart", "점심 시작", lunch.Start, 134);
            var lunchDash = new Label { Text = "~", AutoSize = true, Location = new Point(205, 100) };
            var lunchEnd = LunchPicker("LunchEnd", "점심 끝", lunch.End, 220);
            lunchStart.TabIndex = 3;
            lunchEnd.TabIndex = 4;
            EventHandler refreshLunch = delegate { lunchStart.Enabled = lunchEnd.Enabled = lunchEnabled.Checked; };
            lunchEnabled.CheckedChanged += refreshLunch;
            refreshLunch(lunchEnabled, EventArgs.Empty);
            workPage.Controls.AddRange(new Control[] { start, end, lunchEnabled, lunchStart, lunchDash, lunchEnd });
            todayOverride = settings.TodayOverride;

            workPage.Controls.Add(new Label { Text = "근무 요일", AutoSize = true, Location = new Point(12, 140) });
            string[] dayNames = { "월", "화", "수", "목", "금", "토", "일" };
            string[] dayKeys = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
            var dayBoxes = new CheckBox[7];
            for (int i = 0; i < 7; i++)
            {
                dayBoxes[i] = new CheckBox
                {
                    Name = "WorkDay" + dayKeys[i], AccessibleName = dayNames[i] + "요일 근무", Text = dayNames[i],
                    Checked = (settings.WorkDays & (1 << i)) != 0, AutoSize = true,
                    Location = new Point(12 + i * 40, 162), TabIndex = 5 + i
                };
                workPage.Controls.Add(dayBoxes[i]);
            }
            var skipHolidays = new CheckBox
            {
                Name = "SkipHolidays", Text = "공휴일에 쉬기", Checked = settings.SkipHolidays,
                AutoSize = true, Location = new Point(12, 196), TabIndex = 12
            };
            var editHolidays = new Button
            {
                Name = "EditHolidays", Text = "휴일 편집…", Location = new Point(186, 192), Size = new Size(102, 28), TabIndex = 13
            };
            editHolidays.Click += delegate { OpenHolidayFile(); };
            var holidayHint = new Label
            {
                Text = "공휴일은 " + HolidayCalendar.LastBuiltinYear + "년까지 들어 있습니다.",
                ForeColor = SystemColors.GrayText, AutoSize = true, Location = new Point(12, 224)
            };
            workPage.Controls.Add(new Label { Text = "월급날", AutoSize = true, Location = new Point(12, 258) });
            var payday = new ComboBox
            {
                Name = "Payday", AccessibleName = "월급날", DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(138, 254), Size = new Size(150, 26), TabIndex = 14
            };
            payday.Items.Add("사용 안 함");
            for (int day = 1; day <= 31; day++) payday.Items.Add(day + "일");
            payday.SelectedIndex = settings.Payday;
            workPage.Controls.AddRange(new Control[] { skipHolidays, editHolidays, holidayHint, payday });

            TabPage displayPage = NewPage("DisplayTab", "표시");
            var display = AddDropDown(displayPage, "DisplayMode", "표시 형식", 14);
            display.Items.AddRange(DisplayLabels);
            display.SelectedIndex = (int)settings.Display;
            var monitor = AddDropDown(displayPage, "Monitor", "표시할 모니터", 52);
            PopulateMonitors(monitor, settings.Monitor);
            theme = AddDropDown(displayPage, "Theme", "테마", 90);
            theme.Items.AddRange(ThemeLabels);
            theme.SelectedIndex = (int)settings.Theme;
            var transparencyLabel = new Label { Text = "투명도", AutoSize = true, Location = new Point(12, 137) };
            transparency = new TrackBar
            {
                Name = "Transparency", AccessibleName = "투명도", TickStyle = TickStyle.None,
                Minimum = 0, Maximum = ClockSettings.MaxTransparency, Value = settings.Transparency,
                SmallChange = 1, LargeChange = 10, AutoSize = false, Location = new Point(138, 130),
                Size = new Size(112, 32), TabIndex = 8
            };
            var percentage = new Label
            {
                Text = settings.Transparency + "%", TextAlign = ContentAlignment.MiddleRight,
                Location = new Point(250, 133), Size = new Size(38, 23)
            };
            progress = new CheckBox
            {
                Name = "ShowProgress", Text = "진행 막대 표시", Checked = settings.ShowProgress,
                AutoSize = true, Location = new Point(12, 176), TabIndex = 9
            };
            progressColor = settings.ProgressColor;
            swatch = new Label
            {
                Name = "ProgressColorSwatch", AccessibleName = "진행 막대 색", BorderStyle = BorderStyle.FixedSingle,
                Location = new Point(138, 175), Size = new Size(22, 22)
            };
            pickColor = new Button
            {
                Name = "ProgressColor", Text = "색 변경", Location = new Point(166, 172), Size = new Size(64, 28), TabIndex = 10
            };
            resetColor = new Button
            {
                Name = "ResetProgressColor", Text = "기본값", Location = new Point(234, 172), Size = new Size(54, 28), TabIndex = 11
            };
            PickProgressColor = ShowColorDialog;
            pickColor.Click += delegate
            {
                if (PickProgressColor == null) return;
                Color? picked = PickProgressColor(swatch.BackColor);
                if (picked.HasValue) SetProgressColor(picked.Value);
            };
            resetColor.Click += delegate { SetProgressColor(Color.Empty); };
            var pinInside = new CheckBox
            {
                Name = "PinInside", Text = "가로 작업표시줄 안에 고정", Checked = settings.PinInside,
                AutoSize = true, Location = new Point(12, 214), TabIndex = 12
            };
            var showOvertime = new CheckBox
            {
                Name = "ShowOvertime", Text = "퇴근 후 초과 근무 시간 표시", Checked = settings.ShowOvertime,
                AutoSize = true, Location = new Point(12, 246), TabIndex = 13
            };
            display.TabIndex = 5;
            monitor.TabIndex = 6;
            theme.TabIndex = 7;
            displayPage.Controls.AddRange(new Control[]
            {
                transparencyLabel, transparency, percentage, progress, swatch, pickColor, resetColor, pinInside, showOvertime
            });

            TabPage otherPage = NewPage("OtherTab", "알림·기타");
            var notify = new CheckBox
            {
                Name = "NotifyAtEnd", Text = "퇴근·점심 시간에 화면 알림 (무음)", Checked = settings.NotifyAtEnd,
                AutoSize = true, Location = new Point(12, 14), TabIndex = 0
            };
            var editReminders = new Button
            {
                Name = "EditReminders", Text = "알림 편집…", Location = new Point(186, 40), Size = new Size(102, 28), TabIndex = 1
            };
            editReminders.Click += delegate { OpenReminderFile(); };
            var reminderHint = new Label
            {
                Text = "근무일마다 정한 시각에 무음 알림을 띄웁니다.",
                ForeColor = SystemColors.GrayText, AutoSize = true, MaximumSize = new Size(276, 0), Location = new Point(12, 72)
            };
            startAtLogin = new CheckBox
            {
                Name = "StartAtLogin", Text = "Windows 로그인 시 자동 시작", Checked = registeredAtLogin,
                AutoSize = true, Location = new Point(12, 100), TabIndex = 2
            };
            var salaryLabel = new Label { Text = "세전 연봉 (원)", AutoSize = true, Location = new Point(12, 145) };
            var salary = new TextBox
            {
                Name = "AnnualSalary", AccessibleName = "세전 연봉 (원)", Location = new Point(138, 140),
                Width = 150, MaxLength = 20, TextAlign = HorizontalAlignment.Right, TabIndex = 3,
                Text = settings.AnnualSalary == 0 ? "" : settings.AnnualSalary.ToString("N0", CultureInfo.InvariantCulture)
            };
            var salaryInKorean = new Label
            {
                Name = "AnnualSalaryInKorean", ForeColor = SystemColors.GrayText, TextAlign = ContentAlignment.TopRight,
                Location = new Point(12, 166), Size = new Size(276, 36)
            };
            EventHandler refreshSalary = delegate
            {
                long amount;
                salaryInKorean.Text = string.IsNullOrWhiteSpace(salary.Text) ? "" :
                    (PayEstimate.TryParseSalary(salary.Text, out amount) ? PayEstimate.InKorean(amount) : "연봉 숫자를 확인하세요.");
            };
            salary.TextChanged += refreshSalary;
            refreshSalary(salary, EventArgs.Empty);
            var autoUpdates = new CheckBox
            {
                Name = "AutoUpdates", Text = "자동 업데이트", Checked = settings.AutoUpdate,
                AutoSize = true, Location = new Point(12, 214), TabIndex = 4
            };
            var checkUpdate = new Button
            {
                Name = "CheckForUpdate", Text = "업데이트 확인", Location = new Point(176, 208),
                Size = new Size(112, 30), TabIndex = 5
            };
            checkUpdate.Click += delegate { if (CheckForUpdate != null) CheckForUpdate(); };
            otherPage.Controls.AddRange(new Control[]
            {
                notify, editReminders, reminderHint, startAtLogin, salaryLabel, salary, salaryInKorean, autoUpdates, checkUpdate
            });

            transparency.ValueChanged += delegate
            {
                percentage.Text = transparency.Value + "%";
                Preview();
            };
            progress.CheckedChanged += delegate
            {
                RefreshProgressColor();
                Preview();
            };
            theme.SelectedIndexChanged += delegate
            {
                RefreshProgressColor();
                Preview();
            };
            RefreshProgressColor();
            start.TabIndex = 0;
            end.TabIndex = 1;

            var tabs = new TabControl
            {
                Name = "Tabs", Location = new Point(8, 8), Size = new Size(308, 328), TabIndex = 0
            };
            tabs.TabPages.AddRange(new[] { workPage, displayPage, otherPage });
            feedback = new Label
            {
                Name = "Feedback",
                Text = "퇴근 시간이 출근보다 이르면 다음 날로 계산합니다.",
                AutoSize = true,
                MaximumSize = new Size(300, 0),
                Location = new Point(12, 344)
            };
            var save = new Button { Text = "저장", Location = new Point(237, 386), Size = new Size(79, 28), TabIndex = 3 };
            var cancel = new Button
            {
                Text = "취소", Location = new Point(150, 386), Size = new Size(79, 28), TabIndex = 2,
                DialogResult = DialogResult.Cancel
            };
            save.Click += delegate
            {
                Save(start.Value.TimeOfDay, end.Value.TimeOfDay,
                    new LunchBreak(lunchEnabled.Checked, lunchStart.Value.TimeOfDay, lunchEnd.Value.TimeOfDay),
                    (DisplayMode)display.SelectedIndex,
                    ((MonitorOption)monitor.SelectedItem).Device, transparency.Value, progress.Checked,
                    notify.Checked, SelectedTheme, pinInside.Checked, salary.Text, autoUpdates.Checked, progressColor,
                    showOvertime.Checked, WorkDayMask(dayBoxes), skipHolidays.Checked, payday.SelectedIndex);
            };
            Controls.AddRange(new Control[] { tabs, feedback, cancel, save });
            AcceptButton = save;
            CancelButton = cancel;
        }

        private static int WorkDayMask(CheckBox[] boxes)
        {
            int mask = 0;
            for (int i = 0; i < boxes.Length; i++)
                if (boxes[i].Checked) mask |= 1 << i;
            return mask;
        }

        private void OpenHolidayFile()
        {
            OpenUserFile(HolidayCalendar.DefaultPath, HolidayCalendar.EnsureUserFile, "휴일 파일을 열 수 없습니다.");
        }

        private void OpenReminderFile()
        {
            OpenUserFile(ReminderList.DefaultPath, ReminderList.EnsureUserFile, "알림 파일을 열 수 없습니다.");
        }

        private void OpenUserFile(string path, Action<string> ensure, string failure)
        {
            try
            {
                ensure(path);
                using (System.Diagnostics.Process.Start("notepad.exe", "\"" + path + "\"")) { }
            }
            catch (IOException) { ShowError(failure); }
            catch (UnauthorizedAccessException) { ShowError(failure); }
            catch (System.ComponentModel.Win32Exception) { ShowError(failure); }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            // Form.Dispose leaves an assigned Font alone.
            if (disposing) formFont.Dispose();
        }

        private static TabPage NewPage(string name, string text)
        {
            return new TabPage { Name = name, Text = text, UseVisualStyleBackColor = false, BackColor = SystemColors.Window };
        }

        private void Preview()
        {
            if (PreviewAppearance != null)
                PreviewAppearance(SelectedTransparency, ShowProgress, SelectedTheme, SelectedProgressColor);
        }

        private void SetProgressColor(Color color)
        {
            progressColor = BarColor.Opaque(color);
            RefreshProgressColor();
            Preview();
        }

        private void RefreshProgressColor()
        {
            swatch.BackColor = progressColor.IsEmpty ? ClockPalette.For(SelectedTheme).Normal : progressColor;
            pickColor.Enabled = progress.Checked;
            resetColor.Enabled = progress.Checked && !progressColor.IsEmpty;
        }

        private Color? ShowColorDialog(Color current)
        {
            using (var picker = new ColorDialog
            {
                Color = current, AnyColor = true, FullOpen = true, CustomColors = ClockPalette.AccentSwatches()
            })
            {
                return picker.ShowDialog(this) == DialogResult.OK ? picker.Color : (Color?)null;
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // Small screens at high scaling: scroll rather than push Save off the screen.
            Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
            if (Height <= area.Height) return;
            AutoScroll = true;
            Width += SystemInformation.VerticalScrollBarWidth;
            Height = area.Height;
            CenterToScreen();
        }

        private static DateTimePicker LunchPicker(string name, string label, TimeSpan time, int x)
        {
            return new DateTimePicker
            {
                Name = name, AccessibleName = label, Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm",
                ShowUpDown = true, Location = new Point(x, 96), Size = new Size(68, 26), Value = DateTime.Today.Add(time)
            };
        }

        private static DateTimePicker TimePicker(Control page, TimeSpan time, string name, int y)
        {
            page.Controls.Add(new Label { Text = name, AutoSize = true, Location = new Point(12, y + 4) });
            return new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom,
                CustomFormat = "HH:mm",
                ShowUpDown = true,
                AccessibleName = name,
                Location = new Point(138, y),
                Size = new Size(150, 26),
                Value = DateTime.Today.Add(time)
            };
        }

        private static ComboBox AddDropDown(Control page, string name, string label, int y)
        {
            page.Controls.Add(new Label { Text = label, AutoSize = true, Location = new Point(12, y + 4) });
            var control = new ComboBox
            {
                Name = name, AccessibleName = label, DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(138, y), Size = new Size(150, 26)
            };
            page.Controls.Add(control);
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

        private void Save(TimeSpan start, TimeSpan end, LunchBreak lunch, DisplayMode display, string monitor,
            int translucency, bool showProgress, bool notifyAtEnd, ClockTheme selectedTheme, bool pinInside,
            string salaryText, bool autoUpdates, Color selectedProgressColor, bool overtime,
            int workDays, bool skipHolidays, int payday)
        {
            try
            {
                if (workDays == 0) throw new ArgumentException("근무 요일을 하나 이상 고르세요.");
                if (display == DisplayMode.Payday && payday == 0)
                    throw new ArgumentException("월급날 D-day를 표시하려면 월급날을 고르세요.");
                long salary = 0;
                if (!string.IsNullOrWhiteSpace(salaryText) && !PayEstimate.TryParseSalary(salaryText, out salary))
                    throw new ArgumentException("세전 연봉을 원 단위 숫자로 입력하세요.");
                if (display == DisplayMode.Earnings && salary == 0)
                    throw new ArgumentException("오늘 급여를 표시하려면 세전 연봉을 입력하세요.");
                StoreSettings(new ClockSettings(new Schedule(start, end, lunch), display, monitor,
                    translucency, showProgress, notifyAtEnd, selectedTheme, pinInside, salary, autoUpdates,
                    selectedProgressColor, overtime, workDays, skipHolidays, payday, todayOverride));
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
