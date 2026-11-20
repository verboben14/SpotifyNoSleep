using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SpotifyNoSleep
{
    public static class Program
    {
        public const string AppVersion = "1.0.0";
        private const string SingleInstanceMutexName = @"Local\SpotifyNoSleep_SingleInstance_Mutex";
        private const int AttachParentProcess = -1;

        [STAThread]
        public static int Main(string[] args)
        {
            if (args != null && args.Length > 0)
            {
                return HandleCliMode(args);
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, SingleInstanceMutexName, out createdNew))
            {
                if (!createdNew)
                {
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                string configPath = AppConfig.GetDefaultConfigPath();
                AppConfig config = AppConfig.Load(configPath);
                Logger logger = new Logger(Logger.GetDefaultLogPath(), config.EnableLogging);

                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    logger.Error("Unhandled UI thread exception", e.Exception);
                };

                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e)
                {
                    logger.Error("Unhandled domain exception", e.ExceptionObject as Exception);
                };

                using (TrayApplicationContext context = new TrayApplicationContext(
                    config,
                    configPath,
                    logger,
                    new SmtcDetector(),
                    new WasapiDetector(),
                    new WindowTitleDetector()))
                {
                    Application.Run(context);
                }
            }

            return 0;
        }

        private static int HandleCliMode(string[] args)
        {
            AttachParentConsoleIfAvailable();

            string command = args[0].Trim().ToLowerInvariant();
            if (command == "--version" || command == "-v")
            {
                Console.WriteLine("SpotifyNoSleep v" + AppVersion);
                return 0;
            }

            if (command == "--enable-startup")
            {
                string exePath = Application.ExecutablePath;
                bool ok = StartupManager.SetEnabled(true, exePath);
                Console.WriteLine(ok
                    ? "Enabled Windows startup: " + exePath
                    : "Failed to enable Windows startup.");
                return ok ? 0 : 1;
            }

            if (command == "--disable-startup")
            {
                bool ok = StartupManager.SetEnabled(false, Application.ExecutablePath);
                Console.WriteLine(ok
                    ? "Disabled Windows startup."
                    : "Failed to disable Windows startup.");
                return ok ? 0 : 1;
            }

            if (command == "--diagnose" || command == "-d")
            {
                return RunDiagnostics();
            }

            Console.WriteLine("SpotifyNoSleep v" + AppVersion);
            Console.WriteLine("Usage:");
            Console.WriteLine("  SpotifyNoSleep.exe                   Run system tray background monitor");
            Console.WriteLine("  SpotifyNoSleep.exe --diagnose        Inspect live Spotify detection & power state");
            Console.WriteLine("  SpotifyNoSleep.exe --enable-startup  Register auto-start on Windows logon");
            Console.WriteLine("  SpotifyNoSleep.exe --disable-startup Remove auto-start on Windows logon");
            Console.WriteLine("  SpotifyNoSleep.exe --version         Print version");
            return 0;
        }

        private static int RunDiagnostics()
        {
            string configPath = AppConfig.GetDefaultConfigPath();
            AppConfig config = AppConfig.Load(configPath);
            SmtcDetector smtc = new SmtcDetector();
            WasapiDetector wasapi = new WasapiDetector();
            WindowTitleDetector windowTitle = new WindowTitleDetector();

            PlaybackSnapshot snapshot = new PlaybackSnapshot();
            snapshot.UtcNow = DateTime.UtcNow;
            snapshot.Smtc = smtc.Detect();
            snapshot.Wasapi = wasapi.Detect();
            snapshot.WindowTitle = windowTitle.Detect();

            PlaybackDecisionEngine engine = new PlaybackDecisionEngine(config.GracePeriodSeconds, 0.0001f);
            PlaybackDecision decision = engine.Evaluate(snapshot);

            bool kernelPowerOk;
            using (PowerManager pm = new PowerManager(null))
            {
                kernelPowerOk = pm.HasKernelPowerRequest;
            }

            Console.WriteLine("=== SpotifyNoSleep Diagnostics (v" + AppVersion + ") ===");
            Console.WriteLine("Config Path     : " + configPath);
            Console.WriteLine("Log Path        : " + Logger.GetDefaultLogPath());
            Console.WriteLine("Windows Startup : " + (StartupManager.IsEnabled() ? "Enabled (" + StartupManager.GetRegisteredCommand() + ")" : "Disabled"));
            Console.WriteLine("Kernel PowerReq : " + (kernelPowerOk ? "Available" : "Fallback (SetThreadExecutionState)"));
            Console.WriteLine("Prevent Lock    : " + config.PreventLockScreen + " (Strict Input Keepalive: " + config.StrictLockPrevention + ")");
            Console.WriteLine();
            Console.WriteLine("[1] SMTC (Windows Media Control)");
            Console.WriteLine("    State       : " + snapshot.Smtc.State);
            Console.WriteLine("    SourceAppId : " + (string.IsNullOrEmpty(snapshot.Smtc.SourceAppId) ? "(none)" : snapshot.Smtc.SourceAppId));
            Console.WriteLine("    Track       : " + (string.IsNullOrEmpty(snapshot.Smtc.GetDisplayTrack()) ? "(none)" : snapshot.Smtc.GetDisplayTrack()));
            if (!string.IsNullOrEmpty(snapshot.Smtc.ErrorMessage))
            {
                Console.WriteLine("    Error       : " + snapshot.Smtc.ErrorMessage);
            }
            Console.WriteLine();
            Console.WriteLine("[2] WASAPI (Core Audio Sessions)");
            Console.WriteLine("    Available   : " + snapshot.Wasapi.IsAvailable);
            Console.WriteLine("    Spotify PID : " + snapshot.Wasapi.SpotifyProcessDetected);
            Console.WriteLine("    Active Sess : " + snapshot.Wasapi.HasActiveSession + " (count=" + snapshot.Wasapi.ActiveSessionCount + ")");
            Console.WriteLine("    Peak Level  : " + snapshot.Wasapi.MaxPeakLevel.ToString("F5"));
            if (!string.IsNullOrEmpty(snapshot.Wasapi.ErrorMessage))
            {
                Console.WriteLine("    Error       : " + snapshot.Wasapi.ErrorMessage);
            }
            Console.WriteLine();
            Console.WriteLine("[3] Window Title Fallback");
            Console.WriteLine("    Running     : " + snapshot.WindowTitle.SpotifyRunning);
            Console.WriteLine("    Playing?    : " + snapshot.WindowTitle.AppearsPlaying);
            Console.WriteLine("    Title       : " + (string.IsNullOrEmpty(snapshot.WindowTitle.RawTitle) ? "(none)" : snapshot.WindowTitle.RawTitle));
            Console.WriteLine();
            Console.WriteLine("[Decision]");
            Console.WriteLine("    Keep Awake  : " + decision.ShouldKeepAwake);
            Console.WriteLine("    Reason      : " + decision.Reason);
            if (!string.IsNullOrEmpty(decision.CurrentTrack))
            {
                Console.WriteLine("    Track       : " + decision.CurrentTrack);
            }

            return 0;
        }

        private static void AttachParentConsoleIfAvailable()
        {
            if (!Console.IsOutputRedirected)
            {
                AttachConsole(AttachParentProcess);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AttachConsole(int dwProcessId);
    }
}
