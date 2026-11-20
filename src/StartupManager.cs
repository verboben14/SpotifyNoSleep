using System;
using Microsoft.Win32;

namespace SpotifyNoSleep
{
    public static class StartupManager
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string AppValueName = "SpotifyNoSleep";

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null)
                    {
                        return false;
                    }

                    object val = key.GetValue(AppValueName);
                    return val != null && !string.IsNullOrWhiteSpace(val.ToString());
                }
            }
            catch
            {
                return false;
            }
        }

        public static string GetRegisteredCommand()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null)
                    {
                        return string.Empty;
                    }

                    object val = key.GetValue(AppValueName);
                    return val == null ? string.Empty : val.ToString();
                }
            }
            catch
            {
                return string.Empty;
            }
        }

        public static bool SetEnabled(bool enabled, string exePath)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (key == null)
                    {
                        return false;
                    }

                    if (enabled)
                    {
                        string command = string.Format("\"{0}\"", exePath);
                        key.SetValue(AppValueName, command, RegistryValueKind.String);
                    }
                    else
                    {
                        if (key.GetValue(AppValueName) != null)
                        {
                            key.DeleteValue(AppValueName, false);
                        }
                    }

                    return true;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
