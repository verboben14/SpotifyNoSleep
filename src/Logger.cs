using System;
using System.IO;
using System.Text;

namespace SpotifyNoSleep
{
    public sealed class Logger
    {
        private const long MaxLogBytes = 512 * 1024;
        private readonly object _syncRoot = new object();
        private readonly string _logPath;

        public bool Enabled { get; set; }
        public string LogPath { get { return _logPath; } }

        public Logger(string logPath, bool enabled)
        {
            _logPath = logPath;
            Enabled = enabled;
        }

        public static string GetDefaultLogPath()
        {
            return Path.Combine(AppConfig.GetDefaultDataDirectory(), "spotifynosleep.log");
        }

        public void Info(string message)
        {
            Write("INFO", message);
        }

        public void Warn(string message)
        {
            Write("WARN", message);
        }

        public void Error(string message, Exception ex)
        {
            string full = ex == null
                ? message
                : string.Format("{0}: {1}", message, ex.Message);
            Write("ERROR", full);
        }

        private void Write(string level, string message)
        {
            if (!Enabled || string.IsNullOrEmpty(_logPath))
            {
                return;
            }

            lock (_syncRoot)
            {
                try
                {
                    string dir = Path.GetDirectoryName(_logPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    RotateIfNeeded();

                    string line = string.Format(
                        "{0:yyyy-MM-dd HH:mm:ss} [{1}] {2}{3}",
                        DateTime.Now,
                        level,
                        message,
                        Environment.NewLine);

                    File.AppendAllText(_logPath, line, Encoding.UTF8);
                }
                catch
                {
                    // Logging must never crash the background monitor.
                }
            }
        }

        private void RotateIfNeeded()
        {
            FileInfo info = new FileInfo(_logPath);
            if (!info.Exists || info.Length < MaxLogBytes)
            {
                return;
            }

            string backupPath = _logPath + ".old";
            if (File.Exists(backupPath))
            {
                File.Delete(backupPath);
            }

            File.Move(_logPath, backupPath);
        }
    }
}
