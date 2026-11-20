using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace SpotifyNoSleep
{
    [DataContract]
    public sealed class AppConfig
    {
        [DataMember]
        public bool PreventLockScreen { get; set; }

        [DataMember]
        public bool StrictLockPrevention { get; set; }

        [DataMember]
        public int PollIntervalMs { get; set; }

        [DataMember]
        public int GracePeriodSeconds { get; set; }

        [DataMember]
        public bool ShowNotifications { get; set; }

        [DataMember]
        public bool EnableLogging { get; set; }

        public AppConfig()
        {
            PreventLockScreen = true;
            StrictLockPrevention = false;
            PollIntervalMs = 2000;
            GracePeriodSeconds = 10;
            ShowNotifications = false;
            EnableLogging = true;
        }

        public void Normalize()
        {
            if (PollIntervalMs < 500)
            {
                PollIntervalMs = 500;
            }
            else if (PollIntervalMs > 30000)
            {
                PollIntervalMs = 30000;
            }

            if (GracePeriodSeconds < 0)
            {
                GracePeriodSeconds = 0;
            }
            else if (GracePeriodSeconds > 300)
            {
                GracePeriodSeconds = 300;
            }
        }

        public static string GetDefaultDataDirectory()
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrWhiteSpace(appData))
            {
                appData = Environment.GetEnvironmentVariable("APPDATA");
            }
            if (string.IsNullOrWhiteSpace(appData))
            {
                appData = AppDomain.CurrentDomain.BaseDirectory;
            }
            return Path.Combine(appData, "SpotifyNoSleep");
        }

        public static string GetDefaultConfigPath()
        {
            return Path.Combine(GetDefaultDataDirectory(), "config.json");
        }

        public static AppConfig Load(string path)
        {
            AppConfig config = new AppConfig();
            try
            {
                if (!File.Exists(path))
                {
                    config.Save(path);
                    return config;
                }

                byte[] bytes = File.ReadAllBytes(path);
                if (bytes.Length == 0)
                {
                    return config;
                }

                int offset = 0;
                if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                {
                    offset = 3;
                }

                using (MemoryStream ms = new MemoryStream(bytes, offset, bytes.Length - offset))
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(AppConfig));
                    AppConfig loaded = serializer.ReadObject(ms) as AppConfig;
                    if (loaded != null)
                    {
                        loaded.Normalize();
                        return loaded;
                    }
                }
            }
            catch
            {
                // Fall back to defaults if config file is corrupted or unreadable.
            }

            config.Normalize();
            return config;
        }

        public void Save(string path)
        {
            Normalize();
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                using (MemoryStream ms = new MemoryStream())
                {
                    DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(AppConfig));
                    serializer.WriteObject(ms, this);
                    string json = Encoding.UTF8.GetString(ms.ToArray());
                    File.WriteAllText(path, json, new UTF8Encoding(false));
                }
            }
            catch
            {
                // Non-fatal if settings cannot be saved.
            }
        }
    }
}
