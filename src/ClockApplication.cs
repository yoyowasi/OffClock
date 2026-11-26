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
    internal sealed class ClockApplication : ApplicationContext
    {
        private readonly SettingsFile store;
        private readonly Dictionary<string, ClockStrip> strips = new Dictionary<string, ClockStrip>();
        private readonly float dpi;
        private readonly TaskbarDock dock = new TaskbarDock();
        private readonly Timer animation = new Timer { Interval = 32 };
        private readonly Timer placement = new Timer { Interval = 400 };
        private readonly MilestoneReminder reminder = new MilestoneReminder(DateTime.Now);
        private readonly Icon icon;
        private readonly ContextMenuStrip menu = new ContextMenuStrip();
        private readonly NotifyIcon tray;
        private readonly ContextMenuStrip stripMenu = new ContextMenuStrip();
        private readonly ToolTip hoverTip = new ToolTip { ShowAlways = true };
        private readonly ToolStripMenuItem displayMenu = new ToolStripMenuItem("표시 형식");
        private readonly ToolStripMenuItem themeMenu = new ToolStripMenuItem("테마");
        private readonly ToolStripMenuItem stripVisibility = new ToolStripMenuItem("카운트다운 숨기기");
        private readonly List<ToolStripMenuItem> todayMenus = new List<ToolStripMenuItem>();
        private ClockSettings settings;
        private HolidayCalendar holidays = HolidayCalendar.Builtin;
        private DateTime holidayStamp;
        private ReminderList customReminders = ReminderList.Empty;
        private DateTime reminderStamp;
        private readonly Dictionary<string, DateTime> firedReminders = new Dictionary<string, DateTime>();
        private DateTime nextHolidayCheck;
        private Milestone pendingMilestone;
        private DateTime pendingUntil;
        private TodayHoursDialog todayDialog;
        private ClockSettingsDialog editor;
        private UpdateDialog installedNotes;
        private FinishNotice notice;
        private bool paused;
        private bool refreshOnTick;
        private bool resumePending;
        private DateTime nextUpdateCheck;
        private Task<string> automaticUpdate;
        private Func<UpdateDialog> pendingNotice;

        public ClockApplication(SettingsFile store, bool restartingAfterUpdate, bool showReleaseNotes,
            bool updateFailed = false)
        {
            this.store = store;
            if (showReleaseNotes) pendingNotice = () => new UpdateDialog(true);
            else if (updateFailed)
            {
                UpdateFailure failure = UpdateFailure.Load(UpdateFailure.DefaultPath);
                if (failure != null) pendingNotice = () => new UpdateDialog(failure);
            }
            settings = store.Load();
            nextUpdateCheck = restartingAfterUpdate ? DateTime.UtcNow.AddHours(6) : DateTime.MinValue;
            using (Graphics screen = Graphics.FromHwnd(IntPtr.Zero))
                dpi = screen.DpiX / 96f;

            ReloadUserFiles(true);
            try { icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (ArgumentException) { }
            catch (IOException) { }
            menu.Items.Add("퇴근시계 설정…", null, delegate { EditSettings(); });
            var visibility = new ToolStripMenuItem("카운트다운 숨기기");
            visibility.Click += delegate { TogglePaused(); };
            menu.Items.Add(visibility);
            menu.Items.Add(BuildTodayMenu());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("종료", null, delegate { ExitThread(); });
            BuildStripMenu();
            menu.Opening += delegate
            {
                visibility.Text = VisibilityText();
                RefreshTodayMenus();
            };
            tray = new NotifyIcon { Icon = icon ?? SystemIcons.Application, ContextMenuStrip = menu };
            tray.MouseDoubleClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) EditSettings();
            };
            UpdateTrayText();
            tray.Visible = true;

            animation.Tick += delegate { UpdateCountdown(); };
            placement.Tick += delegate
            {
                if (pendingNotice != null)
                {
                    installedNotes = pendingNotice();
                    pendingNotice = null;
                    installedNotes.FormClosed += delegate { installedNotes = null; };
                    installedNotes.Show();
                }
                // Backstop: close the widget menu if a click landed outside it and the menu stayed open.
                if (stripMenu.Visible && Control.MouseButtons != MouseButtons.None && !IsOverMenu(stripMenu))
                    stripMenu.Close();
                bool resumed = resumePending;
                resumePending = false;
                refreshOnTick = !refreshOnTick;
                if ((refreshOnTick || resumed) && !paused) dock.RefreshInlineSpace();
                UpdatePlacement();
                if (resumed && !paused)
                {
                    UpdateCountdown();
                    foreach (ClockStrip strip in strips.Values)
                        if (strip.Visible) strip.Invalidate();
                }
                DateTime clockNow = DateTime.Now;
                if (clockNow >= nextHolidayCheck)
                {
                    nextHolidayCheck = clockNow.AddMinutes(1);
                    ReloadUserFiles(false);
                }
                UpdateTrayText();
                CheckReminders(clockNow);
                CheckForUpdates();
            };
            dock.RefreshInlineSpace();
            UpdatePlacement();
            placement.Start();
        }

        private string VisibilityText()
        {
            return paused ? "카운트다운 표시하기" : "카운트다운 숨기기";
        }

        private void TogglePaused()
        {
            paused = !paused;
            UpdatePlacement();
        }

        // Menu items belong to one strip only, so the widget gets its own copy of the tray items.
        private void BuildStripMenu()
        {
            for (int i = 0; i < ClockSettingsDialog.DisplayLabels.Length; i++)
            {
                DisplayMode mode = (DisplayMode)i;
                displayMenu.DropDownItems.Add(ClockSettingsDialog.DisplayLabels[i], null,
                    delegate { ApplyFromMenu(settings.WithDisplay(mode)); });
            }
            for (int i = 0; i < ClockSettingsDialog.ThemeLabels.Length; i++)
            {
                ClockTheme choice = (ClockTheme)i;
                themeMenu.DropDownItems.Add(ClockSettingsDialog.ThemeLabels[i], null,
                    delegate { ApplyFromMenu(settings.WithTheme(choice)); });
            }
            stripVisibility.Click += delegate { TogglePaused(); };
            stripMenu.Items.Add(displayMenu);
            stripMenu.Items.Add(themeMenu);
            stripMenu.Items.Add(new ToolStripSeparator());
            stripMenu.Items.Add("퇴근시계 설정…", null, delegate { EditSettings(); });
            stripMenu.Items.Add(stripVisibility);
            stripMenu.Items.Add(BuildTodayMenu());
            stripMenu.Items.Add("종료", null, delegate { ExitThread(); });
            stripMenu.Opening += delegate { RefreshStripMenu(); };
        }

        private void RefreshStripMenu()
        {
            stripVisibility.Text = VisibilityText();
            // The dialog previews and saves display and theme itself: leave them alone while it is open.
            displayMenu.Enabled = themeMenu.Enabled = editor == null;
            for (int i = 0; i < displayMenu.DropDownItems.Count; i++)
            {
                var item = (ToolStripMenuItem)displayMenu.DropDownItems[i];
                item.Checked = i == (int)settings.Display;
                item.Enabled = ((DisplayMode)i != DisplayMode.Earnings || settings.AnnualSalary > 0) &&
                    ((DisplayMode)i != DisplayMode.Payday || settings.Payday > 0);
            }
            for (int i = 0; i < themeMenu.DropDownItems.Count; i++)
                ((ToolStripMenuItem)themeMenu.DropDownItems[i]).Checked = i == (int)settings.Theme;
            RefreshTodayMenus();
        }

        private ToolStripMenuItem BuildTodayMenu()
        {
            var root = new ToolStripMenuItem("오늘만 변경");
            root.DropDownItems.Add("오전 반차", null, delegate { ApplyHalfDay(true); });
            root.DropDownItems.Add("오후 반차", null, delegate { ApplyHalfDay(false); });
            root.DropDownItems.Add("오늘 쉬기", null,
                delegate { ApplyFromMenu(settings.WithOverride(DayOverride.Rest(WorkDateNow()))); });
            root.DropDownItems.Add("시간 직접 입력…", null, delegate { ApplyCustomHours(); });
            root.DropDownItems.Add(new ToolStripSeparator());
            root.DropDownItems.Add("원래대로", null, delegate { ApplyFromMenu(settings.WithOverride(null)); });
            todayMenus.Add(root);
            return root;
        }

        private void RefreshTodayMenus()
        {
            bool available = editor == null && todayDialog == null && settings.SupportsOverride;
            bool overridden = settings.ActiveOverride(DateTime.Now) != null;
            foreach (ToolStripMenuItem root in todayMenus)
            {
                root.Enabled = available;
                root.ToolTipText = settings.SupportsOverride ? "" : "야간 근무 시간에는 쓸 수 없습니다.";
                // The last item is "원래대로".
                root.DropDownItems[root.DropDownItems.Count - 1].Enabled = overridden;
            }
        }

        private DateTime WorkDateNow()
        {
            return settings.TargetDate(DateTime.Now);
        }

        private void ApplyHalfDay(bool morningOff)
        {
            try
            {
                Schedule half = morningOff ? settings.Schedule.HalfDayMorningOff() : settings.Schedule.HalfDayAfternoonOff();
                ApplyFromMenu(settings.WithOverride(DayOverride.Hours(WorkDateNow(), half.Start, half.End)));
            }
            catch (ArgumentException)
            {
                tray.ShowBalloonTip(3000, "퇴근시계", "반차 시간을 계산할 수 없습니다.", ToolTipIcon.Warning);
            }
        }

        private void ApplyCustomHours()
        {
            if (editor != null || todayDialog != null) return;
            Schedule current = settings.ScheduleAt(DateTime.Now);
            using (var dialog = new TodayHoursDialog(current.Start, current.End, icon))
            {
                todayDialog = dialog;
                try
                {
                    if (dialog.ShowDialog() != DialogResult.OK) return;
                    ApplyFromMenu(settings.WithOverride(DayOverride.Hours(WorkDateNow(), dialog.Start, dialog.End)));
                }
                finally
                {
                    todayDialog = null;
                }
            }
        }

        // Re-reads holidays.txt and reminders.txt when their last-write time changed (or when forced).
        // A stamp and its object are only replaced after a successful read, so a file locked by an editor or
        // a virus scanner keeps the previous list and is retried on the next check.
        private void ReloadUserFiles(bool force)
        {
            DateTime stamp;
            if (FileChanged(HolidayCalendar.DefaultPath, holidayStamp, force, out stamp))
            {
                HolidayCalendar loaded;
                if (HolidayCalendar.TryLoad(HolidayCalendar.DefaultPath, out loaded))
                {
                    holidays = loaded;
                    holidayStamp = stamp;
                }
            }
            if (FileChanged(ReminderList.DefaultPath, reminderStamp, force, out stamp))
            {
                ReminderList loaded;
                if (ReminderList.TryLoad(ReminderList.DefaultPath, out loaded))
                {
                    customReminders = loaded;
                    reminderStamp = stamp;
                }
            }
        }

        private static bool FileChanged(string path, DateTime knownStamp, bool force, out DateTime stamp)
        {
            stamp = DateTime.MinValue;
            try { stamp = File.GetLastWriteTimeUtc(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (SecurityException) { }
            catch (ArgumentException) { }
            return force || stamp != knownStamp;
        }

        private void CheckReminders(DateTime now)
        {
            CheckMilestones(now);
            CheckCustomReminders(now);
        }

        private void CheckMilestones(DateTime now)
        {
            if (!settings.NotifyAtEnd)
            {
                pendingMilestone = Milestone.None;
                return;
            }
            // Overrides last the whole day and never cross midnight, so the schedule in force now owns every
            // deadline due now. Always check so each reminder's last-check time advances, even on a day off.
            Milestone due = reminder.Check(settings.ScheduleAt(now), now);
            if (settings.CountdownAt(now, holidays).Target == CountdownTarget.DayOff)
            {
                pendingMilestone = Milestone.None;
                return;
            }
            if (due != Milestone.None)
            {
                pendingMilestone = due;
                pendingUntil = reminder.RelevantUntil;
            }
            if (pendingMilestone == Milestone.None) return;
            // Held back by a fullscreen app: try again on later ticks until it is no longer relevant.
            if (now > pendingUntil || NotifyMilestone(pendingMilestone)) pendingMilestone = Milestone.None;
        }

        // Due reminders are never remembered as pending: each tick recomputes what is due within the grace
        // period and skips what already fired, so a blocked or late reminder retries by itself.
        private void CheckCustomReminders(DateTime now)
        {
            foreach (string key in firedReminders.Where(p => p.Value + ReminderList.Grace < now)
                .Select(p => p.Key).ToList())
                firedReminders.Remove(key);
            if (customReminders.Count == 0) return;
            // A visible notice (a milestone shown this tick, or an earlier reminder) is not replaced: wait.
            if (notice != null) return;
            foreach (ReminderOccurrence due in customReminders.DueAt(settings, holidays, now))
            {
                if (firedReminders.ContainsKey(due.Key)) continue;
                // False means a fullscreen app blocks every notice: retry on a later tick.
                if (!ShowNotice(due.Reminder.Text, due.Reminder.Describe())) return;
                firedReminders[due.Key] = due.Due;
                return;
            }
        }

        // What a change of the effective schedule looks like to the reminders.
        private static string ScheduleKey(ClockSettings value, DateTime now)
        {
            DayOverride active = value.ActiveOverride(now);
            Schedule schedule = value.ScheduleAt(now);
            return (active != null && active.Off ? "off " : "on ") + Schedule.Format(schedule.Start) + " " +
                Schedule.Format(schedule.End) + " " + schedule.Lunch.Format();
        }

        private void ApplyFromMenu(ClockSettings updated)
        {
            if (editor != null) return;
            try
            {
                store.Save(updated);
            }
            catch (IOException) { ShowSaveFailure(); return; }
            catch (UnauthorizedAccessException) { ShowSaveFailure(); return; }
            catch (SecurityException) { ShowSaveFailure(); return; }
            DateTime now = DateTime.Now;
            bool scheduleChanged = ScheduleKey(settings, now) != ScheduleKey(updated, now);
            settings = updated;
            // Only a different effective schedule restarts the milestones; display and theme keep a held notice.
            if (scheduleChanged)
            {
                reminder.Reset(now);
                pendingMilestone = Milestone.None;
            }
            UpdateTrayText();
            UpdateAppearance(settings.Transparency, settings.ShowProgress, settings.Theme, settings.ProgressColor);
            UpdatePlacement();
            UpdateCountdown();
        }

        private void ShowSaveFailure()
        {
            tray.ShowBalloonTip(3000, "퇴근시계", "설정 파일을 저장할 수 없습니다.", ToolTipIcon.Warning);
        }

        private void ShowStripMenu(ClockStrip strip, Point location)
        {
            if (!strip.IsHandleCreated) return;
            // A menu opened from a no-activate window only closes on an outside click if our process
            // owns the foreground, as NotifyIcon arranges for the tray menu.
            SetForegroundWindow(strip.Handle);
            RefreshStripMenu();
            stripMenu.Show(strip, location);
        }

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr window);

        private static bool IsOverMenu(ToolStripDropDown dropDown)
        {
            Point cursor = Cursor.Position;
            if (dropDown.Bounds.Contains(cursor)) return true;
            foreach (ToolStripItem item in dropDown.Items)
            {
                var parent = item as ToolStripDropDownItem;
                if (parent != null && parent.DropDown.Visible && parent.DropDown.Bounds.Contains(cursor)) return true;
            }
            return false;
        }

        private void UpdateTooltip()
        {
            DateTime now = DateTime.Now;
            Schedule schedule = settings.ScheduleAt(now);
            LunchBreak lunch = schedule.Lunch;
            Countdown countdown = settings.CountdownAt(now, holidays);
            string text = "출근 " + Schedule.Format(schedule.Start) + " – 퇴근 " + Schedule.Format(schedule.End) +
                (lunch.Enabled ? "  ·  점심 " + Schedule.Format(lunch.Start) + "–" + Schedule.Format(lunch.End) : "") +
                (settings.ActiveOverride(now) != null ? " (오늘만)" : "");
            if (countdown.Target == CountdownTarget.DayOff)
                text += "\n쉬는 날 (" + countdown.DayOffName + ")";
            else
            {
                text += "\n진행 " + (int)Math.Floor(countdown.Progress * 100) + "%";
                if (settings.AnnualSalary > 0)
                    text += "\n오늘 급여 " + settings.EarnedAt(now, countdown, holidays)
                        .ToString("N0", CultureInfo.InvariantCulture) + "원";
            }
            if (settings.Payday > 0)
            {
                int days = settings.PaydayDaysLeft(now, holidays).GetValueOrDefault();
                DateTime payday = PaydayRule.Next(settings.Payday, now.Date, holidays);
                text += days == 0 ? "\n오늘은 월급날" : "\n월급날 D-" + days + " (" + payday.Month + "월 " + payday.Day + "일)";
            }
            ReminderOccurrence next = customReminders.NextToday(settings, holidays, now);
            if (next != null) text += "\n다음 알림 " + next.Due.ToString("HH:mm", CultureInfo.InvariantCulture) +
                " " + next.Reminder.Text;
            for (int ahead = 1; ahead <= 366; ahead++)
            {
                DateTime day = now.Date.AddDays(ahead);
                string holiday = holidays.NameOn(day);
                if (holiday == null) continue;
                text += "\n다음 공휴일 " + day.Month + "월 " + day.Day + "일 " + holiday + " (D-" + ahead + ")";
                break;
            }
            foreach (ClockStrip strip in strips.Values)
                if (hoverTip.GetToolTip(strip) != text) hoverTip.SetToolTip(strip, text);
        }

        // NotifyIcon.Text throws above 63 characters: drop the lunch part first, then cut.
        private string TrayText(DateTime now)
        {
            Schedule schedule = settings.Schedule;
            LunchBreak lunch = schedule.Lunch;
            DayOverride active = settings.ActiveOverride(now);
            string head = "퇴근시계  ·  " + Schedule.Format(schedule.Start) + "–" + Schedule.Format(schedule.End);
            string lunchPart = lunch.Enabled ? "  ·  점심 " + Schedule.Format(lunch.Start) + "–" + Schedule.Format(lunch.End) : "";
            string today = active == null ? "" : active.Off ? "  ·  오늘 쉼" :
                "  ·  오늘 " + Schedule.Format(active.Start) + "–" + Schedule.Format(active.End);
            string text = head + lunchPart + today;
            if (text.Length > 63) text = head + today;
            return text.Length > 63 ? text.Substring(0, 63) : text;
        }

        private void UpdateTrayText()
        {
            string text = TrayText(DateTime.Now);
            if (tray.Text != text) tray.Text = text;
        }

        private void UpdateCountdown(ClockStrip only = null)
        {
            DateTime now = DateTime.Now;
            Countdown countdown = settings.CountdownAt(now, holidays);
            long earned = settings.Display == DisplayMode.Earnings ? settings.EarnedAt(now, countdown, holidays) : 0;
            int? paydayDays = settings.Display == DisplayMode.Payday ? settings.PaydayDaysLeft(now, holidays) : null;
            foreach (ClockStrip strip in strips.Values)
                if (only == null ? strip.Visible : ReferenceEquals(strip, only))
                    strip.Display(countdown, settings.Display, earned, paydayDays);
        }

        private void CheckForUpdates()
        {
            if (automaticUpdate != null)
            {
                if (!automaticUpdate.IsCompleted) return;
                if (automaticUpdate.IsFaulted)
                {
                    System.Diagnostics.Trace.WriteLine(automaticUpdate.Exception.GetBaseException().Message);
                    automaticUpdate = null;
                    return;
                }

                string stage = automaticUpdate.Result;
                if (stage != null && settings.AutoUpdate &&
                    (editor != null || todayDialog != null || notice != null || installedNotes != null)) return;
                automaticUpdate = null;
                if (stage == null) return;
                if (!settings.AutoUpdate)
                {
                    ReleaseUpdate.DeleteStage(stage);
                    return;
                }

                try
                {
                    ReleaseUpdate.StartInstaller(stage);
                    placement.Stop();
                    ExitThread();
                }
                catch (Exception ex)
                {
                    ReleaseUpdate.DeleteStage(stage);
                    System.Diagnostics.Trace.WriteLine(ex.Message);
                }
                return;
            }

            DateTime now = DateTime.UtcNow;
            if (!settings.AutoUpdate || editor != null || todayDialog != null || now < nextUpdateCheck) return;
            nextUpdateCheck = now.AddHours(6);
            automaticUpdate = Task.Run(delegate
            {
                ReleaseUpdate release = ReleaseUpdate.Check();
                if (release == null) return null;
                UpdateFailure failure = UpdateFailure.Load(UpdateFailure.DefaultPath);
                return failure != null && failure.IsFor(release.Version) ? null : release.Download();
            });
        }

        private void ShowUpdateDialog()
        {
            using (var dialog = new UpdateDialog())
            {
                if (dialog.ShowDialog(editor) != DialogResult.OK) return;
            }
            editor.Close();
            ExitThread();
        }

        private void UpdatePlacement()
        {
            if (paused)
            {
                animation.Stop();
                foreach (ClockStrip strip in strips.Values) strip.Hide();
                return;
            }

            var attached = new HashSet<string>();
            bool anyVisible = false;
            foreach (Taskbar taskbar in TaskbarDock.FindForDisplay(settings.Monitor))
            {
                string monitor = taskbar.Screen.DeviceName;
                if (!attached.Add(monitor)) continue;

                ClockStrip strip;
                if (!strips.TryGetValue(monitor, out strip))
                {
                    strip = new ClockStrip(dpi);
                    if (editor == null)
                        strip.SetAppearance(settings.Transparency, settings.ShowProgress, settings.Theme, settings.ProgressColor);
                    else
                        strip.SetAppearance(editor.SelectedTransparency, editor.ShowProgress, editor.SelectedTheme,
                            editor.SelectedProgressColor);
                    strip.MouseClick += delegate(object sender, MouseEventArgs e)
                    {
                        if (e.Button == MouseButtons.Left) strip.BeginInvoke((Action)EditSettings);
                        else if (e.Button == MouseButtons.Right) ShowStripMenu(strip, e.Location);
                    };
                    strip.MouseEnter += delegate { UpdateTooltip(); };
                    strip.TaskbarCreated += RestoreTrayIcon;
                    strip.PowerResumed += delegate { resumePending = true; };
                    strips.Add(monitor, strip);
                }

                Rectangle target = dock.Place(taskbar, new Size(strip.Scale(235), strip.Scale(34)),
                    strip.Scale(7), settings.PinInside);
                if (target.IsEmpty)
                {
                    strip.Hide();
                    continue;
                }

                strip.ShowOvertime = settings.ShowOvertime;
                if (strip.Bounds != target) strip.Bounds = target;
                if (!strip.Visible)
                {
                    UpdateCountdown(strip);
                    strip.Show();
                }
                if (target.IntersectsWith(taskbar.Bounds))
                    Native.KeepAboveTaskbar(strip.Handle, taskbar.Window);
                anyVisible = true;
            }

            foreach (string monitor in strips.Keys.Where(name => !attached.Contains(name)).ToArray())
            {
                strips[monitor].Dispose();
                strips.Remove(monitor);
            }
            animation.Enabled = anyVisible;
            UpdateTooltip();
        }

        private void RestoreTrayIcon(object sender, EventArgs e)
        {
            if (!ReferenceEquals(sender, strips.Values.FirstOrDefault(item => item.IsHandleCreated))) return;
            tray.Visible = false;
            tray.Visible = true;
            UpdatePlacement();
        }

        private void EditSettings()
        {
            if (editor != null)
            {
                editor.Activate();
                return;
            }
            if (todayDialog != null)
            {
                todayDialog.Activate();
                return;
            }

            var startup = new LoginStartup(Environment.GetFolderPath(Environment.SpecialFolder.Startup,
                Environment.SpecialFolderOption.DoNotVerify),
                Application.ExecutablePath);
            using (var dialog = new ClockSettingsDialog(settings, icon, startup.Registered))
            {
                editor = dialog;
                try
                {
                    dialog.PreviewAppearance = UpdateAppearance;
                    dialog.CheckForUpdate = ShowUpdateDialog;
                    dialog.StoreSettings = updated =>
                    {
                        startup.Save(dialog.StartAtLogin, delegate { store.Save(updated); });
                        if (updated.NotifyAtEnd != settings.NotifyAtEnd || updated.WorkDays != settings.WorkDays ||
                            updated.SkipHolidays != settings.SkipHolidays ||
                            updated.Schedule.Start != settings.Schedule.Start || updated.Schedule.End != settings.Schedule.End ||
                            updated.Schedule.Lunch.Format() != settings.Schedule.Lunch.Format())
                            reminder.Reset(DateTime.Now);
                        if (updated.AutoUpdate && !settings.AutoUpdate) nextUpdateCheck = DateTime.MinValue;
                        settings = updated;
                        UpdateTrayText();
                        UpdatePlacement();
                        UpdateCountdown();
                    };
                    dialog.ShowDialog();
                }
                finally
                {
                    editor = null;
                    // The dialog may have opened the holiday or reminder file for editing.
                    ReloadUserFiles(true);
                    UpdateTrayText();
                    UpdateAppearance(settings.Transparency, settings.ShowProgress, settings.Theme, settings.ProgressColor);
                    UpdateCountdown();
                }
            }
        }

        private void UpdateAppearance(int transparency, bool showProgress, ClockTheme theme, Color progressColor)
        {
            foreach (ClockStrip strip in strips.Values)
                strip.SetAppearance(transparency, showProgress, theme, progressColor);
        }

        private bool NotifyMilestone(Milestone milestone)
        {
            string lunchEnd = Schedule.Format(settings.ScheduleAt(DateTime.Now).Lunch.End);
            if (milestone == Milestone.Leave) return ShowNotice("퇴근 시간이에요", "설정한 퇴근 시간이 되었습니다.");
            if (milestone == Milestone.LunchStarts) return ShowNotice("점심시간이에요", lunchEnd + "까지 맛있게 드세요.");
            if (milestone == Milestone.LunchEnding) return ShowNotice("점심 5분 남았어요", lunchEnd + "에 점심시간이 끝납니다.");
            return true;
        }

        private bool ShowNotice(string title, string body)
        {
            Taskbar taskbar = TaskbarDock.FindForDisplay(settings.Monitor).First();
            if (Native.IsFullscreen(taskbar.Screen.Bounds)) return false;
            if (notice != null) notice.Close();
            notice = new FinishNotice(dpi, settings.Theme, taskbar.Screen.WorkingArea, title, body);
            ClockStrip strip;
            if (strips.TryGetValue(taskbar.Screen.DeviceName, out strip) && strip.Visible &&
                notice.Bounds.IntersectsWith(strip.Bounds))
            {
                notice.Top = strip.Top - notice.Height - strip.Scale(7);
            }
            notice.FormClosed += delegate { notice = null; };
            notice.Show();
            return true;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                animation.Dispose();
                placement.Dispose();
                if (automaticUpdate != null && automaticUpdate.Status == TaskStatus.RanToCompletion &&
                    automaticUpdate.Result != null)
                    ReleaseUpdate.DeleteStage(automaticUpdate.Result);
                tray.Dispose();
                menu.Dispose();
                stripMenu.Dispose();
                hoverTip.Dispose();
                if (installedNotes != null) installedNotes.Dispose();
                if (notice != null) notice.Dispose();
                foreach (ClockStrip strip in strips.Values) strip.Dispose();
                if (icon != null) icon.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
