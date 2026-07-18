using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Media;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace TaskbarTimerWidget
{
    internal static class Program
    {
        private const string MutexName = @"Local\TaskbarTimerWidget";
        private const string ActivationEventName = @"Local\TaskbarTimerWidget.Activate";

        [STAThread]
        private static void Main(string[] args)
        {
            bool interactiveLaunch = args.Length == 0 || !string.Equals(args[0], "--startup", StringComparison.OrdinalIgnoreCase);
            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            using (EventWaitHandle activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ActivationEventName))
            {
                if (!createdNew)
                {
                    if (interactiveLaunch) activationEvent.Set();
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                using (TaskbarTimerWidget widget = new TaskbarTimerWidget())
                {
                    RegisteredWaitHandle activationRegistration = ThreadPool.RegisterWaitForSingleObject(
                        activationEvent,
                        delegate
                        {
                            if (widget.IsDisposed || !widget.IsHandleCreated) return;
                            try
                            {
                                widget.BeginInvoke(new MethodInvoker(widget.ActivateFromShortcut));
                            }
                            catch (InvalidOperationException)
                            {
                                // Closing; nothing left to activate.
                            }
                        },
                        null,
                        Timeout.Infinite,
                        false);

                    Application.Run(widget);
                    activationRegistration.Unregister(null);
                }
                GC.KeepAlive(mutex);
            }
        }
    }

    internal sealed class TaskbarTimerWidget : Form
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "TaskbarTimerWidget";
        private const int WidgetWidth = 132;
        private const int WidgetHeight = 32;

        private readonly System.Windows.Forms.Timer countdownTimer = new System.Windows.Forms.Timer();
        private readonly System.Windows.Forms.Timer dockingSafetyTimer = new System.Windows.Forms.Timer();
        private readonly CountdownDisplay countdownDisplay = new CountdownDisplay();
        private readonly PresetDropDownButton presetButton = new PresetDropDownButton();
        private readonly ToolTip toolTip = new ToolTip();
        private readonly ToolStripMenuItem startMenuItem = new ToolStripMenuItem("Start");
        private readonly ToolStripMenuItem pauseMenuItem = new ToolStripMenuItem("Pause");
        private readonly ToolStripMenuItem startupMenuItem = new ToolStripMenuItem("Run at Windows startup");
        private readonly ToolStripMenuItem monitorMenuItem = new ToolStripMenuItem("Select monitor");
        private readonly ContextMenuStrip contextMenu = new ContextMenuStrip();
        private readonly ContextMenuStrip presetDropDownMenu = new ContextMenuStrip();
        private readonly SettingsService settingsService = new SettingsService();
        private readonly AppSettings settings;
        private readonly TimerStateMachine timerState;
        private readonly int taskbarCreatedMessage;

        private TaskbarDockingService dockingService;
        private TimerPreset selectedPreset;
        private DateTime finishAnimationStarted;
        private string lastDisplayText = string.Empty;
        private bool exiting;
        private bool dialogOpen;
        private bool finishAnimationActive;
        private bool warningHighlightActive;
        private bool lightTheme;
        private bool themeApplied;
        private bool repositionQueued;
        private bool pendingForcedReposition;
        private bool eventsDetached;

        public TaskbarTimerWidget()
        {
            settings = settingsService.Load();
            settings.AlwaysVisible = true;
            selectedPreset = TimerPresets.Find(settings.SelectedPresetId);
            timerState = new TimerStateMachine(selectedPreset.Duration);
            taskbarCreatedMessage = NativeMethods.RegisterWindowMessage("TaskbarCreated");

            Text = "Taskbar Timer Widget";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            ClientSize = new Size(WidgetWidth, WidgetHeight);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            AutoScaleMode = AutoScaleMode.Dpi;
            lightTheme = IsLightTheme();

            countdownDisplay.Dock = DockStyle.Fill;
            countdownDisplay.Font = new Font("Consolas", 11.0f, FontStyle.Bold);
            countdownDisplay.Cursor = Cursors.Default;

            presetButton.Dock = DockStyle.Right;
            presetButton.Width = 24;
            presetButton.Cursor = Cursors.Hand;
            presetButton.AccessibleName = "Timer presets";
            presetButton.AccessibleDescription = "Choose a countdown duration without starting the timer.";
            presetButton.Click += delegate { ShowPresetDropDown(); };

            Controls.Add(countdownDisplay);
            Controls.Add(presetButton);
            BuildPresetDropDown();
            BuildContextMenu();

            toolTip.SetToolTip(countdownDisplay, "Right-click and choose Set custom timer to enter a custom duration.");
            toolTip.SetToolTip(presetButton, "Choose a timer preset (does not start automatically).");

            countdownTimer.Interval = 200;
            countdownTimer.Tick += CountdownTimerTick;
            dockingSafetyTimer.Interval = 3000;
            dockingSafetyTimer.Tick += delegate { RepositionWidget(true); };
            dockingSafetyTimer.Start();

            ApplyTheme();
            ApplyStateToControls();
            UpdateDisplay();

            Shown += delegate
            {
                RepositionWidget(true);
            };
            FormClosing += WidgetFormClosing;
            SystemEvents.UserPreferenceChanged += UserPreferenceChanged;
            SystemEvents.DisplaySettingsChanged += DisplaySettingsChanged;
        }

        public void ActivateFromShortcut()
        {
            if (!Visible) Show();
            RepositionWidget(true);
            Invalidate();
        }

        protected override CreateParams CreateParams
        {
            get
            {
                const int WsExToolWindow = 0x00000080;
                CreateParams parameters = base.CreateParams;
                parameters.ExStyle |= WsExToolWindow;
                return parameters;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            dockingService = new TaskbarDockingService(Handle);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (Width > 0 && Height > 0)
            {
                Region oldRegion = Region;
                Region = CreateRoundedRegion(ClientRectangle, Math.Max(6, Height / 4));
                if (oldRegion != null) oldRegion.Dispose();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color borderColor = lightTheme ? Color.FromArgb(80, 95, 105) : Color.FromArgb(88, 99, 116);
            using (Pen border = new Pen(borderColor))
            using (GraphicsPath path = CreateRoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), Math.Max(6, Height / 4)))
                e.Graphics.DrawPath(border, path);
        }

        protected override void WndProc(ref Message message)
        {
            int messageId = message.Msg;
            base.WndProc(ref message);

            if (messageId == taskbarCreatedMessage
                || messageId == NativeMethods.WmDisplayChange
                || messageId == NativeMethods.WmDpiChanged
                || messageId == NativeMethods.WmSettingChange
                || messageId == NativeMethods.WmDeviceChange)
            {
                QueueReposition(true);
            }
            else if (messageId == NativeMethods.WmWindowPosChanged && dockingService != null && !dockingService.IsApplying)
            {
                QueueReposition(false);
            }
        }

        private void BuildPresetDropDown()
        {
            foreach (TimerPreset preset in TimerPresets.All)
            {
                ToolStripMenuItem item = new ToolStripMenuItem(preset.Label);
                item.Tag = preset;
                item.Font = new Font("Consolas", 10.0f, FontStyle.Regular);
                item.Click += PresetMenuItemClick;
                presetDropDownMenu.Items.Add(item);
            }
        }

        private void BuildContextMenu()
        {
            ToolStripMenuItem setMenuItem = new ToolStripMenuItem("Set custom timer...");
            setMenuItem.Click += delegate { OpenDurationDialog(); };

            ToolStripMenuItem presetsMenuItem = new ToolStripMenuItem("Timer presets");
            foreach (TimerPreset preset in TimerPresets.All)
            {
                ToolStripMenuItem item = new ToolStripMenuItem(preset.Label);
                item.Tag = preset;
                item.Click += PresetMenuItemClick;
                presetsMenuItem.DropDownItems.Add(item);
            }

            startMenuItem.Click += delegate { StartTimer(); };
            pauseMenuItem.Enabled = false;
            pauseMenuItem.Click += delegate { TogglePause(); };
            ToolStripMenuItem resetMenuItem = new ToolStripMenuItem("Reset");
            resetMenuItem.Click += delegate { ResetTimer(); };
            ToolStripMenuItem reattachMenuItem = new ToolStripMenuItem("Realign on taskbar");
            reattachMenuItem.Click += delegate { RepositionWidget(true); };
            monitorMenuItem.DropDownOpening += delegate { PopulateMonitorMenu(); };
            startupMenuItem.CheckOnClick = true;
            startupMenuItem.Checked = IsStartupEnabled();
            startupMenuItem.Click += delegate { SetStartup(startupMenuItem.Checked); };
            ToolStripMenuItem exitMenuItem = new ToolStripMenuItem("Exit");
            exitMenuItem.Click += delegate { ExitApplication(); };

            contextMenu.Items.AddRange(new ToolStripItem[]
            {
                setMenuItem,
                presetsMenuItem,
                startMenuItem,
                pauseMenuItem,
                resetMenuItem,
                new ToolStripSeparator(),
                monitorMenuItem,
                reattachMenuItem,
                startupMenuItem,
                new ToolStripSeparator(),
                exitMenuItem
            });
            contextMenu.Opening += delegate { UpdatePresetChecks(); };
            countdownDisplay.ContextMenuStrip = contextMenu;
            presetButton.ContextMenuStrip = contextMenu;
            ContextMenuStrip = contextMenu;
        }

        private void PopulateMonitorMenu()
        {
            monitorMenuItem.DropDownItems.Clear();
            foreach (Screen screen in Screen.AllScreens)
            {
                ToolStripMenuItem item = new ToolStripMenuItem(screen.DeviceName + (screen.Primary ? " (Primary)" : string.Empty));
                item.Tag = screen.DeviceName;
                item.Checked = string.Equals(settings.TargetMonitor, screen.DeviceName, StringComparison.OrdinalIgnoreCase);
                item.Click += delegate(object sender, EventArgs e)
                {
                    settings.TargetMonitor = (string)((ToolStripMenuItem)sender).Tag;
                    SaveSettings();
                    RepositionWidget(true);
                };
                monitorMenuItem.DropDownItems.Add(item);
            }
        }

        private void ShowPresetDropDown()
        {
            if (timerState.State == TimerState.Running) return;
            UpdatePresetChecks();

            TaskbarDockSide side = dockingService == null ? TaskbarDockSide.Bottom : dockingService.CurrentSide;
            Point anchor;
            ToolStripDropDownDirection direction;
            if (side == TaskbarDockSide.Top)
            {
                anchor = new Point(presetButton.Width, presetButton.Height);
                direction = ToolStripDropDownDirection.BelowLeft;
            }
            else if (side == TaskbarDockSide.Left)
            {
                anchor = new Point(presetButton.Width, 0);
                direction = ToolStripDropDownDirection.Right;
            }
            else if (side == TaskbarDockSide.Right)
            {
                anchor = Point.Empty;
                direction = ToolStripDropDownDirection.Left;
            }
            else
            {
                anchor = new Point(presetButton.Width, 0);
                direction = ToolStripDropDownDirection.AboveLeft;
            }

            presetDropDownMenu.Show(presetButton, anchor, direction);
            foreach (ToolStripItem rawItem in presetDropDownMenu.Items)
            {
                ToolStripMenuItem item = rawItem as ToolStripMenuItem;
                if (item != null && item.Checked)
                {
                    item.Select();
                    break;
                }
            }
        }

        private void PresetMenuItemClick(object sender, EventArgs e)
        {
            if (timerState.State == TimerState.Running) return;
            ToolStripMenuItem menuItem = (ToolStripMenuItem)sender;
            SelectPreset((TimerPreset)menuItem.Tag);
        }

        private void SelectPreset(TimerPreset preset)
        {
            selectedPreset = preset;
            timerState.SelectPreset(preset);
            settings.SelectedPresetId = preset.Id;
            finishAnimationActive = false;
            warningHighlightActive = false;
            ApplyCountdownColors(GetThemeBackColor(), GetThemeForeColor());
            SaveSettings();
            ApplyStateToControls();
            UpdatePresetChecks();
            UpdateDisplay();
            RefreshCountdownTimer();
        }

        private void UpdatePresetChecks()
        {
            SetPresetChecks(presetDropDownMenu.Items);
            foreach (ToolStripItem contextItem in contextMenu.Items)
            {
                ToolStripMenuItem menu = contextItem as ToolStripMenuItem;
                if (menu != null && menu.Text == "Timer presets") SetPresetChecks(menu.DropDownItems);
            }
        }

        private void SetPresetChecks(ToolStripItemCollection items)
        {
            foreach (ToolStripItem rawItem in items)
            {
                ToolStripMenuItem item = rawItem as ToolStripMenuItem;
                TimerPreset preset = item == null ? null : item.Tag as TimerPreset;
                if (preset != null)
                {
                    item.Checked = preset.Id == selectedPreset.Id;
                    item.Enabled = timerState.State != TimerState.Running;
                }
            }
        }

        private void CountdownTimerTick(object sender, EventArgs e)
        {
            if (finishAnimationActive)
            {
                UpdateFinishAnimation();
                return;
            }

            if (timerState.State != TimerState.Running) return;
            if (timerState.Tick(DateTime.UtcNow))
            {
                AnnounceFinished();
                ApplyStateToControls();
                return;
            }

            UpdateDisplay();
            UpdateWarningVisual();
            RefreshCountdownTimer();
        }

        private void OpenDurationDialog()
        {
            if (dialogOpen) return;
            dialogOpen = true;
            try
            {
                TimeSpan suggested = timerState.Remaining > TimeSpan.Zero ? timerState.Remaining : selectedPreset.Duration;
                using (DurationDialog dialog = new DurationDialog(suggested))
                {
                    if (dialog.ShowDialog() != DialogResult.OK) return;
                    timerState.SetDuration(dialog.Duration);
                    timerState.Start(DateTime.UtcNow);
                }

                finishAnimationActive = false;
                warningHighlightActive = false;
                ApplyCountdownColors(GetThemeBackColor(), GetThemeForeColor());
                toolTip.SetToolTip(countdownDisplay, "Right-click and choose Set custom timer to enter a custom duration.");
                ApplyStateToControls();
                UpdateDisplay();
                RefreshCountdownTimer();
            }
            finally
            {
                dialogOpen = false;
                RepositionWidget(true);
            }
        }

        private void TogglePause()
        {
            if (timerState.State == TimerState.Running)
            {
                timerState.Pause(DateTime.UtcNow);
                warningHighlightActive = false;
                ApplyCountdownColors(GetThemeBackColor(), GetThemeForeColor());
            }
            else if (timerState.State == TimerState.Paused)
            {
                timerState.Resume(DateTime.UtcNow);
            }
            else
            {
                return;
            }

            ApplyStateToControls();
            UpdateDisplay();
            RefreshCountdownTimer();
        }

        private void StartTimer()
        {
            if (!timerState.Start(DateTime.UtcNow)) return;

            finishAnimationActive = false;
            warningHighlightActive = false;
            ApplyCountdownColors(GetThemeBackColor(), GetThemeForeColor());
            ApplyStateToControls();
            UpdateDisplay();
            RefreshCountdownTimer();
        }

        private void ResetTimer()
        {
            timerState.Reset();
            finishAnimationActive = false;
            warningHighlightActive = false;
            ApplyCountdownColors(GetThemeBackColor(), GetThemeForeColor());
            toolTip.SetToolTip(countdownDisplay, "Right-click and choose Set custom timer to enter a custom duration.");
            ApplyStateToControls();
            UpdateDisplay();
            RefreshCountdownTimer();
            RepositionWidget(true);
        }

        private void ApplyStateToControls()
        {
            startMenuItem.Enabled = timerState.State == TimerState.Idle;
            pauseMenuItem.Enabled = timerState.State == TimerState.Running || timerState.State == TimerState.Paused;
            pauseMenuItem.Text = timerState.State == TimerState.Paused ? "Resume" : "Pause";
            presetButton.Enabled = timerState.State != TimerState.Running;
            presetButton.Invalidate();
            UpdatePresetChecks();
        }

        private void RefreshCountdownTimer()
        {
            bool requiresAnimation = finishAnimationActive
                || (timerState.State == TimerState.Running && timerState.Remaining.TotalSeconds <= 11.0);
            countdownTimer.Interval = requiresAnimation ? 50 : 200;
            countdownTimer.Enabled = finishAnimationActive || timerState.State == TimerState.Running;
        }

        private void UpdateDisplay()
        {
            TimeSpan remaining = timerState.Remaining;
            long totalSeconds = remaining <= TimeSpan.Zero ? 0 : (long)Math.Ceiling(remaining.TotalSeconds);
            long totalHours = totalSeconds / 3600;
            int minutes = (int)((totalSeconds % 3600) / 60);
            int seconds = (int)(totalSeconds % 60);
            string displayText = string.Format("{0:00}:{1:00}:{2:00}", totalHours, minutes, seconds);
            if (lastDisplayText != displayText)
            {
                countdownDisplay.Text = displayText;
                lastDisplayText = displayText;
            }

            Text = timerState.State == TimerState.Idle
                ? "Taskbar Timer Widget"
                : displayText + " - Taskbar Timer Widget";
        }

        private void AnnounceFinished()
        {
            finishAnimationActive = true;
            finishAnimationStarted = DateTime.Now;
            warningHighlightActive = false;
            countdownDisplay.Text = "00:00:00";
            lastDisplayText = "00:00:00";
            Text = "00:00:00 - Taskbar Timer Widget";
            toolTip.SetToolTip(countdownDisplay, "Time is up. Choose a preset, or right-click and choose Set custom timer.");
            RefreshCountdownTimer();
            RepositionWidget(true);
            SystemSounds.Exclamation.Play();
        }

        private void UpdateWarningVisual()
        {
            TimeSpan remaining = timerState.Remaining;
            bool inFinalTenSeconds = remaining > TimeSpan.Zero && remaining.TotalSeconds <= 10.0;
            if (!inFinalTenSeconds)
            {
                if (warningHighlightActive)
                {
                    warningHighlightActive = false;
                    ApplyCountdownColors(GetThemeBackColor(), GetThemeForeColor());
                }
                return;
            }

            bool highlight = ((int)(remaining.TotalMilliseconds / 250.0) % 2) == 0;
            if (warningHighlightActive == highlight) return;
            warningHighlightActive = highlight;
            ApplyCountdownColors(
                highlight ? Color.FromArgb(166, 44, 54) : GetThemeBackColor(),
                highlight ? Color.White : GetThemeForeColor());
        }

        private void UpdateFinishAnimation()
        {
            const double animationDurationMilliseconds = 1800.0;
            double elapsed = (DateTime.Now - finishAnimationStarted).TotalMilliseconds;
            if (elapsed >= animationDurationMilliseconds)
            {
                finishAnimationActive = false;
                ApplyCountdownColors(GetThemeBackColor(), GetThemeForeColor());
                RefreshCountdownTimer();
                return;
            }

            double progress = elapsed / animationDurationMilliseconds;
            double pulse = Math.Pow(Math.Sin(progress * Math.PI * 3.0), 2.0) * (1.0 - progress * 0.45);
            Color background = BlendColor(GetThemeBackColor(), Color.FromArgb(202, 48, 60), pulse);
            Color foreground = BlendColor(GetThemeForeColor(), Color.White, pulse);
            ApplyCountdownColors(background, foreground);
        }

        private void QueueReposition(bool force)
        {
            if (exiting || IsDisposed) return;
            pendingForcedReposition = pendingForcedReposition || force;
            if (repositionQueued || !IsHandleCreated) return;
            repositionQueued = true;
            BeginInvoke(new MethodInvoker(delegate
            {
                repositionQueued = false;
                bool applyForce = pendingForcedReposition;
                pendingForcedReposition = false;
                RepositionWidget(applyForce);
            }));
        }

        private void RepositionWidget(bool force)
        {
            if (exiting || !IsHandleCreated || dockingService == null) return;
            dockingService.Reposition(Size, settings.TargetMonitor, settings.HorizontalOffset, settings.VerticalOffset, force);
            if (!string.IsNullOrEmpty(dockingService.CurrentMonitor)
                && !string.Equals(settings.TargetMonitor, dockingService.CurrentMonitor, StringComparison.OrdinalIgnoreCase))
            {
                settings.TargetMonitor = dockingService.CurrentMonitor;
                SaveSettings();
            }
        }

        private void DisplaySettingsChanged(object sender, EventArgs e)
        {
            QueueReposition(true);
        }

        private void ApplyCountdownColors(Color background, Color foreground)
        {
            countdownDisplay.SetColors(background, foreground);
        }

        private Color GetThemeBackColor()
        {
            return lightTheme ? Color.FromArgb(232, 234, 237) : Color.FromArgb(38, 40, 45);
        }

        private Color GetThemeForeColor()
        {
            return lightTheme ? Color.FromArgb(26, 28, 32) : Color.FromArgb(245, 247, 250);
        }

        private void ApplyTheme()
        {
            bool newLightTheme = IsLightTheme();
            if (themeApplied && lightTheme == newLightTheme) return;
            lightTheme = newLightTheme;
            BackColor = GetThemeBackColor();
            presetButton.SetColors(
                GetThemeBackColor(),
                GetThemeForeColor(),
                lightTheme ? Color.FromArgb(210, 214, 220) : Color.FromArgb(52, 55, 62));
            if (!finishAnimationActive && !warningHighlightActive)
                ApplyCountdownColors(GetThemeBackColor(), GetThemeForeColor());
            themeApplied = true;
            Invalidate(true);
        }

        private static bool IsLightTheme()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object value = key == null ? null : key.GetValue("SystemUsesLightTheme");
                    return value is int && (int)value != 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private void UserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
        {
            if (!IsDisposed) BeginInvoke(new MethodInvoker(ApplyTheme));
            QueueReposition(true);
        }

        private void SaveSettings()
        {
            try
            {
                settings.AlwaysVisible = true;
                settingsService.Save(settings);
            }
            catch
            {
                // Keep running if the registry is unavailable.
            }
        }

        private bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath))
                {
                    string registeredCommand = key == null ? null : key.GetValue(RunValueName) as string;
                    string expectedCommand = "\"" + Application.ExecutablePath + "\" --startup";
                    return string.Equals(registeredCommand, expectedCommand, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch
            {
                return false;
            }
        }

        private void SetStartup(bool enabled)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (enabled) key.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\" --startup");
                    else key.DeleteValue(RunValueName, false);
                }
            }
            catch (Exception exception)
            {
                startupMenuItem.Checked = !enabled;
                MessageBox.Show(
                    "The startup setting could not be changed.\n\n" + exception.Message,
                    "Taskbar Timer Widget",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void ExitApplication()
        {
            exiting = true;
            countdownTimer.Stop();
            dockingSafetyTimer.Stop();
            DetachSystemEvents();
            Close();
        }

        private void WidgetFormClosing(object sender, FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                if (!Visible) Show();
                RepositionWidget(true);
                return;
            }

            DetachSystemEvents();
        }

        private void DetachSystemEvents()
        {
            if (eventsDetached) return;
            eventsDetached = true;
            SystemEvents.UserPreferenceChanged -= UserPreferenceChanged;
            SystemEvents.DisplaySettingsChanged -= DisplaySettingsChanged;
        }

        private static Color BlendColor(Color from, Color to, double amount)
        {
            amount = Math.Max(0.0, Math.Min(1.0, amount));
            return Color.FromArgb(
                (int)Math.Round(from.R + (to.R - from.R) * amount),
                (int)Math.Round(from.G + (to.G - from.G) * amount),
                (int)Math.Round(from.B + (to.B - from.B) * amount));
        }

        private static Region CreateRoundedRegion(Rectangle bounds, int radius)
        {
            using (GraphicsPath path = CreateRoundedPath(bounds, radius))
                return new Region(path);
        }

        private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = radius * 2;
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class CountdownDisplay : Control
    {
        private Color backgroundColor = SystemColors.Control;
        private Color foregroundColor = SystemColors.ControlText;

        public CountdownDisplay()
        {
            SetStyle(
                ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.Opaque
                | ControlStyles.ResizeRedraw,
                true);
            UpdateStyles();
        }

        public override string Text
        {
            get { return base.Text; }
            set
            {
                if (string.Equals(base.Text, value, StringComparison.Ordinal)) return;
                base.Text = value;
                Invalidate();
            }
        }

        public void SetColors(Color background, Color foreground)
        {
            if (backgroundColor == background && foregroundColor == foreground) return;
            backgroundColor = background;
            foregroundColor = foreground;
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(backgroundColor);
            TextRenderer.DrawText(
                e.Graphics,
                Text,
                Font,
                ClientRectangle,
                foregroundColor,
                TextFormatFlags.HorizontalCenter
                | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine
                | TextFormatFlags.NoPadding
                | TextFormatFlags.NoPrefix);
        }
    }

    internal sealed class PresetDropDownButton : Control
    {
        private Color backgroundColor = SystemColors.Control;
        private Color foregroundColor = SystemColors.ControlText;
        private Color hoverColor = SystemColors.ControlLight;
        private bool hovered;

        public PresetDropDownButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            TabStop = true;
        }

        public void SetColors(Color background, Color foreground, Color hover)
        {
            backgroundColor = background;
            foregroundColor = foreground;
            hoverColor = hover;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hovered = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hovered = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) Focus();
            base.OnMouseDown(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            return key == Keys.Up || key == Keys.Down || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter || e.KeyCode == Keys.Space || e.KeyCode == Keys.Down || e.KeyCode == Keys.Up)
            {
                OnClick(EventArgs.Empty);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color fill = !Enabled ? backgroundColor : (hovered || Focused ? hoverColor : backgroundColor);
            e.Graphics.Clear(fill);
            Color arrowColor = Enabled ? foregroundColor : Color.FromArgb(110, foregroundColor);
            Point center = new Point(Width / 2, Height / 2 + 1);
            Point[] arrow =
            {
                new Point(center.X - 4, center.Y - 2),
                new Point(center.X + 4, center.Y - 2),
                new Point(center.X, center.Y + 3)
            };
            using (SolidBrush brush = new SolidBrush(arrowColor)) e.Graphics.FillPolygon(brush, arrow);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, new Rectangle(3, 3, Width - 6, Height - 6), foregroundColor, fill);
        }
    }

    internal sealed class DurationDialog : Form
    {
        private readonly ModernNumberBox hours = new ModernNumberBox();
        private readonly ModernNumberBox minutes = new ModernNumberBox();
        private readonly ModernNumberBox seconds = new ModernNumberBox();
        private bool dragging;
        private Point dragCursorOrigin;
        private Point dragFormOrigin;

        public TimeSpan Duration
        {
            get { return new TimeSpan((int)hours.Value, (int)minutes.Value, (int)seconds.Value); }
        }

        public DurationDialog(TimeSpan current)
        {
            SuspendLayout();
            Text = "Set countdown";
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
            ClientSize = new Size(440, 326);
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = true;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(20, 23, 30);
            ForeColor = Color.White;
            AutoScaleMode = AutoScaleMode.Dpi;
            DoubleBuffered = true;
            Padding = new Padding(1);

            Panel header = new Panel();
            header.BackColor = Color.FromArgb(24, 28, 37);
            header.SetBounds(1, 1, 438, 78);
            Controls.Add(header);

            Label title = new Label();
            title.Text = "Set countdown";
            title.Font = new Font("Segoe UI", 13, FontStyle.Bold);
            title.ForeColor = Color.FromArgb(244, 247, 255);
            title.SetBounds(24, 14, 330, 28);
            header.Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "Choose a duration for your timer";
            subtitle.Font = new Font("Segoe UI", 9, FontStyle.Regular);
            subtitle.ForeColor = Color.FromArgb(143, 151, 170);
            subtitle.SetBounds(25, 43, 330, 22);
            header.Controls.Add(subtitle);

            ModernButton closeButton = new ModernButton();
            closeButton.Text = "×";
            closeButton.Font = new Font("Segoe UI", 15, FontStyle.Regular);
            closeButton.SetColors(Color.Transparent, Color.FromArgb(44, 49, 61), Color.FromArgb(235, 238, 245), Color.Transparent);
            closeButton.CornerRadius = 8;
            closeButton.SetBounds(390, 18, 32, 32);
            closeButton.TabStop = false;
            closeButton.AccessibleName = "Close";
            closeButton.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            header.Controls.Add(closeButton);

            AttachDragHandler(header);
            AttachDragHandler(title);
            AttachDragHandler(subtitle);

            ConfigureNumber(hours, 0, 999, Math.Min(999, (decimal)Math.Floor(current.TotalHours)), 24, "Hours");
            ConfigureNumber(minutes, 0, 59, current.Minutes, 160, "Minutes");
            ConfigureNumber(seconds, 0, 59, current.Seconds, 296, "Seconds");

            ModernButton cancelButton = new ModernButton();
            cancelButton.Text = "Cancel";
            cancelButton.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            cancelButton.SetColors(Color.FromArgb(30, 34, 44), Color.FromArgb(42, 48, 61), Color.FromArgb(218, 223, 235), Color.FromArgb(57, 64, 80));
            cancelButton.CornerRadius = 10;
            cancelButton.DialogResult = DialogResult.Cancel;
            cancelButton.SetBounds(24, 265, 188, 42);

            ModernButton startButton = new ModernButton();
            startButton.Text = "Start timer";
            startButton.Font = new Font("Segoe UI", 10, FontStyle.Bold);
            startButton.SetColors(Color.FromArgb(57, 121, 255), Color.FromArgb(75, 137, 255), Color.White, Color.Transparent);
            startButton.CornerRadius = 10;
            startButton.DialogResult = DialogResult.OK;
            startButton.SetBounds(228, 265, 188, 42);
            startButton.Click += delegate
            {
                if (Duration <= TimeSpan.Zero)
                {
                    MessageBox.Show(this, "Enter a duration greater than zero.", "Set Countdown", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    DialogResult = DialogResult.None;
                }
            };

            Controls.AddRange(new Control[] { cancelButton, startButton });
            AcceptButton = startButton;
            CancelButton = cancelButton;
            Shown += delegate { minutes.Focus(); };
            ResumeLayout(false);
        }

        private void ConfigureNumber(ModernNumberBox control, decimal minimum, decimal maximum, decimal value, int x, string caption)
        {
            Label label = new Label();
            label.Text = caption;
            label.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            label.TextAlign = ContentAlignment.MiddleLeft;
            label.ForeColor = Color.FromArgb(171, 179, 198);
            label.SetBounds(x, 98, 120, 24);
            Controls.Add(label);

            control.Minimum = (int)minimum;
            control.Maximum = (int)maximum;
            control.Value = (int)value;
            control.Font = new Font("Segoe UI", 18, FontStyle.Bold);
            control.AccessibleName = caption;
            control.SetBounds(x, 126, 120, 78);
            Controls.Add(control);
        }

        private void AttachDragHandler(Control control)
        {
            control.MouseDown += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button != MouseButtons.Left) return;
                dragging = true;
                dragCursorOrigin = Cursor.Position;
                dragFormOrigin = Location;
            };
            control.MouseMove += delegate
            {
                if (!dragging) return;
                Point offset = new Point(Cursor.Position.X - dragCursorOrigin.X, Cursor.Position.Y - dragCursorOrigin.Y);
                Location = new Point(dragFormOrigin.X + offset.X, dragFormOrigin.Y + offset.Y);
            };
            control.MouseUp += delegate { dragging = false; };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen border = new Pen(Color.FromArgb(55, 62, 78)))
            using (GraphicsPath path = ModernButton.CreateRoundedPath(new Rectangle(0, 0, ClientSize.Width - 1, ClientSize.Height - 1), 12))
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                e.Graphics.DrawPath(border, path);
            }
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            if (Width <= 0 || Height <= 0) return;
            Region oldRegion = Region;
            using (GraphicsPath path = ModernButton.CreateRoundedPath(ClientRectangle, 12)) Region = new Region(path);
            if (oldRegion != null) oldRegion.Dispose();
        }
    }

    internal sealed class RoundedPanel : Panel
    {
        public Color FillColor { get; set; }
        public Color BorderColor { get; set; }
        public int CornerRadius { get; set; }

        public RoundedPanel()
        {
            FillColor = Color.FromArgb(27, 31, 41);
            BorderColor = Color.FromArgb(48, 55, 70);
            CornerRadius = 10;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent == null ? BackColor : Parent.BackColor);
            using (GraphicsPath path = ModernButton.CreateRoundedPath(new Rectangle(1, 1, Width - 3, Height - 3), CornerRadius))
            using (SolidBrush fill = new SolidBrush(FillColor))
            using (Pen border = new Pen(BorderColor))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(border, path);
            }
        }
    }

    internal sealed class ModernButton : Button
    {
        private Color normalColor;
        private Color hoverColor;
        private Color textColor;
        private Color borderColor;
        private bool hovered;

        public int CornerRadius { get; set; }

        public ModernButton()
        {
            normalColor = Color.FromArgb(35, 40, 51);
            hoverColor = Color.FromArgb(48, 55, 69);
            textColor = Color.White;
            borderColor = Color.Transparent;
            CornerRadius = 8;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public void SetColors(Color normal, Color hover, Color foreground, Color border)
        {
            normalColor = normal;
            hoverColor = hover;
            textColor = foreground;
            borderColor = border;
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent == null ? BackColor : Parent.BackColor);
            Rectangle bounds = new Rectangle(1, 1, Width - 3, Height - 3);
            using (GraphicsPath path = CreateRoundedPath(bounds, CornerRadius))
            using (SolidBrush fill = new SolidBrush(hovered ? hoverColor : normalColor))
            {
                e.Graphics.FillPath(fill, path);
                if (borderColor.A > 0)
                {
                    using (Pen border = new Pen(borderColor)) e.Graphics.DrawPath(border, path);
                }
            }

            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, textColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix);
        }

        internal static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int diameter = Math.Max(2, Math.Min(radius * 2, Math.Min(bounds.Width, bounds.Height)));
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class ModernNumberBox : Control
    {
        private const int StepColumnWidth = 36;
        private int minimum;
        private int maximum = 100;
        private int currentValue;
        private string editBuffer = string.Empty;
        private bool replaceOnNextDigit = true;
        private int hoveredStep;
        private int pressedStep;

        public int Minimum
        {
            get { return minimum; }
            set { minimum = value; Value = currentValue; }
        }

        public int Maximum
        {
            get { return maximum; }
            set { maximum = Math.Max(value, minimum); Value = currentValue; }
        }

        public int Value
        {
            get { return currentValue; }
            set
            {
                int next = Math.Max(minimum, Math.Min(maximum, value));
                if (currentValue == next) return;
                currentValue = next;
                Invalidate();
            }
        }

        public ModernNumberBox()
        {
            AccessibleRole = AccessibleRole.SpinButton;
            TabStop = true;
            Cursor = Cursors.IBeam;
            ForeColor = Color.FromArgb(245, 247, 252);
            BackColor = Color.FromArgb(20, 23, 30);
            SetStyle(
                ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.Selectable,
                true);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            return key == Keys.Up || key == Keys.Down || key == Keys.Home || key == Keys.End || base.IsInputKey(keyData);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            replaceOnNextDigit = true;
            editBuffer = string.Empty;
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            replaceOnNextDigit = true;
            editBuffer = string.Empty;
            pressedStep = 0;
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Up) ChangeValue(1);
            else if (e.KeyCode == Keys.Down) ChangeValue(-1);
            else if (e.KeyCode == Keys.Home) Value = Minimum;
            else if (e.KeyCode == Keys.End) Value = Maximum;
            else if (e.KeyCode == Keys.Back)
            {
                string text = replaceOnNextDigit ? Value.ToString() : editBuffer;
                text = text.Length > 1 ? text.Substring(0, text.Length - 1) : "0";
                editBuffer = text;
                replaceOnNextDigit = false;
                int parsed;
                if (int.TryParse(text, out parsed)) Value = parsed;
            }
            else
            {
                base.OnKeyDown(e);
                return;
            }

            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (char.IsDigit(e.KeyChar))
            {
                string digit = e.KeyChar.ToString();
                string candidate = replaceOnNextDigit ? digit : editBuffer + digit;
                int parsed;
                if (int.TryParse(candidate, out parsed))
                {
                    if (parsed > Maximum) parsed = int.Parse(digit);
                    Value = parsed;
                    editBuffer = Value.ToString();
                    replaceOnNextDigit = false;
                }
                e.Handled = true;
            }
            base.OnKeyPress(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Focus();
                pressedStep = HitTestStep(e.Location);
                if (pressedStep != 0) ChangeValue(pressedStep);
                else replaceOnNextDigit = true;
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            pressedStep = 0;
            Invalidate();
            base.OnMouseUp(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int nextHover = HitTestStep(e.Location);
            if (hoveredStep != nextHover)
            {
                hoveredStep = nextHover;
                Cursor = hoveredStep == 0 ? Cursors.IBeam : Cursors.Hand;
                Invalidate();
            }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hoveredStep = 0;
            pressedStep = 0;
            Cursor = Cursors.IBeam;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ChangeValue(e.Delta > 0 ? 1 : -1);
            base.OnMouseWheel(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent == null ? BackColor : Parent.BackColor);

            Rectangle outerBounds = new Rectangle(1, 1, Width - 3, Height - 3);
            Color borderColor = Focused ? Color.FromArgb(71, 129, 255) : Color.FromArgb(48, 55, 70);
            using (GraphicsPath outer = ModernButton.CreateRoundedPath(outerBounds, 10))
            using (SolidBrush fill = new SolidBrush(Color.FromArgb(27, 31, 41)))
            using (Pen border = new Pen(borderColor, Focused ? 1.5f : 1.0f))
            {
                e.Graphics.FillPath(fill, outer);
                e.Graphics.DrawPath(border, outer);
            }

            Rectangle valueBounds = new Rectangle(8, 4, Width - StepColumnWidth - 10, Height - 8);
            TextRenderer.DrawText(
                e.Graphics,
                Value.ToString(),
                Font,
                valueBounds,
                ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

            DrawStep(e.Graphics, GetStepBounds(1), "+", 1);
            DrawStep(e.Graphics, GetStepBounds(-1), "−", -1);
        }

        private void DrawStep(Graphics graphics, Rectangle bounds, string glyph, int step)
        {
            Color fillColor = pressedStep == step
                ? Color.FromArgb(62, 72, 91)
                : hoveredStep == step ? Color.FromArgb(53, 61, 77) : Color.FromArgb(38, 44, 56);
            using (GraphicsPath path = ModernButton.CreateRoundedPath(bounds, 6))
            using (SolidBrush fill = new SolidBrush(fillColor)) graphics.FillPath(fill, path);

            using (Font glyphFont = new Font("Segoe UI", 11, FontStyle.Bold))
            {
                TextRenderer.DrawText(
                    graphics,
                    glyph,
                    glyphFont,
                    bounds,
                    Color.FromArgb(190, 201, 224),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            }
        }

        private void ChangeValue(int amount)
        {
            Value += amount;
            replaceOnNextDigit = true;
            editBuffer = string.Empty;
        }

        private int HitTestStep(Point location)
        {
            if (GetStepBounds(1).Contains(location)) return 1;
            if (GetStepBounds(-1).Contains(location)) return -1;
            return 0;
        }

        private Rectangle GetStepBounds(int step)
        {
            return new Rectangle(Width - 32, step > 0 ? 8 : Height - 36, 24, 28);
        }
    }
}
