using System;
using System.Collections.Generic;

namespace TaskbarTimerWidget
{
    internal enum TimerState
    {
        Idle,
        Running,
        Paused,
        Completed
    }

    internal sealed class TimerPreset
    {
        public TimerPreset(string id, string label, TimeSpan duration, int sortOrder)
        {
            Id = id;
            Label = label;
            Duration = duration;
            SortOrder = sortOrder;
        }

        public string Id { get; private set; }
        public string Label { get; private set; }
        public TimeSpan Duration { get; private set; }
        public int SortOrder { get; private set; }

        public override string ToString()
        {
            return Label;
        }
    }

    internal static class TimerPresets
    {
        private static readonly TimerPreset[] presets = new TimerPreset[]
        {
            new TimerPreset("10m", "00:10:00", TimeSpan.FromMinutes(10), 0),
            new TimerPreset("20m", "00:20:00", TimeSpan.FromMinutes(20), 1),
            new TimerPreset("30m", "00:30:00", TimeSpan.FromMinutes(30), 2),
            new TimerPreset("1h", "01:00:00", TimeSpan.FromHours(1), 3),
            new TimerPreset("1h30m", "01:30:00", TimeSpan.FromMinutes(90), 4),
            new TimerPreset("3h", "03:00:00", TimeSpan.FromHours(3), 5),
            new TimerPreset("5h", "05:00:00", TimeSpan.FromHours(5), 6)
        };

        public static IList<TimerPreset> All
        {
            get { return Array.AsReadOnly(presets); }
        }

        public static TimerPreset Find(string id)
        {
            foreach (TimerPreset preset in presets)
            {
                if (string.Equals(preset.Id, id, StringComparison.OrdinalIgnoreCase)) return preset;
            }

            return presets[2];
        }
    }

    internal sealed class TimerStateMachine
    {
        private TimeSpan baseDuration;
        private TimeSpan remaining;
        private DateTime endsAt;

        public TimerStateMachine(TimeSpan initialDuration)
        {
            SetDuration(initialDuration);
        }

        public TimerState State { get; private set; }
        public TimeSpan Remaining { get { return remaining; } }
        public TimeSpan BaseDuration { get { return baseDuration; } }

        public void SelectPreset(TimerPreset preset)
        {
            if (preset == null) throw new ArgumentNullException("preset");
            SetDuration(preset.Duration);
        }

        public void SetDuration(TimeSpan duration)
        {
            if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException("duration");
            baseDuration = duration;
            remaining = duration;
            State = TimerState.Idle;
        }

        public bool Start(DateTime now)
        {
            if (State != TimerState.Idle && State != TimerState.Paused) return false;
            if (remaining <= TimeSpan.Zero) return false;
            endsAt = now + remaining;
            State = TimerState.Running;
            return true;
        }

        public void Pause(DateTime now)
        {
            if (State != TimerState.Running) return;
            remaining = endsAt - now;
            if (remaining <= TimeSpan.Zero)
            {
                remaining = TimeSpan.Zero;
                State = TimerState.Completed;
                return;
            }

            State = TimerState.Paused;
        }

        public bool Resume(DateTime now)
        {
            if (State != TimerState.Paused) return false;
            return Start(now);
        }

        public bool Tick(DateTime now)
        {
            if (State != TimerState.Running) return false;
            remaining = endsAt - now;
            if (remaining > TimeSpan.Zero) return false;

            remaining = TimeSpan.Zero;
            State = TimerState.Completed;
            return true;
        }

        public void Reset()
        {
            remaining = baseDuration;
            State = TimerState.Idle;
        }
    }
}
