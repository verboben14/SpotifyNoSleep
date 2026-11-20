using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace SpotifyNoSleep
{
    public sealed class WindowTitleDetector : IWindowTitleDetector
    {
        private static readonly HashSet<string> IdleTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "Spotify",
            "Spotify Free",
            "Spotify Premium",
            "Spotify Web Player",
            "Drag"
        };

        public WindowTitleSnapshot Detect()
        {
            WindowTitleSnapshot snapshot = new WindowTitleSnapshot();
            HashSet<uint> spotifyPids = new HashSet<uint>();

            try
            {
                Process[] processes = Process.GetProcessesByName("Spotify");
                for (int i = 0; i < processes.Length; i++)
                {
                    try
                    {
                        spotifyPids.Add((uint)processes[i].Id);
                    }
                    finally
                    {
                        processes[i].Dispose();
                    }
                }
            }
            catch
            {
                return snapshot;
            }

            if (spotifyPids.Count == 0)
            {
                return snapshot;
            }

            snapshot.SpotifyRunning = true;

            string idleCandidate = string.Empty;
            string playingCandidate = string.Empty;

            EnumWindows(delegate(IntPtr hwnd, IntPtr lParam)
            {
                uint pid;
                GetWindowThreadProcessId(hwnd, out pid);
                if (!spotifyPids.Contains(pid))
                {
                    return true;
                }

                int length = GetWindowTextLength(hwnd);
                if (length <= 0)
                {
                    return true;
                }

                StringBuilder sb = new StringBuilder(length + 1);
                if (GetWindowText(hwnd, sb, sb.Capacity) <= 0)
                {
                    return true;
                }

                string title = sb.ToString().Trim();
                if (string.IsNullOrEmpty(title))
                {
                    return true;
                }

                StringBuilder classBuilder = new StringBuilder(64);
                GetClassName(hwnd, classBuilder, classBuilder.Capacity);
                string className = classBuilder.ToString();
                if (className.IndexOf("Chrome_WidgetWin", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return true;
                }

                if (IdleTitles.Contains(title))
                {
                    if (string.IsNullOrEmpty(idleCandidate))
                    {
                        idleCandidate = title;
                    }
                }
                else
                {
                    playingCandidate = title;
                    return false;
                }

                return true;
            }, IntPtr.Zero);

            if (!string.IsNullOrEmpty(playingCandidate))
            {
                snapshot.AppearsPlaying = true;
                snapshot.RawTitle = playingCandidate;
            }
            else if (!string.IsNullOrEmpty(idleCandidate))
            {
                snapshot.AppearsPlaying = false;
                snapshot.RawTitle = idleCandidate;
            }

            return snapshot;
        }

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowTextLength(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hwnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hwnd, StringBuilder lpClassName, int nMaxCount);
    }
}
