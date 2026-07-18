using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;
using TaskbarTimerWidget;
using WidgetForm = TaskbarTimerWidget.TaskbarTimerWidget;

internal static class WidgetSmokeTests
{
    private static Exception failure;
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using (WidgetForm widget = new WidgetForm())
        {
            try
            {
                widget.Show();
                Application.DoEvents();
                int taskbarCreated = NativeMethods.RegisterWindowMessage("TaskbarCreated");
                NativeMethods.SendMessage(widget.Handle, taskbarCreated, IntPtr.Zero, IntPtr.Zero);
                Application.DoEvents();
                Thread.Sleep(150);
                Application.DoEvents();
                VerifyWidget(widget);
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }

        if (failure != null)
        {
            Console.Error.WriteLine("FAIL: Live widget smoke test\n" + failure);
            Environment.Exit(1);
        }

        Console.Error.WriteLine("PASS: Live widget idle visibility, tool-window configuration, docking, and TaskbarCreated recovery");
    }

    private static void VerifyWidget(WidgetForm widget)
    {
        True(widget.Visible, "Widget must remain visible while idle.");
        True(widget.TopMost, "Widget must remain topmost.");
        True(!widget.ShowInTaskbar, "Widget must not create a taskbar application button.");
        True(widget.FormBorderStyle == FormBorderStyle.None, "Widget must remain borderless.");

        TaskbarDockingService service = new TaskbarDockingService(widget.Handle);
        IList<TaskbarInfo> taskbars = service.GetTaskbars();
        True(taskbars.Count > 0, "Explorer taskbar discovery returned no taskbars.");

        foreach (TaskbarInfo taskbar in taskbars)
        {
            True(service.Reposition(widget.Size, taskbar.MonitorDeviceName, 0, 0, true), "SetWindowPos failed for " + taskbar.MonitorDeviceName);
            True(IsDockedTo(widget.Bounds, taskbar), "Widget was not docked to " + taskbar.MonitorDeviceName + ": " + widget.Bounds);
            foreach (Rectangle occupied in TaskbarDockingService.GetForeignTaskbarChildren(taskbar))
                True(!widget.Bounds.IntersectsWith(occupied), "Widget overlaps a third-party taskbar child at " + occupied + ".");
            True(
                NativeMethods.GetWindow(widget.Handle, NativeMethods.GwOwner) == taskbar.Handle,
                "Widget must be owned by its target taskbar so taskbar clicks cannot cover it.");
        }
    }

    private static bool IsDockedTo(Rectangle widgetBounds, TaskbarInfo taskbar)
    {
        if (taskbar.Side == TaskbarDockSide.Top || taskbar.Side == TaskbarDockSide.Bottom)
            return Rectangle.Intersect(widgetBounds, taskbar.Bounds).Height == widgetBounds.Height;

        int gap = taskbar.Side == TaskbarDockSide.Left
            ? Math.Abs(widgetBounds.Left - taskbar.Bounds.Right)
            : Math.Abs(taskbar.Bounds.Left - widgetBounds.Right);
        return gap <= 8 && taskbar.MonitorBounds.Contains(widgetBounds);
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
