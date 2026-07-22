using System;
using System.Collections.Generic;
using System.Drawing;
using TaskbarTimerWidget;

internal static class WidgetLogicTests
{
    private static int passed;

    private static void Main()
    {
        Run("Preset values and order", PresetValuesAndOrder);
        Run("Preset selection remains idle", PresetSelectionRemainsIdle);
        Run("Widget size follows current DPI", WidgetSizeFollowsCurrentDpi);
        Run("Start pause resume complete", StartPauseResumeComplete);
        Run("Running timer cannot be restarted", RunningTimerCannotBeRestarted);
        Run("Pausing an expired timer completes it", PausingExpiredTimerCompletesIt);
        Run("Invalid duration is rejected", InvalidDurationIsRejected);
        Run("Reset restores duration", ResetRestoresDuration);
        Run("Bottom taskbar positioning", BottomTaskbarPositioning);
        Run("Top taskbar positioning", TopTaskbarPositioning);
        Run("Position clamps to monitor", PositionClampsToMonitor);
        Run("Target monitor and primary fallback", TargetMonitorAndPrimaryFallback);
        Run("DPI-scaled positioning", DpiScaledPositioning);
        Run("Widget height constrained to taskbar", WidgetHeightConstrainedToTaskbar);
        Run("Avoids third-party taskbar widgets", AvoidsThirdPartyTaskbarWidgets);
        Run("TaskbarCreated registration", TaskbarCreatedRegistration);
        Console.WriteLine("All {0} tests passed.", passed);
    }

    private static void WidgetSizeFollowsCurrentDpi()
    {
        Equal(new Size(132, 32), TaskbarPositionCalculator.ScaleForDpi(new Size(132, 32), 96));
        Equal(new Size(165, 40), TaskbarPositionCalculator.ScaleForDpi(new Size(132, 32), 120));
        Equal(new Size(198, 48), TaskbarPositionCalculator.ScaleForDpi(new Size(132, 32), 144));
        Equal(new Size(132, 32), TaskbarPositionCalculator.ScaleForDpi(new Size(132, 32), 0));
    }

    private static void PresetValuesAndOrder()
    {
        string[] labels = { "00:10:00", "00:20:00", "00:30:00", "01:00:00", "01:30:00", "03:00:00", "05:00:00" };
        double[] minutes = { 10, 20, 30, 60, 90, 180, 300 };
        IList<TimerPreset> presets = TimerPresets.All;
        Equal(7, presets.Count);
        for (int index = 0; index < presets.Count; index++)
        {
            Equal(labels[index], presets[index].Label);
            Equal(minutes[index], presets[index].Duration.TotalMinutes);
            Equal(index, presets[index].SortOrder);
        }
    }

    private static void PresetSelectionRemainsIdle()
    {
        TimerStateMachine timer = new TimerStateMachine(TimeSpan.FromMinutes(5));
        timer.SelectPreset(TimerPresets.Find("1h30m"));
        Equal(TimerState.Idle, timer.State);
        Equal(90.0, timer.Remaining.TotalMinutes);
    }

    private static void StartPauseResumeComplete()
    {
        DateTime start = new DateTime(2026, 1, 1, 12, 0, 0);
        TimerStateMachine timer = new TimerStateMachine(TimeSpan.FromSeconds(10));
        True(timer.Start(start));
        timer.Tick(start.AddSeconds(4));
        Equal(6.0, timer.Remaining.TotalSeconds);
        timer.Pause(start.AddSeconds(4));
        Equal(TimerState.Paused, timer.State);
        True(timer.Resume(start.AddSeconds(20)));
        True(timer.Tick(start.AddSeconds(26)));
        Equal(TimerState.Completed, timer.State);
    }

    private static void ResetRestoresDuration()
    {
        DateTime start = new DateTime(2026, 1, 1);
        TimerStateMachine timer = new TimerStateMachine(TimeSpan.FromMinutes(20));
        timer.Start(start);
        timer.Tick(start.AddMinutes(4));
        timer.Reset();
        Equal(TimerState.Idle, timer.State);
        Equal(20.0, timer.Remaining.TotalMinutes);
    }

    private static void RunningTimerCannotBeRestarted()
    {
        DateTime start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        TimerStateMachine timer = new TimerStateMachine(TimeSpan.FromSeconds(10));
        True(timer.Start(start));
        True(!timer.Start(start.AddSeconds(4)));
        True(timer.Tick(start.AddSeconds(10)));
        Equal(TimerState.Completed, timer.State);
    }

    private static void PausingExpiredTimerCompletesIt()
    {
        DateTime start = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        TimerStateMachine timer = new TimerStateMachine(TimeSpan.FromSeconds(2));
        True(timer.Start(start));
        timer.Pause(start.AddSeconds(3));
        Equal(TimerState.Completed, timer.State);
        Equal(TimeSpan.Zero, timer.Remaining);
    }

    private static void InvalidDurationIsRejected()
    {
        bool threw = false;
        try
        {
            new TimerStateMachine(TimeSpan.Zero);
        }
        catch (ArgumentOutOfRangeException)
        {
            threw = true;
        }

        True(threw);
    }

