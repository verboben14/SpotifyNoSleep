using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SpotifyNoSleep
{
    public sealed class TrayApplicationContext : ApplicationContext
    {
        private readonly AppConfig _config;
        private readonly string _configPath;
        private readonly Logger _logger;
        private readonly ISmtcDetector _smtcDetector;
        private readonly IWasapiDetector _wasapiDetector;
        private readonly IWindowTitleDetector _windowTitleDetector;
        private readonly PlaybackDecisionEngine _engine;
        private readonly PowerManager _powerManager;

        private readonly Control _uiInvoker;
        private readonly NotifyIcon _notifyIcon;
        private readonly ContextMenuStrip _contextMenu;
        private readonly ToolStripMenuItem _statusMenuItem;
        private readonly ToolStripMenuItem _trackMenuItem;
        private readonly ToolStripMenuItem _pauseResumeMenuItem;
        private readonly ToolStripMenuItem _preventLockMenuItem;
        private readonly ToolStripMenuItem _strictLockMenuItem;
        private readonly ToolStripMenuItem _startupMenuItem;
        private readonly ToolStripMenuItem _notificationsMenuItem;

        private readonly Icon _idleIcon;
        private readonly Icon _activeIcon;
        private readonly Icon _pausedIcon;
        private readonly Timer _pollTimer;

        private bool _monitoringPaused;
        private bool _pollInProgress;
        private bool _lastAwakeState;
        private string _lastTrackLogged;
        private bool _disposed;

        public TrayApplicationContext(
            AppConfig config,
            string configPath,
            Logger logger,
            ISmtcDetector smtcDetector,
            IWasapiDetector wasapiDetector,
            IWindowTitleDetector windowTitleDetector)
        {
            _config = config;
            _configPath = configPath;
            _logger = logger;
            _smtcDetector = smtcDetector;
            _wasapiDetector = wasapiDetector;
            _windowTitleDetector = windowTitleDetector;
            _engine = new PlaybackDecisionEngine(config.GracePeriodSeconds, 0.0001f);
            _powerManager = new PowerManager(logger);
            _lastTrackLogged = string.Empty;

            _uiInvoker = new Control();
            IntPtr handle = _uiInvoker.Handle;
            if (handle == IntPtr.Zero)
            {
                throw new InvalidOperationException("Failed to initialize UI synchronization handle.");
            }

            _idleIcon = TrayIconFactory.CreateIcon(TrayVisualState.Idle);
            _activeIcon = TrayIconFactory.CreateIcon(TrayVisualState.Active);
            _pausedIcon = TrayIconFactory.CreateIcon(TrayVisualState.Paused);

            _contextMenu = new ContextMenuStrip();

            _statusMenuItem = new ToolStripMenuItem("Status: Monitoring (Spotify idle)");
            _statusMenuItem.Enabled = false;

            _trackMenuItem = new ToolStripMenuItem("Track: None");
            _trackMenuItem.Enabled = false;
            _trackMenuItem.Visible = false;

            _pauseResumeMenuItem = new ToolStripMenuItem("Pause Monitoring", null, OnPauseResumeClicked);

            _preventLockMenuItem = new ToolStripMenuItem("Prevent Lock Screen & Display Off", null, OnPreventLockClicked);
            _preventLockMenuItem.CheckOnClick = false;
            _preventLockMenuItem.Checked = _config.PreventLockScreen;

            _strictLockMenuItem = new ToolStripMenuItem("Strict Lock Prevention (Input Keepalive)", null, OnStrictLockClicked);
            _strictLockMenuItem.CheckOnClick = false;
            _strictLockMenuItem.Checked = _config.StrictLockPrevention;
            _strictLockMenuItem.Enabled = _config.PreventLockScreen;

            _startupMenuItem = new ToolStripMenuItem("Start Automatically with Windows", null, OnStartupClicked);
            _startupMenuItem.CheckOnClick = false;
            _startupMenuItem.Checked = StartupManager.IsEnabled();

            _notificationsMenuItem = new ToolStripMenuItem("Show Tray Notifications", null, OnNotificationsClicked);
            _notificationsMenuItem.CheckOnClick = false;
            _notificationsMenuItem.Checked = _config.ShowNotifications;

            ToolStripMenuItem openLogMenuItem = new ToolStripMenuItem("Open Log File", null, OnOpenLogClicked);
            ToolStripMenuItem exitMenuItem = new ToolStripMenuItem("Exit", null, OnExitClicked);

            _contextMenu.Items.Add(_statusMenuItem);
            _contextMenu.Items.Add(_trackMenuItem);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add(_pauseResumeMenuItem);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add(_preventLockMenuItem);
            _contextMenu.Items.Add(_strictLockMenuItem);
            _contextMenu.Items.Add(_startupMenuItem);
            _contextMenu.Items.Add(_notificationsMenuItem);
            _contextMenu.Items.Add(new ToolStripSeparator());
            _contextMenu.Items.Add(openLogMenuItem);
            _contextMenu.Items.Add(exitMenuItem);

            _notifyIcon = new NotifyIcon();
            _notifyIcon.Icon = _idleIcon;
            _notifyIcon.Text = TruncateTooltip("SpotifyNoSleep - Monitoring");
            _notifyIcon.ContextMenuStrip = _contextMenu;
            _notifyIcon.Visible = true;
            _notifyIcon.DoubleClick += OnPauseResumeClicked;

            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionEnding += OnSessionEnding;

            _pollTimer = new Timer();
            _pollTimer.Interval = _config.PollIntervalMs;
            _pollTimer.Tick += OnPollTimerTick;
            _pollTimer.Start();

            _logger.Info(string.Format(
                "SpotifyNoSleep v{0} started (PollInterval={1}ms, GracePeriod={2}s, PreventLock={3}, StrictLock={4}, KernelPowerReq={5}, Startup={6})",
                Program.AppVersion,
                _config.PollIntervalMs,
                _config.GracePeriodSeconds,
                _config.PreventLockScreen,
                _config.StrictLockPrevention,
                _powerManager.HasKernelPowerRequest,
                StartupManager.IsEnabled()));

            TriggerPoll();
        }

        private void OnPollTimerTick(object sender, EventArgs e)
        {
            TriggerPoll();
        }

        private void TriggerPoll()
        {
            if (_disposed || _pollInProgress)
            {
                return;
            }

            if (_monitoringPaused)
            {
                _powerManager.ReleaseAll();
                UpdateUiForPaused();
                return;
            }

            _pollInProgress = true;
            Task.Factory.StartNew(delegate
            {
                PlaybackSnapshot snapshot = new PlaybackSnapshot();
                snapshot.UtcNow = DateTime.UtcNow;

                try
                {
                    snapshot.Smtc = _smtcDetector.Detect();
                }
                catch (Exception ex)
                {
                    _logger.Error("SMTC detection error", ex);
                }

                try
                {
                    snapshot.Wasapi = _wasapiDetector.Detect();
                }
                catch (Exception ex)
                {
                    _logger.Error("WASAPI detection error", ex);
                }

                try
                {
                    snapshot.WindowTitle = _windowTitleDetector.Detect();
                }
                catch (Exception ex)
                {
                    _logger.Error("Window title detection error", ex);
                }

                return snapshot;
            }).ContinueWith(delegate(Task<PlaybackSnapshot> t)
            {
                if (_disposed)
                {
                    return;
                }

                PlaybackSnapshot result = t.IsFaulted ? new PlaybackSnapshot() : t.Result;

                try
                {
                    if (_uiInvoker.IsHandleCreated && !_uiInvoker.IsDisposed)
                    {
                        _uiInvoker.BeginInvoke((MethodInvoker)delegate
                        {
                            HandlePollResultOnUiThread(result);
                        });
                    }
                    else
                    {
                        _pollInProgress = false;
                    }
                }
                catch
                {
                    _pollInProgress = false;
                }
            });
        }

        private void HandlePollResultOnUiThread(PlaybackSnapshot snapshot)
        {
            _pollInProgress = false;
            if (_disposed)
            {
                return;
            }

            if (_monitoringPaused)
            {
                _powerManager.ReleaseAll();
                UpdateUiForPaused();
                return;
            }

            PlaybackDecision decision = _engine.Evaluate(snapshot);
            _powerManager.ApplyState(
                decision.ShouldKeepAwake,
                _config.PreventLockScreen,
                _config.StrictLockPrevention);

            if (decision.ShouldKeepAwake != _lastAwakeState)
            {
                _lastAwakeState = decision.ShouldKeepAwake;
                if (decision.ShouldKeepAwake)
                {
                    _logger.Info(string.Format(
                        "Awake mode ENGAGED ({0}). Track: {1}",
                        decision.Reason,
                        string.IsNullOrEmpty(decision.CurrentTrack) ? "(unknown)" : decision.CurrentTrack));
                    if (_config.ShowNotifications)
                    {
                        ShowBalloon("SpotifyNoSleep Active", "Keeping PC awake while Spotify plays.");
                    }
                }
                else
                {
                    _logger.Info(string.Format("Awake mode RELEASED ({0}).", decision.Reason));
                    if (_config.ShowNotifications)
                    {
                        ShowBalloon("SpotifyNoSleep Idle", "Spotify stopped. Normal sleep & lock restored.");
                    }
                }
            }
            else if (decision.ShouldKeepAwake
                && !string.IsNullOrEmpty(decision.CurrentTrack)
                && !string.Equals(_lastTrackLogged, decision.CurrentTrack, StringComparison.Ordinal))
            {
                _lastTrackLogged = decision.CurrentTrack;
                _logger.Info("Now playing: " + decision.CurrentTrack);
            }

            UpdateUiForDecision(decision);
        }

        private void UpdateUiForDecision(PlaybackDecision decision)
        {
            if (decision.ShouldKeepAwake)
            {
                _notifyIcon.Icon = _activeIcon;
                string modeLabel = _config.PreventLockScreen
                    ? "Keeping PC & screen awake"
                    : "Keeping PC awake (sleep only)";

                _statusMenuItem.Text = decision.InGracePeriod
                    ? "Status: Active (grace period)"
                    : "Status: Active - " + modeLabel;

                if (!string.IsNullOrEmpty(decision.CurrentTrack))
                {
                    _trackMenuItem.Text = "Playing: " + TruncateMenuText(decision.CurrentTrack, 52);
                    _trackMenuItem.Visible = true;
                    _notifyIcon.Text = TruncateTooltip("SpotifyNoSleep: Active\n" + decision.CurrentTrack);
                }
                else
                {
                    _trackMenuItem.Visible = false;
                    _notifyIcon.Text = TruncateTooltip("SpotifyNoSleep: Active (Spotify playing)");
                }
            }
            else
            {
                _notifyIcon.Icon = _idleIcon;
                _statusMenuItem.Text = "Status: Monitoring (" + decision.Reason + ")";
                _trackMenuItem.Visible = false;
                _notifyIcon.Text = TruncateTooltip("SpotifyNoSleep: Monitoring (Spotify idle)");
            }
        }

        private void UpdateUiForPaused()
        {
            _notifyIcon.Icon = _pausedIcon;
            _statusMenuItem.Text = "Status: Paused by user";
            _trackMenuItem.Visible = false;
            _notifyIcon.Text = TruncateTooltip("SpotifyNoSleep: Monitoring paused");
        }

        private void OnPauseResumeClicked(object sender, EventArgs e)
        {
            _monitoringPaused = !_monitoringPaused;
            if (_monitoringPaused)
            {
                _engine.Reset();
                _powerManager.ReleaseAll();
                _lastAwakeState = false;
                _pauseResumeMenuItem.Text = "Resume Monitoring";
                UpdateUiForPaused();
                _logger.Info("Monitoring paused by user.");
            }
            else
            {
                _pauseResumeMenuItem.Text = "Pause Monitoring";
                _logger.Info("Monitoring resumed by user.");
                TriggerPoll();
            }
        }

        private void OnPreventLockClicked(object sender, EventArgs e)
        {
            _config.PreventLockScreen = !_config.PreventLockScreen;
            _preventLockMenuItem.Checked = _config.PreventLockScreen;
            _strictLockMenuItem.Enabled = _config.PreventLockScreen;
            _config.Save(_configPath);
            _logger.Info("PreventLockScreen set to " + _config.PreventLockScreen);
            TriggerPoll();
        }

        private void OnStrictLockClicked(object sender, EventArgs e)
        {
            _config.StrictLockPrevention = !_config.StrictLockPrevention;
            _strictLockMenuItem.Checked = _config.StrictLockPrevention;
            _config.Save(_configPath);
            _logger.Info("StrictLockPrevention set to " + _config.StrictLockPrevention);
            TriggerPoll();
        }

        private void OnStartupClicked(object sender, EventArgs e)
        {
            bool target = !StartupManager.IsEnabled();
            if (StartupManager.SetEnabled(target, Application.ExecutablePath))
            {
                _startupMenuItem.Checked = target;
                _logger.Info("Start with Windows set to " + target);
            }
            else
            {
                _logger.Warn("Failed to update Windows startup registry entry.");
            }
        }

        private void OnNotificationsClicked(object sender, EventArgs e)
        {
            _config.ShowNotifications = !_config.ShowNotifications;
            _notificationsMenuItem.Checked = _config.ShowNotifications;
            _config.Save(_configPath);
        }

        private void OnOpenLogClicked(object sender, EventArgs e)
        {
            try
            {
                if (!File.Exists(_logger.LogPath))
                {
                    _logger.Info("Log opened by user.");
                }
                Process.Start(new ProcessStartInfo(_logger.LogPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                _logger.Error("Could not open log file", ex);
            }
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode == PowerModes.Suspend)
            {
                _logger.Info("System suspending; releasing power requests.");
                _engine.Reset();
                _powerManager.ReleaseAll();
                _lastAwakeState = false;
            }
            else if (e.Mode == PowerModes.Resume)
            {
                _logger.Info("System resumed; restarting playback detection.");
                _engine.Reset();
                TriggerPoll();
            }
        }

        private void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            _logger.Info("Windows session ending; releasing power requests.");
            _powerManager.ReleaseAll();
        }

        private void OnExitClicked(object sender, EventArgs e)
        {
            _logger.Info("SpotifyNoSleep exiting at user request.");
            ExitThread();
        }

        private void ShowBalloon(string title, string message)
        {
            try
            {
                _notifyIcon.ShowBalloonTip(2500, title, message, ToolTipIcon.Info);
            }
            catch
            {
            }
        }

        private static string TruncateTooltip(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "SpotifyNoSleep";
            }
            return text.Length <= 63 ? text : text.Substring(0, 60) + "...";
        }

        private static string TruncateMenuText(string text, int maxLength)
        {
            if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            {
                return text;
            }
            return text.Substring(0, maxLength - 3) + "...";
        }

        protected override void Dispose(bool disposing)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (disposing)
            {
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                SystemEvents.SessionEnding -= OnSessionEnding;

                if (_pollTimer != null)
                {
                    _pollTimer.Stop();
                    _pollTimer.Dispose();
                }

                if (_powerManager != null)
                {
                    _powerManager.Dispose();
                }

                if (_notifyIcon != null)
                {
                    _notifyIcon.Visible = false;
                    _notifyIcon.Dispose();
                }

                if (_contextMenu != null)
                {
                    _contextMenu.Dispose();
                }

                if (_idleIcon != null)
                {
                    _idleIcon.Dispose();
                }

                if (_activeIcon != null)
                {
                    _activeIcon.Dispose();
                }

                if (_pausedIcon != null)
                {
                    _pausedIcon.Dispose();
                }

                if (_uiInvoker != null)
                {
                    _uiInvoker.Dispose();
                }
            }

            base.Dispose(disposing);
        }
    }
}
