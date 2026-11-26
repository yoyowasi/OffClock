using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Automation;
using System.Windows.Forms;

namespace OffClock
{
    internal sealed class TaskbarDock
    {
        private Dictionary<IntPtr, InlineSpace> spaces = new Dictionary<IntPtr, InlineSpace>();
        private int queryInProgress;

        public static IEnumerable<Taskbar> FindForDisplay(string device)
        {
            List<Taskbar> taskbars = FindTaskbars();
            if (device.Length == 0) return taskbars;

            Screen screen = Screen.AllScreens.FirstOrDefault(candidate => candidate.DeviceName == device) ??
                Screen.PrimaryScreen;
            Taskbar selected = taskbars.FirstOrDefault(taskbar => taskbar.Screen.DeviceName == screen.DeviceName);
            return new[] { selected ?? Taskbar.Fallback(screen) };
        }

        public static List<Taskbar> FindTaskbars()
        {
            bool autoHide = Native.TaskbarAutoHides();
            var bars = new List<Taskbar>();
            IntPtr primary = Native.FindWindow("Shell_TrayWnd");
            Rectangle primaryBounds;
            uint primaryEdge;
            if (Native.ReadPrimaryTaskbar(out primaryBounds, out primaryEdge))
                bars.Add(new Taskbar(primary, primaryBounds, primaryEdge, Screen.FromRectangle(primaryBounds), autoHide));
            else
            {
                Rectangle bounds = Native.Bounds(primary);
                Screen screen = Screen.PrimaryScreen;
                bars.Add(bounds.IsEmpty ? Taskbar.Fallback(screen) :
                    new Taskbar(primary, bounds, InferEdge(bounds, screen.Bounds), screen, autoHide));
            }

            foreach (IntPtr window in Native.SecondaryTaskbars())
            {
                Rectangle bounds = Native.Bounds(window);
                if (bounds.Width <= 0 || bounds.Height <= 0) continue;
                Screen screen = Screen.FromRectangle(bounds);
                if (screen.Primary) continue;
                uint edge = InferEdge(bounds, screen.Bounds);
                bars.Add(new Taskbar(window, bounds, edge, screen, autoHide));
            }
            return bars;
        }

        internal static uint InferEdge(Rectangle bar, Rectangle screen)
        {
            if (bar.Width >= bar.Height)
                return Math.Abs(bar.Top - screen.Top) <= Math.Abs(bar.Bottom - screen.Bottom) ? 1u : 3u;
            return Math.Abs(bar.Left - screen.Left) <= Math.Abs(bar.Right - screen.Right) ? 0u : 2u;
        }

        public Rectangle Place(Taskbar taskbar, Size size, int margin, bool pinInside)
        {
            Rectangle bar = taskbar.Bounds;
            Screen screen = taskbar.Screen;
            if (Native.IsFullscreen(screen.Bounds)) return Rectangle.Empty;
            if (bar.IsEmpty)
            {
                Rectangle work = screen.WorkingArea;
                return new Rectangle(work.Right - size.Width - margin, work.Bottom - size.Height - margin,
                    size.Width, size.Height);
            }

            uint edge = taskbar.Edge;
            if (taskbar.AutoHide && !taskbar.IsShown()) return Rectangle.Empty;
            bool horizontal = edge == 1 || edge == 3;
            if (horizontal && pinInside && bar.Height < size.Height + 2)
            {
                int height = Math.Max(1, bar.Height - 2);
                size = new Size(Math.Max(1, size.Width * height / size.Height), height);
            }
            if (horizontal && bar.Height >= size.Height + 2)
            {
                InlineSpace verified;
                if (Volatile.Read(ref spaces).TryGetValue(taskbar.Window, out verified) && verified.Bar == bar &&
                    unchecked(Environment.TickCount - verified.MeasuredAt) < 2000 &&
                    verified.Right - verified.Left >= size.Width + margin * 2)
                {
                    return new Rectangle(verified.Right - margin - size.Width,
                        bar.Top + (bar.Height - size.Height) / 2, size.Width, size.Height);
                }
            }

            if (horizontal && pinInside)
            {
                Rectangle tray = Native.Bounds(Native.FindChild(taskbar.Window, "TrayNotifyWnd"));
                if (tray.IsEmpty) tray = Native.Bounds(Native.FindChild(taskbar.Window, "ClockButton"));
                Rectangle list = Native.Bounds(Native.FindChild(taskbar.Window, "MSTaskListWClass"));
                return PinnedPlacement(bar, tray, list, size, margin);
            }
            return Beside(bar, screen.WorkingArea, edge, size, margin);
        }

        internal static Rectangle PinnedPlacement(Rectangle bar, Rectangle tray, Rectangle list, Size size, int margin)
        {
            // Explorer versions expose different children. Leave room for the system tray even without a provider.
            int right = bar.Right - Math.Min(bar.Width / 3, size.Width);
            if (bar.IntersectsWith(list)) right = Math.Min(bar.Right, list.Right);
            if (bar.IntersectsWith(tray) && tray.Left > bar.Left)
                right = Math.Min(bar.IntersectsWith(list) ? right : bar.Right, tray.Left);
            int width = Math.Min(size.Width, bar.Width);
            int x = Math.Max(bar.Left, right - margin - width);
            return new Rectangle(x, bar.Top + (bar.Height - size.Height) / 2, width, size.Height);
        }