    private static void BottomTaskbarPositioning()
    {
        TaskbarInfo taskbar = NewTaskbar(new Rectangle(0, 1040, 1920, 40), new Rectangle(0, 0, 1920, 1080));
        Rectangle result = TaskbarPositionCalculator.Calculate(taskbar, new Size(132, 32), new Rectangle(1700, 1040, 220, 40), 0, 0);
        Equal(TaskbarDockSide.Bottom, taskbar.Side);
        Equal(1558, result.Left);
        Equal(1044, result.Top);
    }

    private static void TopTaskbarPositioning()
    {
        TaskbarInfo taskbar = NewTaskbar(new Rectangle(0, 0, 1920, 40), new Rectangle(0, 0, 1920, 1080));
        Rectangle result = TaskbarPositionCalculator.Calculate(taskbar, new Size(132, 32), null, 0, 0);
        Equal(TaskbarDockSide.Top, taskbar.Side);
        Equal(4, result.Top);
    }

    private static void PositionClampsToMonitor()
    {
        TaskbarInfo taskbar = NewTaskbar(new Rectangle(-1280, 984, 1280, 40), new Rectangle(-1280, 0, 1280, 1024));
        Rectangle result = TaskbarPositionCalculator.Calculate(taskbar, new Size(132, 32), null, 10000, 10000);
        True(result.Left >= -1280 && result.Right <= 0);
        True(result.Top >= 984 && result.Bottom <= 1024);
    }

    private static void TargetMonitorAndPrimaryFallback()
    {
        TaskbarInfo primary = NewTaskbar(new Rectangle(0, 1040, 1920, 40), new Rectangle(0, 0, 1920, 1080));
        primary.MonitorDeviceName = @"\\.\DISPLAY1";
        primary.IsPrimary = true;
        TaskbarInfo secondary = NewTaskbar(new Rectangle(-1280, 984, 1280, 40), new Rectangle(-1280, 0, 1280, 1024));
        secondary.MonitorDeviceName = @"\\.\DISPLAY2";
        IList<TaskbarInfo> taskbars = new List<TaskbarInfo> { primary, secondary };

        Equal(secondary, TaskbarDockingService.SelectTaskbar(taskbars, @"\\.\DISPLAY2"));
        Equal(primary, TaskbarDockingService.SelectTaskbar(taskbars, @"\\.\MISSING"));
        Equal(primary, TaskbarDockingService.SelectTaskbar(taskbars, "Primary"));
        Equal(primary, TaskbarDockingService.SelectTaskbar(taskbars, string.Empty));
    }

    private static void DpiScaledPositioning()
    {
        TaskbarInfo taskbar = NewTaskbar(new Rectangle(0, 1020, 2560, 60), new Rectangle(0, 0, 2560, 1080));
        Rectangle result = TaskbarPositionCalculator.Calculate(taskbar, new Size(198, 48), new Rectangle(2250, 1020, 310, 60), 0, 0);
        Equal(198, result.Width);
        Equal(48, result.Height);
        True(result.Left >= taskbar.Bounds.Left && result.Right <= taskbar.Bounds.Right);
        True(result.Top >= taskbar.Bounds.Top && result.Bottom <= taskbar.Bounds.Bottom);
    }

    private static void WidgetHeightConstrainedToTaskbar()
    {
        TaskbarInfo taskbar = NewTaskbar(new Rectangle(0, 1040, 1920, 40), new Rectangle(0, 0, 1920, 1080));
        Rectangle result = TaskbarPositionCalculator.Calculate(taskbar, new Size(165, 40), null, 0, 0);
        Equal(32, result.Height);
        Equal(1044, result.Top);
    }

    private static void AvoidsThirdPartyTaskbarWidgets()
    {
        TaskbarInfo taskbar = NewTaskbar(new Rectangle(1920, 1032, 1920, 48), new Rectangle(1920, 0, 1920, 1080));
        Rectangle trafficMonitor = new Rectangle(3392, 1040, 362, 32);
        IList<Rectangle> occupied = new List<Rectangle> { trafficMonitor };
        Rectangle result = TaskbarPositionCalculator.Calculate(taskbar, new Size(132, 32), null, occupied, 0, 0);

        Equal(3250, result.Left);
        True(!result.IntersectsWith(trafficMonitor));
        Equal(10, trafficMonitor.Left - result.Right);
    }

    private static void TaskbarCreatedRegistration()
    {
        True(NativeMethods.RegisterWindowMessage("TaskbarCreated") != 0);
    }

    private static TaskbarInfo NewTaskbar(Rectangle bounds, Rectangle monitor)
    {
        return new TaskbarInfo
        {
            Bounds = bounds,
            MonitorBounds = monitor,
            Side = TaskbarPositionCalculator.DetectSide(bounds, monitor)
        };
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            passed++;
            Console.WriteLine("PASS: " + name);
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine("FAIL: {0}\n{1}", name, exception);
            Environment.Exit(1);
        }
    }

    private static void Equal(object expected, object actual)
    {
        if (!object.Equals(expected, actual)) throw new InvalidOperationException(string.Format("Expected {0}, got {1}.", expected, actual));
    }

    private static void True(bool value)
    {
        if (!value) throw new InvalidOperationException("Expected true.");
    }
}
