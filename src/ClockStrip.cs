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
    internal abstract class TaskbarWindow : Form
    {
        protected TaskbarWindow()
        {
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get
            {
                const int toolWindow = 0x80, noActivate = 0x08000000;
                CreateParams result = base.CreateParams;
                result.ExStyle |= toolWindow | noActivate;
                return result;
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int mouseActivate = 0x0021, noActivate = 3;
            if (m.Msg == mouseActivate)
            {
                m.Result = new IntPtr(noActivate);
                return;
            }
            base.WndProc(ref m);
        }
    }

    internal sealed class FinishNotice : TaskbarWindow
    {
        private readonly ClockPalette palette;
        private readonly float dpi;
        private readonly Timer dismissal = new Timer { Interval = 4000 };
        private readonly Font titleFont = new Font("Malgun Gothic", 16, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font bodyFont = new Font("Malgun Gothic", 12, FontStyle.Regular, GraphicsUnit.Pixel);
        private readonly string body;
        // Layout in design pixels (before the dpi scale): the text column and the title's 1 or 2 lines.
        private const int TextLeft = 21;
        private const int TextWidth = 224;
        private readonly int titleLine;
        private readonly int bodyLine;
        private readonly int titleLines;

        public FinishNotice(float dpi, ClockTheme theme, Rectangle workArea, string title = "퇴근 시간이에요",
            string body = "설정한 퇴근 시간이 되었습니다.")
        {
            this.dpi = dpi;
            this.body = body;
            palette = ClockPalette.For(theme);
            Text = title;
            BackColor = palette.Background;
            titleLine = LineHeight(titleFont);
            bodyLine = LineHeight(bodyFont);
            using (Graphics measure = Graphics.FromHwnd(IntPtr.Zero))
            using (StringFormat format = TextFormat(true))
            {
                measure.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                SizeF size = measure.MeasureString(title, titleFont, new SizeF(TextWidth, 1000), format);
                titleLines = size.Height > titleLine * 1.5f ? 2 : 1;
            }
            int extra = (titleLines - 1) * titleLine;
            ClientSize = new Size((int)Math.Round(248 * dpi), (int)Math.Round((65 + extra) * dpi));
            int margin = (int)Math.Round(10 * dpi);
            Location = new Point(workArea.Right - Width - margin, workArea.Bottom - Height - margin);
            dismissal.Tick += delegate { Close(); };
            Shown += delegate { dismissal.Start(); };
            MouseClick += delegate { Close(); };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics graphics = e.Graphics;
            graphics.ScaleTransform(dpi, dpi);
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            int extra = (titleLines - 1) * titleLine;
            using (var border = new Pen(palette.Border))
            using (var accent = new SolidBrush(palette.Normal))
            using (var title = new SolidBrush(palette.Digits))
            using (var muted = new SolidBrush(palette.Muted))
            using (StringFormat titleFormat = TextFormat(true))
            using (StringFormat bodyFormat = TextFormat(false))
            {
                graphics.DrawRectangle(border, 0.5f, 0.5f, 247, 64 + extra);
                graphics.FillRectangle(accent, 9, 13, 3, 40 + extra);
                graphics.DrawString(Text, titleFont, title, new RectangleF(TextLeft, 8, TextWidth, titleLines * titleLine),
                    titleFormat);
                graphics.DrawString(body, bodyFont, muted, new RectangleF(TextLeft + 1, 34 + extra, TextWidth - 1, bodyLine),
                    bodyFormat);
            }
        }

        // Whole lines only, an ellipsis where the text is cut; the body also stays on one line.
        private static StringFormat TextFormat(bool wrap)
        {
            var format = new StringFormat(StringFormatFlags.LineLimit) { Trimming = StringTrimming.EllipsisCharacter };
            if (!wrap) format.FormatFlags |= StringFormatFlags.NoWrap;
            return format;
        }

        private static int LineHeight(Font font)
        {
            FontFamily family = font.FontFamily;
            return (int)Math.Ceiling((double)family.GetLineSpacing(font.Style) / family.GetEmHeight(font.Style) * font.Size);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                dismissal.Dispose();
                titleFont.Dispose();
                bodyFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class ClockStrip : TaskbarWindow
    {
        private readonly Font labelFont = new Font("Malgun Gothic", 12, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font timeFont = new Font("Consolas", 20, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font msFont = new Font("Consolas", 14, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font totalFont = new Font("Consolas", 17, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly Font compactFont = new Font("Consolas", 11.5f, FontStyle.Bold, GraphicsUnit.Pixel);
        private readonly float dpi;
        private readonly int taskbarCreated = Native.RegisterWindowMessage("TaskbarCreated");
        private Countdown countdown;
        private DisplayMode display;
        private long earnings;
        private bool showProgress = true;
        private ClockTheme theme;
        private Color progressColor;
        private bool showOvertime;
        // Days until payday (0: today), null: none.
        private int? paydayDays;
        public event EventHandler TaskbarCreated;
        public event EventHandler PowerResumed;

        public ClockStrip(float dpi)
        {
            this.dpi = dpi;
            ClientSize = new Size(Scale(235), Scale(34));
            Text = "퇴근시계 카운트다운";
            Cursor = Cursors.Hand;
            BackColor = TransparencyKey = Color.Magenta;
        }

        public int Scale(int value) { return (int)Math.Round(value * dpi); }
        protected override void WndProc(ref Message m)
        {
            const int powerBroadcast = 0x0218;
            const int resumeAutomatic = 0x12, resumeFromSuspend = 0x07;
            if (m.Msg == powerBroadcast && PowerResumed != null &&
                (m.WParam.ToInt64() == resumeAutomatic || m.WParam.ToInt64() == resumeFromSuspend))
            {
                PowerResumed(this, EventArgs.Empty);
            }
            base.WndProc(ref m);
            if (m.Msg == taskbarCreated && TaskbarCreated != null)
                TaskbarCreated(this, EventArgs.Empty);
        }

        public void Display(Countdown value, DisplayMode mode, long earned = 0, int? paydayDaysLeft = null)
        {
            if (value.Remaining == countdown.Remaining && value.Progress == countdown.Progress &&
                value.Target == countdown.Target && value.Overtime == countdown.Overtime &&
                value.DayOffName == countdown.DayOffName && mode == display && earned == earnings &&
                paydayDaysLeft == paydayDays)
                return;
            countdown = value;
            display = mode;
            earnings = earned;
            paydayDays = paydayDaysLeft;
            Invalidate();
        }

        public bool ShowOvertime
        {
            get { return showOvertime; }
            set
            {
                if (showOvertime == value) return;
                showOvertime = value;
                Invalidate();
            }
        }

        private bool OvertimeShown
        {
            get
            {
                return showOvertime && display != DisplayMode.Earnings && display != DisplayMode.Payday &&
                    countdown.Target != CountdownTarget.DayOff && countdown.Finished &&
                    countdown.Overtime >= TimeSpan.FromSeconds(1);
            }
        }

        public void SetAppearance(int transparency, bool showProgress, ClockTheme theme = ClockTheme.Dark,
            Color progressColor = default(Color))
        {
            Opacity = 1 - transparency / 100.0;
            progressColor = BarColor.Opaque(progressColor);
            if (this.showProgress == showProgress && this.theme == theme && this.progressColor == progressColor) return;
            this.showProgress = showProgress;
            this.theme = theme;
            this.progressColor = progressColor;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            float scale = Math.Min(ClientSize.Width / 235f, ClientSize.Height / 34f);
            g.ScaleTransform(scale, scale);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

            ClockPalette palette = ClockPalette.For(theme);
            CountdownStage stage = countdown.Stage;
            Color accent = palette.Accent(stage);
            using (var panel = RoundedRectangle(0.5f, 0.5f, 234, 33, 9))
            using (var panelFill = new SolidBrush(palette.Background))
            using (var outline = new Pen(palette.Border))
            using (var accentBrush = new SolidBrush(accent))
            using (var accentPen = new Pen(accent, 1.35f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            using (var dim = new SolidBrush(palette.Muted))
            using (var bright = new SolidBrush(palette.Digits))
            {
                // Keep chroma-key edges crisp, including on light taskbars.
                g.SmoothingMode = SmoothingMode.None;
                g.FillPath(panelFill, panel);
                g.DrawPath(outline, panel);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                if (palette.Mascot != Mascot.None)
                    MascotPainter.Draw(g, palette, stage);
                else
                {
                    g.DrawEllipse(accentPen, 9, 7, 15, 15);
                    if (countdown.Finished)
                        g.DrawLines(accentPen, new[] { new PointF(13, 14.5f), new PointF(16, 17.5f), new PointF(20.5f, 12) });
                    else
                        g.DrawLines(accentPen, new[] { new PointF(16.5f, 10.5f), new PointF(16.5f, 14.5f), new PointF(19.5f, 16) });
                }

                string label = display == DisplayMode.Payday ? (paydayDays == 0 ? "월급날" : "월급날까지") :
                    countdown.Target == CountdownTarget.DayOff ? countdown.DayOffName :
                    display == DisplayMode.Earnings ? "오늘 급여" :
                    countdown.Target == CountdownTarget.Lunch ? "점심까지" :
                    countdown.Target == CountdownTarget.LunchEnd ? "점심 끝까지" :
                    OvertimeShown ? "초과 근무" : (countdown.Finished ? "퇴근 완료" : "퇴근까지");
                DrawLabel(g, label, dim);
                DrawTime(g, stage == CountdownStage.Urgent ? accentBrush : bright, accentBrush);
                if (showProgress && countdown.Target != CountdownTarget.DayOff)
                {
                    using (var track = new SolidBrush(palette.Track))
                    using (var bar = new SolidBrush(progressColor.IsEmpty ? accent : progressColor))
                    {
                        g.FillRectangle(track, 10, 27, 215, 2);
                        g.FillRectangle(bar, 10, 27, (float)(215 * countdown.Progress), 2);
                    }
                }
            }
        }

        // The clock digits start at x 91: a label too long for the slot is narrowed instead of overlapping them.
        private void DrawLabel(Graphics g, string label, Brush brush)
        {
            const float left = 31, limit = 88, padding = 2;
            StringFormat format = StringFormat.GenericTypographic;
            float width = g.MeasureString(label, labelFont, PointF.Empty, format).Width;
            if (left + padding + width <= limit)
            {
                g.DrawString(label, labelFont, brush, left, 7.5f);
                return;
            }
            GraphicsState state = g.Save();
            g.TranslateTransform(left, 7.5f);
            g.ScaleTransform((limit - left) / width, 1);
            g.DrawString(label, labelFont, brush, 0, 0, format);
            g.Restore(state);
        }

        private void DrawTime(Graphics g, Brush digits, Brush accent)
        {
            var format = StringFormat.GenericTypographic;
            bool overtime = OvertimeShown;
            Countdown shown = overtime ? countdown.AsOvertime() : countdown;
            bool dayOff = countdown.Target == CountdownTarget.DayOff && display != DisplayMode.Payday;
            if (display == DisplayMode.Clock && !dayOff)
            {
                string seconds = (overtime ? "+" : "") + shown.Seconds;
                float msWidth = g.MeasureString(shown.Milliseconds, msFont, PointF.Empty, format).Width;
                Font clockFont = timeFont;
                float y = 2.5f;
                // The "+" adds a digit: shrink so the milliseconds still end inside the panel (x 226).
                if (overtime && 91 + g.MeasureString(seconds, clockFont, PointF.Empty, format).Width + 3 + msWidth > 226)
                {
                    clockFont = totalFont;
                    y = 4.5f;
                }
                float width = g.MeasureString(seconds, clockFont, PointF.Empty, format).Width;
                g.DrawString(seconds, clockFont, digits, 91, y, format);
                g.DrawString(shown.Milliseconds, msFont, accent, overtime ? 91 + width + 3 : 182, 8, format);
                return;
            }

            string unit;
            string total;
            Brush totalBrush = digits;
            if (dayOff)
            {
                unit = null;
                total = "쉬는 날";
            }
            else if (display == DisplayMode.Payday)
            {
                if (paydayDays == 0)
                {
                    unit = null;
                    total = "오늘!";
                    totalBrush = accent;
                }
                else
                {
                    unit = "일";
                    total = !paydayDays.HasValue ? "-" : paydayDays.Value.ToString("N0", CultureInfo.InvariantCulture);
                }
            }
            else if (display == DisplayMode.Earnings)
            {
                unit = "원";
                total = earnings.ToString("N0", CultureInfo.InvariantCulture);
            }
            else
            {
                total = shown.FormatTotal(display, out unit);
                if (overtime) total = "+" + total;
            }
            float unitX = unit == null ? 224 : 224 - g.MeasureString(unit, labelFont, PointF.Empty, format).Width;
            float digitsRight = unit == null ? 224 : unitX - 5;
            Font font = totalFont;
            float digitsX = digitsRight - g.MeasureString(total, font, PointF.Empty, format).Width;
            if (digitsX < 84)
            {
                font = compactFont;
                digitsX = digitsRight - g.MeasureString(total, font, PointF.Empty, format).Width;
            }
            g.DrawString(total, font, totalBrush, digitsX, font == totalFont ? 4.5f : 8, format);
            if (unit != null) g.DrawString(unit, labelFont, accent, unitX, 8, format);
        }

        private static GraphicsPath RoundedRectangle(float x, float y, float width, float height, float radius)
        {
            var path = new GraphicsPath();
            float d = radius * 2;
            path.AddArc(x, y, d, d, 180, 90);
            path.AddArc(x + width - d, y, d, d, 270, 90);
            path.AddArc(x + width - d, y + height - d, d, d, 0, 90);
            path.AddArc(x, y + height - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                labelFont.Dispose();
                timeFont.Dispose();
                msFont.Dispose();
                totalFont.Dispose();
                compactFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