        internal static Rectangle Beside(Rectangle bar, Rectangle work, uint edge, Size size, int margin)
        {
            int x = work.Right - size.Width - margin;
            int y = work.Bottom - size.Height - margin;
            switch (edge)
            {
                case 0: x = Math.Max(bar.Right, work.Left) + margin; break;
                case 1: y = Math.Max(bar.Bottom, work.Top) + margin; break;
                case 2: x = Math.Min(bar.Left, work.Right) - size.Width - margin; break;
                default: y = Math.Min(bar.Top, work.Bottom) - size.Height - margin; break;
            }
            return new Rectangle(x, y, size.Width, size.Height);
        }

        public void RefreshInlineSpace()
        {
            if (Interlocked.Exchange(ref queryInProgress, 1) != 0) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                var measured = new Dictionary<IntPtr, InlineSpace>();
                try
                {
                    foreach (Taskbar taskbar in FindTaskbars())
                    {
                        if (taskbar.Window == IntPtr.Zero || (taskbar.Edge != 1 && taskbar.Edge != 3)) continue;
                        if (taskbar.AutoHide && !taskbar.IsShown()) continue;
                        try
                        {
                            InlineSpace available = FindInlineSpace(taskbar);
                            if (available != null) measured[taskbar.Window] = available;
                        }
                        // Routine while Explorer redraws or restarts: retried on the next tick, not worth a log line.
                        catch (ElementNotAvailableException) { }
                        catch (InvalidOperationException) { }
                        catch (COMException) { }
                        catch (Exception ex) { System.Diagnostics.Trace.WriteLine("FindInlineSpace: " + ex.Message); }
                    }
                }
                // This runs on a pool thread, where an escaping exception would end the process.
                catch (Exception ex) { System.Diagnostics.Trace.WriteLine("RefreshInlineSpace: " + ex.Message); }
                finally
                {
                    Volatile.Write(ref spaces, measured);
                    Volatile.Write(ref queryInProgress, 0);
                }
            });
        }

        private static InlineSpace FindInlineSpace(Taskbar taskbar)
        {
            IntPtr tray = Native.FindChild(taskbar.Window, "TrayNotifyWnd");
            IntPtr list = Native.FindChild(taskbar.Window, "MSTaskListWClass");
            if (list == IntPtr.Zero) return null;

            Rectangle bar = taskbar.Bounds;
            Rectangle trayBounds = Native.Bounds(tray);
            Rectangle listBounds = Native.Bounds(list);
            if (listBounds.IsEmpty || !bar.IntersectsWith(listBounds)) return null;

            int left = Math.Max(listBounds.Left, bar.Left);
            int right = Math.Min(listBounds.Right, bar.Right);
            if (!trayBounds.IsEmpty) right = Math.Min(right, trayBounds.Left);
            var root = AutomationElement.FromHandle(list);
            var visible = new PropertyCondition(AutomationElement.IsOffscreenProperty, false);
            var request = new CacheRequest();
            request.Add(AutomationElement.BoundingRectangleProperty);
            var children = default(AutomationElementCollection);
            using (request.Activate())
                children = root.FindAll(TreeScope.Children, visible);

            // An empty UIA tree may indicate an unavailable Explorer provider.
            if (children.Count == 0) return null;
            foreach (AutomationElement child in children)
            {
                System.Windows.Rect bounds = child.Cached.BoundingRectangle;
                if (bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) continue;
                if (bounds.Right <= listBounds.Left || bounds.Left >= right ||
                    bounds.Bottom <= listBounds.Top || bounds.Top >= listBounds.Bottom) continue;
                left = Math.Max(left, (int)Math.Ceiling(bounds.Right));
            }
            return new InlineSpace(bar, left, right);
        }

        private sealed class InlineSpace
        {
            public readonly Rectangle Bar;
            public readonly int Left;
            public readonly int Right;
            public readonly int MeasuredAt;

            public InlineSpace(Rectangle bar, int left, int right)
            {
                Bar = bar;
                Left = left;
                Right = right;
                MeasuredAt = Environment.TickCount;
            }
        }
    }

    internal sealed class Taskbar
    {
        public readonly IntPtr Window;
        public readonly Rectangle Bounds;
        public readonly uint Edge;
        public readonly Screen Screen;
        public readonly bool AutoHide;

        public Taskbar(IntPtr window, Rectangle bounds, uint edge, Screen screen, bool autoHide)
        {
            Window = window;
            Bounds = bounds;
            Edge = edge;
            Screen = screen;
            AutoHide = autoHide;
        }

        public static Taskbar Fallback(Screen screen)
        {
            return new Taskbar(IntPtr.Zero, Rectangle.Empty, 3, screen, false);
        }

        public bool IsShown()
        {
            Rectangle current = Native.Bounds(Window);
            if (current.IsEmpty) return false;
            Rectangle visible = Rectangle.Intersect(current, Screen.Bounds);
            bool horizontal = Edge == 1 || Edge == 3;
            int visibleSize = horizontal ? visible.Height : visible.Width;
            return visibleSize >= 12;
        }
    }

    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Rect
        {
            public int Left, Top, Right, Bottom;
            public Rectangle DrawingBounds { get { return Rectangle.FromLTRB(Left, Top, Right, Bottom); } }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct AppBarData
        {
            public uint Size;
            public IntPtr Window;
            public uint Callback;
            public uint Edge;
            public Rect Rect;
            public IntPtr Parameter;
        }

        private delegate bool WindowCallback(IntPtr hwnd, IntPtr parameter);
        [DllImport("shell32.dll")]
        private static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowW")]
        private static extern IntPtr FindWindowByClass(string className, IntPtr title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowExW")]
        private static extern IntPtr FindWindowAfter(IntPtr parent, IntPtr previous, string className, IntPtr title);
        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hwnd, WindowCallback callback, IntPtr parameter);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hwnd, System.Text.StringBuilder text, int capacity);
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")]
        private static extern bool GetClientRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")]
        private static extern bool ClientToScreen(IntPtr hwnd, ref Point point);
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hwnd, uint command);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter,
            int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();
        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")]
        internal static extern int RegisterWindowMessage(string message);

        public static IntPtr FindWindow(string className) { return FindWindowByClass(className, IntPtr.Zero); }

        public static void KeepAboveTaskbar(IntPtr widget, IntPtr taskbar)
        {
            if (taskbar == IntPtr.Zero) return;
            const uint next = 2;
            const uint noSize = 0x0001, noMove = 0x0002, noActivate = 0x0010;

            IntPtr window = GetWindow(taskbar, next);
            for (int index = 0; index < 64 && window != IntPtr.Zero; index++, window = GetWindow(window, next))
            {
                if (window != widget) continue;
                // Explorer can reorder topmost windows on resume. Explicitly restore this widget's z-order.
                SetWindowPos(widget, new IntPtr(-1),
                    0, 0, 0, 0, noSize | noMove | noActivate);
                return;
            }
        }

        public static IEnumerable<IntPtr> SecondaryTaskbars()
        {
            IntPtr window = IntPtr.Zero;
            while ((window = FindWindowAfter(IntPtr.Zero, window, "Shell_SecondaryTrayWnd", IntPtr.Zero)) != IntPtr.Zero)
                yield return window;
        }

        public static IntPtr FindChild(IntPtr parent, string className)
        {
            if (parent == IntPtr.Zero) return IntPtr.Zero;
            IntPtr found = IntPtr.Zero;
            var name = new System.Text.StringBuilder(96);
            EnumChildWindows(parent, delegate(IntPtr hwnd, IntPtr unused)
            {
                if (!IsWindowVisible(hwnd)) return true;
                GetClassName(hwnd, name, name.Capacity);
                if (!string.Equals(name.ToString(), className, StringComparison.Ordinal)) return true;
                found = hwnd;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        public static Rectangle Bounds(IntPtr window)
        {
            Rect rect;
            return window != IntPtr.Zero && GetWindowRect(window, out rect) ? rect.DrawingBounds : Rectangle.Empty;
        }

        public static bool ReadPrimaryTaskbar(out Rectangle bar, out uint edge)
        {
            const uint getPosition = 5;
            var data = new AppBarData { Size = (uint)Marshal.SizeOf(typeof(AppBarData)) };
            if (SHAppBarMessage(getPosition, ref data) == UIntPtr.Zero)
            {
                bar = Rectangle.Empty;
                edge = 3;
                return false;
            }
            edge = data.Edge;
            bar = data.Rect.DrawingBounds;
            return bar.Width > 0 && bar.Height > 0;
        }

        public static bool TaskbarAutoHides()
        {
            const uint getState = 4;
            var data = new AppBarData { Size = (uint)Marshal.SizeOf(typeof(AppBarData)) };
            return (SHAppBarMessage(getState, ref data).ToUInt32() & 1) != 0;
        }

        public static bool IsFullscreen(Rectangle screen)
        {
            IntPtr window = GetForegroundWindow();
            if (window == IntPtr.Zero || window == GetShellWindow() || window == FindWindow("Shell_TrayWnd"))
                return false;
            // With the desktop focused the foreground window is WorkerW or Progman, which covers the monitor.
            var className = new System.Text.StringBuilder(96);
            GetClassName(window, className, className.Capacity);
            string windowClass = className.ToString();
            if (windowClass == "WorkerW" || windowClass == "Progman") return false;
            Rect client;
            var origin = Point.Empty;
            if (!GetClientRect(window, out client) || !ClientToScreen(window, ref origin)) return false;
            Rectangle bounds = client.DrawingBounds;
            bounds.Offset(origin);
            return bounds.Contains(screen);
        }
    }
}
