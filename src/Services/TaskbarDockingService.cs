using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace TaskbarTimerWidget
{
    internal enum TaskbarDockSide
    {
        Bottom,
        Top,
        Left,
        Right
    }

    internal sealed class TaskbarInfo
    {
        public IntPtr Handle;
        public Rectangle Bounds;
        public Rectangle MonitorBounds;
        public string MonitorDeviceName;
        public TaskbarDockSide Side;
        public bool IsPrimary;
    }

    internal static class TaskbarPositionCalculator
    {
        private const int DefaultDpi = 96;

        public static Size ScaleForDpi(Size logicalSize, int dpi)
        {
            int effectiveDpi = dpi > 0 ? dpi : DefaultDpi;
            return new Size(
                Math.Max(1, (logicalSize.Width * effectiveDpi + DefaultDpi / 2) / DefaultDpi),
                Math.Max(1, (logicalSize.Height * effectiveDpi + DefaultDpi / 2) / DefaultDpi));
        }

        public static TaskbarDockSide DetectSide(Rectangle taskbar, Rectangle monitor)
        {
            int leftDistance = Math.Abs(taskbar.Left - monitor.Left);
            int topDistance = Math.Abs(taskbar.Top - monitor.Top);
            int rightDistance = Math.Abs(taskbar.Right - monitor.Right);
            int bottomDistance = Math.Abs(taskbar.Bottom - monitor.Bottom);

            if (taskbar.Width >= taskbar.Height) return topDistance <= bottomDistance ? TaskbarDockSide.Top : TaskbarDockSide.Bottom;
            return leftDistance <= rightDistance ? TaskbarDockSide.Left : TaskbarDockSide.Right;
        }

        public static Rectangle Calculate(
            TaskbarInfo taskbar,
            Size widgetSize,
            Rectangle? trayBounds,
            int horizontalOffset,
            int verticalOffset)
        {
            return Calculate(taskbar, widgetSize, trayBounds, null, horizontalOffset, verticalOffset);
        }

        public static Rectangle Calculate(
            TaskbarInfo taskbar,
            Size widgetSize,
            Rectangle? trayBounds,
            IList<Rectangle> occupiedBounds,
            int horizontalOffset,
            int verticalOffset)
        {
            const int edgePadding = 8;
            const int itemGap = 10;
            int x;
            int y;
            int width = widgetSize.Width;
            int height = widgetSize.Height;

            if (taskbar.Side == TaskbarDockSide.Top || taskbar.Side == TaskbarDockSide.Bottom)
            {
                if (taskbar.Bounds.Height > 0)
                {
                    int maxHeight = Math.Max(22, taskbar.Bounds.Height - 8);
                    height = Math.Min(widgetSize.Height, maxHeight);
                }

                x = trayBounds.HasValue
                    ? trayBounds.Value.Left - width - 10
                    : taskbar.Bounds.Right - width - 190;
                x += horizontalOffset;
                x = Clamp(x, taskbar.Bounds.Left + edgePadding, taskbar.Bounds.Right - width - edgePadding);
                y = taskbar.Bounds.Top + Math.Max(0, (taskbar.Bounds.Height - height) / 2) + verticalOffset;
                y = Clamp(y, taskbar.Bounds.Top, taskbar.Bounds.Bottom - height);

                Rectangle target = new Rectangle(x, y, width, height);
                if (occupiedBounds != null)
                {
                    // Move left around other taskbar widgets.
                    for (int pass = 0; pass < occupiedBounds.Count; pass++)
                    {
                        bool moved = false;
                        foreach (Rectangle occupied in occupiedBounds)
                        {
                            if (!target.IntersectsWith(occupied)) continue;
                            int movedX = Clamp(
                                occupied.Left - width - itemGap,
                                taskbar.Bounds.Left + edgePadding,
                                taskbar.Bounds.Right - width - edgePadding);
                            if (movedX == target.X) break;
                            target.X = movedX;
                            moved = true;
                            break;
                        }

                        if (!moved) break;
                    }

                    x = target.X;
                }
            }
            else
            {
                if (taskbar.Bounds.Width > 0)
                {
                    int maxWidth = Math.Max(40, taskbar.Bounds.Width - 8);
                    width = Math.Min(widgetSize.Width, maxWidth);
                }

                x = taskbar.Side == TaskbarDockSide.Left
                    ? taskbar.Bounds.Right + edgePadding
                    : taskbar.Bounds.Left - width - edgePadding;
                x += horizontalOffset;
                y = taskbar.Bounds.Bottom - height - edgePadding + verticalOffset;
                x = Clamp(x, taskbar.MonitorBounds.Left, taskbar.MonitorBounds.Right - width);
                y = Clamp(y, taskbar.MonitorBounds.Top, taskbar.MonitorBounds.Bottom - height);
            }

            return new Rectangle(x, y, width, height);
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            if (maximum < minimum) return minimum;
            return Math.Max(minimum, Math.Min(value, maximum));
        }
    }

    internal sealed class TaskbarDockingService
    {
        private readonly IntPtr widgetHandle;
        private Rectangle lastAppliedBounds;

        public TaskbarDockingService(IntPtr widgetHandle)
        {
            this.widgetHandle = widgetHandle;
            CurrentSide = TaskbarDockSide.Bottom;
        }

        public bool IsApplying { get; private set; }
        public TaskbarDockSide CurrentSide { get; private set; }
        public string CurrentMonitor { get; private set; }
        public IntPtr CurrentTaskbarHandle { get; private set; }

        public IList<TaskbarInfo> GetTaskbars()
        {
            List<TaskbarInfo> result = new List<TaskbarInfo>();
            NativeMethods.EnumWindows(delegate(IntPtr window, IntPtr parameter)
            {
                StringBuilder className = new StringBuilder(64);
                if (NativeMethods.GetClassName(window, className, className.Capacity) == 0) return true;
                string value = className.ToString();
                if (value != "Shell_TrayWnd" && value != "Shell_SecondaryTrayWnd") return true;

                NativeMethods.NativeRect taskbarRect;
                if (!NativeMethods.GetWindowRect(window, out taskbarRect)) return true;
                IntPtr monitorHandle = NativeMethods.MonitorFromWindow(window, NativeMethods.MonitorDefaultToNearest);
                NativeMethods.MonitorInfoEx monitorInfo = new NativeMethods.MonitorInfoEx();
                monitorInfo.Size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.MonitorInfoEx));
                if (!NativeMethods.GetMonitorInfo(monitorHandle, ref monitorInfo)) return true;

                Rectangle bounds = ToRectangle(taskbarRect);
                Rectangle monitorBounds = ToRectangle(monitorInfo.Monitor);
                result.Add(new TaskbarInfo
                {
                    Handle = window,
                    Bounds = bounds,
                    MonitorBounds = monitorBounds,
                    MonitorDeviceName = monitorInfo.DeviceName,
                    Side = TaskbarPositionCalculator.DetectSide(bounds, monitorBounds),
                    IsPrimary = value == "Shell_TrayWnd"
                });
                return true;
            }, IntPtr.Zero);
            return result;
        }

        public bool Reposition(Size widgetSize, string targetMonitor, int horizontalOffset, int verticalOffset, bool force)
        {
            IList<TaskbarInfo> taskbars = GetTaskbars();
            TaskbarInfo taskbar = SelectTaskbar(taskbars, targetMonitor);
            if (taskbar == null) return PositionFallback(widgetSize, targetMonitor, force);

            // For non-forced repositions, don't switch to a different monitor.
            // Only allow monitor changes when explicitly forced (user action, display change, etc.).
            if (!force && !string.IsNullOrEmpty(CurrentMonitor)
                && !string.Equals(taskbar.MonitorDeviceName, CurrentMonitor, StringComparison.OrdinalIgnoreCase))
            {
                // Try to stay on the current monitor instead of switching.
                TaskbarInfo currentTaskbar = null;
                foreach (TaskbarInfo tb in taskbars)
                {
                    if (string.Equals(tb.MonitorDeviceName, CurrentMonitor, StringComparison.OrdinalIgnoreCase))
                    {
                        currentTaskbar = tb;
                        break;
                    }
                }
                if (currentTaskbar != null) taskbar = currentTaskbar;
            }

            Rectangle? trayBounds = TryGetTrayBounds(taskbar.Handle);
            IList<Rectangle> occupiedBounds = GetForeignTaskbarChildren(taskbar);
            Rectangle target = TaskbarPositionCalculator.Calculate(taskbar, widgetSize, trayBounds, occupiedBounds, horizontalOffset, verticalOffset);
            CurrentSide = taskbar.Side;
            CurrentMonitor = taskbar.MonitorDeviceName;
            CurrentTaskbarHandle = taskbar.Handle;

            // Ownership keeps the widget above the taskbar without embedding it in Explorer.
            bool attached = EnsureTaskbarOwner(taskbar.Handle);
            return attached && Apply(target, force);
        }

        private bool PositionFallback(Size widgetSize, string targetMonitor, bool force)
        {
            Screen screen = FindScreen(targetMonitor);
            Rectangle area = screen.WorkingArea;
            Rectangle target = new Rectangle(area.Right - widgetSize.Width - 12, area.Bottom - widgetSize.Height - 8, widgetSize.Width, widgetSize.Height);
            CurrentSide = TaskbarDockSide.Bottom;
            CurrentMonitor = screen.DeviceName;
            CurrentTaskbarHandle = IntPtr.Zero;
            return EnsureTaskbarOwner(IntPtr.Zero) && Apply(target, force);
        }

        private bool EnsureTaskbarOwner(IntPtr taskbarHandle)
        {
            IntPtr currentOwner = NativeMethods.GetWindow(widgetHandle, NativeMethods.GwOwner);
            if (currentOwner == taskbarHandle) return true;
            return NativeMethods.SetWindowOwner(widgetHandle, taskbarHandle);
        }

        private bool Apply(Rectangle target, bool force)
        {
            NativeMethods.NativeRect current;
            bool changed = !NativeMethods.GetWindowRect(widgetHandle, out current)
                || current.Left != target.Left
                || current.Top != target.Top
                || current.Width != target.Width
                || current.Height != target.Height;
            if (!force && !changed && lastAppliedBounds == target) return true;

            IsApplying = true;
            try
            {
                bool result = NativeMethods.SetWindowPos(
                    widgetHandle,
                    NativeMethods.HwndTopmost,
                    target.Left,
                    target.Top,
                    target.Width,
                    target.Height,
                    NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow | NativeMethods.SwpNoOwnerZOrder);
                if (result) lastAppliedBounds = target;
                return result;
            }
            finally
            {
                IsApplying = false;
            }
        }

        internal static TaskbarInfo SelectTaskbar(IList<TaskbarInfo> taskbars, string targetMonitor)
        {
            if (!string.IsNullOrEmpty(targetMonitor) && !string.Equals(targetMonitor, "Primary", StringComparison.OrdinalIgnoreCase))
            {
                foreach (TaskbarInfo taskbar in taskbars)
                {
                    if (string.Equals(taskbar.MonitorDeviceName, targetMonitor, StringComparison.OrdinalIgnoreCase)) return taskbar;
                }
            }

            // Prefer the Shell_TrayWnd primary taskbar.
            foreach (TaskbarInfo taskbar in taskbars)
            {
                if (taskbar.IsPrimary) return taskbar;
            }

            // Fall back to the Windows-configured primary display.
            string primaryDevice = Screen.PrimaryScreen.DeviceName;
            foreach (TaskbarInfo taskbar in taskbars)
            {
                if (string.Equals(taskbar.MonitorDeviceName, primaryDevice, StringComparison.OrdinalIgnoreCase)) return taskbar;
            }

            return taskbars.Count > 0 ? taskbars[0] : null;
        }

        private static Rectangle? TryGetTrayBounds(IntPtr taskbar)
        {
            IntPtr tray = NativeMethods.FindWindowEx(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
            NativeMethods.NativeRect rectangle;
            return tray != IntPtr.Zero && NativeMethods.GetWindowRect(tray, out rectangle)
                ? (Rectangle?)ToRectangle(rectangle)
                : null;
        }

        internal static IList<Rectangle> GetForeignTaskbarChildren(TaskbarInfo taskbar)
        {
            List<Rectangle> occupiedBounds = new List<Rectangle>();
            uint explorerProcessId;
            NativeMethods.GetWindowThreadProcessId(taskbar.Handle, out explorerProcessId);

            NativeMethods.EnumChildWindows(taskbar.Handle, delegate(IntPtr child, IntPtr parameter)
            {
                if (!NativeMethods.IsWindowVisible(child)) return true;

                uint childProcessId;
                NativeMethods.GetWindowThreadProcessId(child, out childProcessId);
                if (childProcessId == explorerProcessId) return true;

                NativeMethods.NativeRect childRect;
                if (!NativeMethods.GetWindowRect(child, out childRect)) return true;
                Rectangle intersection = Rectangle.Intersect(taskbar.Bounds, ToRectangle(childRect));
                if (intersection.Width > 0 && intersection.Height > 0)
                    occupiedBounds.Add(intersection);
                return true;
            }, IntPtr.Zero);

            return occupiedBounds;
        }

        private static Screen FindScreen(string deviceName)
        {
            if (!string.IsNullOrEmpty(deviceName))
            {
                foreach (Screen screen in Screen.AllScreens)
                {
                    if (string.Equals(screen.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase)) return screen;
                }
            }

            return Screen.PrimaryScreen;
        }

        private static Rectangle ToRectangle(NativeMethods.NativeRect rectangle)
        {
            return Rectangle.FromLTRB(rectangle.Left, rectangle.Top, rectangle.Right, rectangle.Bottom);
        }
    }
}
