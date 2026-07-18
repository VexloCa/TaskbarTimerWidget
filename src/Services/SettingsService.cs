using Microsoft.Win32;

namespace TaskbarTimerWidget
{
    internal sealed class AppSettings
    {
        public string SelectedPresetId = "30m";
        public string TargetMonitor = string.Empty;
        public int HorizontalOffset;
        public int VerticalOffset;
        public bool AlwaysVisible = true;
    }

    internal sealed class SettingsService
    {
        private const string SettingsKeyPath = @"Software\TaskbarTimerWidget";

        public AppSettings Load()
        {
            AppSettings settings = new AppSettings();
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(SettingsKeyPath))
                {
                    if (key == null) return settings;
                    settings.SelectedPresetId = ReadString(key, "SelectedPreset", settings.SelectedPresetId);
                    settings.TargetMonitor = ReadString(key, "LastTargetMonitor", settings.TargetMonitor);
                    settings.HorizontalOffset = ReadInt(key, "HorizontalOffset", 0);
                    settings.VerticalOffset = ReadInt(key, "VerticalOffset", 0);
                    settings.AlwaysVisible = ReadInt(key, "AlwaysVisible", 1) != 0;
                }
            }
            catch
            {
                // Fall back to defaults if the registry cannot be read.
                return new AppSettings();
            }

            return settings;
        }

        public void Save(AppSettings settings)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(SettingsKeyPath))
            {
                key.SetValue("SelectedPreset", settings.SelectedPresetId, RegistryValueKind.String);
                key.SetValue("LastTargetMonitor", settings.TargetMonitor, RegistryValueKind.String);
                key.SetValue("HorizontalOffset", settings.HorizontalOffset, RegistryValueKind.DWord);
                key.SetValue("VerticalOffset", settings.VerticalOffset, RegistryValueKind.DWord);
                key.SetValue("AlwaysVisible", settings.AlwaysVisible ? 1 : 0, RegistryValueKind.DWord);
            }
        }

        private static string ReadString(RegistryKey key, string name, string fallback)
        {
            string value = key.GetValue(name) as string;
            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        private static int ReadInt(RegistryKey key, string name, int fallback)
        {
            object value = key.GetValue(name);
            return value is int ? (int)value : fallback;
        }
    }
}
