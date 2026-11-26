using System;
using System.Drawing;
using System.Windows.Forms;

namespace OffClock
{
    // Asks for today's start and end time only; the saved schedule is not touched.
    internal sealed class TodayHoursDialog : Form
    {
        private readonly Font formFont;
        private readonly DateTimePicker start;
        private readonly DateTimePicker end;
        private readonly Label feedback;
        public TimeSpan Start { get { return start.Value.TimeOfDay; } }
        public TimeSpan End { get { return end.Value.TimeOfDay; } }

        public TodayHoursDialog(TimeSpan startTime, TimeSpan endTime, Icon icon)
        {
            Text = "오늘만 근무 시간 변경";
            if (icon != null) Icon = icon;
            AutoScaleDimensions = new SizeF(96, 96);
            AutoScaleMode = AutoScaleMode.Dpi;
            ClientSize = new Size(260, 170);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            formFont = new Font("Malgun Gothic", 9);
            Font = formFont;
            BackColor = SystemColors.Window;
            ForeColor = SystemColors.WindowText;

            start = Picker("출근 시간", startTime, 16, 0);
            end = Picker("퇴근 시간", endTime, 56, 1);
            feedback = new Label
            {
                Name = "Feedback", ForeColor = ClockPalette.For(ClockTheme.Light).Urgent, AutoSize = true,
                MaximumSize = new Size(236, 0), Location = new Point(12, 94)
            };
            var ok = new Button { Text = "확인", Location = new Point(97, 130), Size = new Size(72, 28), TabIndex = 2 };
            var cancel = new Button
            {
                Text = "취소", Location = new Point(176, 130), Size = new Size(72, 28), TabIndex = 3,
                DialogResult = DialogResult.Cancel
            };
            ok.Click += delegate
            {
                try
                {
                    // Today-only hours must stay within the day (see ClockSettings.SupportsOverride).
                    if (End <= Start) throw new ArgumentException("퇴근 시간은 같은 날 출근 시간보다 늦어야 합니다.");
                    DialogResult = DialogResult.OK;
                }
                catch (ArgumentException e) { feedback.Text = e.Message; }
            };
            Controls.AddRange(new Control[] { feedback, ok, cancel });
            AcceptButton = ok;
            CancelButton = cancel;
        }

        private DateTimePicker Picker(string name, TimeSpan time, int y, int tabIndex)
        {
            Controls.Add(new Label { Text = name, AutoSize = true, Location = new Point(12, y + 4) });
            var picker = new DateTimePicker
            {
                Format = DateTimePickerFormat.Custom, CustomFormat = "HH:mm", ShowUpDown = true,
                AccessibleName = name, Location = new Point(110, y), Size = new Size(138, 26), TabIndex = tabIndex,
                Value = DateTime.Today.Add(time)
            };
            Controls.Add(picker);
            return picker;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing) formFont.Dispose();
        }
    }
}
