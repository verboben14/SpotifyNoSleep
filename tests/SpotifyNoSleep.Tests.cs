using System;
using System.Drawing;
using System.IO;
using SpotifyNoSleep;

namespace SpotifyNoSleep.Tests
{
    internal static class TestRunner
    {
        private static int _passed;
        private static int _failed;

        public static int Main()
        {
            Run("SmtcPlaying_KeepsAwake", TestSmtcPlayingKeepsAwake);
            Run("SmtcPaused_NoAudioPeak_ReleasesAfterGracePeriod", TestSmtcPausedReleasesAfterGracePeriod);
            Run("SmtcPaused_ButWasapiPeakActive_StillKeepsAwake", TestSmtcPausedWithWasapiPeakKeepsAwake);
            Run("SmtcDisabled_WasapiActiveWithPeak_KeepsAwake", TestSmtcDisabledWasapiActiveKeepsAwake);
            Run("SmtcDisabled_WasapiSilentAndWindowIdle_DoesNotKeepAwake", TestSmtcDisabledWasapiSilentIdleWindow);
            Run("SmtcDisabled_WasapiSilentMinimizedToTray_KeepsAwake", TestSmtcDisabledWasapiSilentMinimized);
            Run("SpotifyExits_ReleasesImmediatelyWithoutGracePeriod", TestSpotifyExitsReleasesImmediately);
            Run("WindowTitleFallback_WhenSmtcAndWasapiUnavailable", TestWindowTitleFallback);
            Run("GracePeriod_BridgesTrackTransitionGap", TestGracePeriodBridgesTrackTransition);
            Run("Reset_ClearsGracePeriodState", TestResetClearsGracePeriod);
            Run("AppConfig_SaveLoadAndNormalize", TestAppConfigSaveLoadAndNormalize);
            Run("TrayIconFactory_CreatesAllVisualStates", TestTrayIconFactory);
            Run("PowerManager_AcquireAndReleaseKernelPowerRequest", TestPowerManager);
            Run("LiveDetectors_RunCleanly", TestLiveDetectors);
            Run("InstallerExecutable_ContainsEmbeddedAppPayload", TestInstallerContainsPayload);

            Console.WriteLine();
            Console.WriteLine(string.Format("Tests finished: {0} passed, {1} failed.", _passed, _failed));
            return _failed == 0 ? 0 : 1;
        }

        private static void Run(string name, Action test)
        {
            try
            {
                test();
                _passed++;
                Console.WriteLine("[PASS] " + name);
            }
            catch (Exception ex)
            {
                _failed++;
                Console.WriteLine("[FAIL] " + name + " -> " + ex.Message);
            }
        }

        private static void AssertTrue(bool condition, string message)
        {
            if (!condition)
            {
                throw new Exception("Assertion failed (expected true): " + message);
            }
        }

        private static void AssertFalse(bool condition, string message)
        {
            if (condition)
            {
                throw new Exception("Assertion failed (expected false): " + message);
            }
        }

        private static void AssertEqual<T>(T expected, T actual, string message)
        {
            if (!object.Equals(expected, actual))
            {
                throw new Exception(string.Format(
                    "Assertion failed ({0}): expected <{1}>, actual <{2}>",
                    message, expected, actual));
            }
        }

        private static void TestSmtcPlayingKeepsAwake()
        {
            PlaybackDecisionEngine engine = new PlaybackDecisionEngine(10, 0.0001f);
            PlaybackSnapshot snap = new PlaybackSnapshot
            {
                UtcNow = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc),
                Smtc = new SmtcSnapshot
                {
                    State = SmtcState.Playing,
                    SourceAppId = "Spotify.exe",
                    TrackArtist = "Daft Punk",
                    TrackTitle = "Veridis Quo"
                },
                Wasapi = new WasapiSnapshot { IsAvailable = true, SpotifyProcessDetected = true, HasActiveSession = true, MaxPeakLevel = 0.0f }
            };

            PlaybackDecision decision = engine.Evaluate(snap);
            AssertTrue(decision.ShouldKeepAwake, "Should keep awake when SMTC is Playing even during silence");
            AssertTrue(decision.IsActivelyPlaying, "Should be actively playing");
            AssertFalse(decision.InGracePeriod, "Should not be in grace period");
            AssertEqual("Daft Punk - Veridis Quo", decision.CurrentTrack, "Track display formatting");
        }

        private static void TestSmtcPausedReleasesAfterGracePeriod()
        {
            PlaybackDecisionEngine engine = new PlaybackDecisionEngine(10, 0.0001f);
            DateTime t0 = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

            engine.Evaluate(new PlaybackSnapshot
            {
                UtcNow = t0,
                Smtc = new SmtcSnapshot { State = SmtcState.Playing, TrackArtist = "Artist", TrackTitle = "Song" },
                Wasapi = new WasapiSnapshot { IsAvailable = true, SpotifyProcessDetected = true, HasActiveSession = true, MaxPeakLevel = 0.25f }
            });

            PlaybackDecision d5 = engine.Evaluate(new PlaybackSnapshot
            {
                UtcNow = t0.AddSeconds(5),
                Smtc = new SmtcSnapshot { State = SmtcState.Paused },
                Wasapi = new WasapiSnapshot { IsAvailable = true, SpotifyProcessDetected = true, HasActiveSession = false, MaxPeakLevel = 0.0f }
            });
            AssertTrue(d5.ShouldKeepAwake, "Should stay awake during grace period");
            AssertTrue(d5.InGracePeriod, "Should report in grace period");

            PlaybackDecision d11 = engine.Evaluate(new PlaybackSnapshot
            {
                UtcNow = t0.AddSeconds(11),
                Smtc = new SmtcSnapshot { State = SmtcState.Paused },
                Wasapi = new WasapiSnapshot { IsAvailable = true, SpotifyProcessDetected = true, HasActiveSession = false, MaxPeakLevel = 0.0f }
            });
            AssertFalse(d11.ShouldKeepAwake, "Should release wake lock after grace period expires");
            AssertFalse(d11.InGracePeriod, "Grace period should be expired");
        }

        private static void TestSmtcPausedWithWasapiPeakKeepsAwake()
        {
            PlaybackDecisionEngine engine = new PlaybackDecisionEngine(10, 0.0001f);
            PlaybackDecision d = engine.Evaluate(new PlaybackSnapshot
            {
                UtcNow = DateTime.UtcNow,
                Smtc = new SmtcSnapshot { State = SmtcState.Paused },
                Wasapi = new WasapiSnapshot { IsAvailable = true, SpotifyProcessDetected = true, HasActiveSession = true, MaxPeakLevel = 0.12f }
            });

            AssertTrue(d.ShouldKeepAwake, "Audible WASAPI peak should override stale SMTC Paused");
            AssertTrue(d.IsActivelyPlaying, "Should be actively playing");
        }

        private static void TestSmtcDisabledWasapiActiveKeepsAwake()
        {
            PlaybackDecisionEngine engine = new PlaybackDecisionEngine(10, 0.0001f);
            PlaybackDecision d = engine.Evaluate(new PlaybackSnapshot
            {
                UtcNow = DateTime.UtcNow,
                Smtc = new SmtcSnapshot { State = SmtcState.NotFound },
                Wasapi = new WasapiSnapshot { IsAvailable = true, SpotifyProcessDetected = true, HasActiveSession = true, MaxPeakLevel = 0.08f }
            });

            AssertTrue(d.ShouldKeepAwake, "WASAPI audio peak should trigger awake mode when SMTC is disabled");
        }

        private static void TestSmtcDisabledWasapiSilentIdleWindow()
        {
            PlaybackDecisionEngine engine = new PlaybackDecisionEngine(10, 0.0001f);
            PlaybackDecision d = engine.Evaluate(new PlaybackSnapshot
            {
                UtcNow = DateTime.UtcNow,
                Smtc = new SmtcSnapshot { State = SmtcState.NotFound },
                Wasapi = new WasapiSnapshot { IsAvailable = true, SpotifyProcessDetected = true, HasActiveSession = true, MaxPeakLevel = 0.0f },
                WindowTitle = new WindowTitleSnapshot { SpotifyRunning = true, AppearsPlaying = false, RawTitle = "Spotify Premium" }
            });

            AssertFalse(d.ShouldKeepAwake, "Silent lingering WASAPI session with idle window title should not keep PC awake");
        }

        private static void TestSmtcDisabledWasapiSilentMinimized()
        {
            PlaybackDecisionEngine engine = new PlaybackDecisionEngine(10, 0.0001f);
            PlaybackDecision d = engine.Evaluate(new PlaybackSnapshot
            {
                UtcNow = DateTime.UtcNow,
                Smtc = new SmtcSnapshot { State = SmtcState.NotFound },
                Wasapi = new WasapiSnapshot { IsAvailable = true, SpotifyProcessDetected = true, HasActiveSession = true, MaxPeakLevel = 0.0f },
                WindowTitle = new WindowTitleSnapshot { SpotifyRunning = true, AppearsPlaying = false, RawTitle = string.Empty }
            });

            AssertTrue(d.ShouldKeepAwake, "Active WASAPI session when minimized (no visible title) should keep awake");
        }

        private static void TestSpotifyExitsReleasesImmediately()
        {
            PlaybackDecisionEngine engine = new PlaybackDecisionEngine(30, 0.0001f);
            DateTime t0 = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

            engine.Evaluate(new PlaybackSnapshot
            {
                UtcNow = t0,
                Smtc = new SmtcSnapshot { State = SmtcState.Playing },
                Wasapi = new WasapiSnapshot { IsAvailable = true, SpotifyProcessDetected = true, HasActiveSession = true, MaxPeakLevel = 0.3f }
            });

            PlaybackDecision d = engine.Evaluate(new PlaybackSnapshot
            {
                UtcNow = t0.AddSeconds(1),
                Smtc = new SmtcSnapshot { State = SmtcState.NotFound },
                Wasapi = new WasapiSnapshot { IsAvailable = true, SpotifyProcessDetected = false, HasActiveSession = false, MaxPeakLevel = 0f },
                WindowTitle = new WindowTitleSnapshot { SpotifyRunning = false }
            });

            AssertFalse(d.ShouldKeepAwake, "Should release immediately when Spotify process exits");
            AssertFalse(d.InGracePeriod, "No grace period needed once Spotify exits");
        }

        private static void TestWindowTitleFallback()
        {
            PlaybackDecisionEngine engine = new PlaybackDecisionEngine(10, 0.0001f);
            PlaybackDecision d = engine.Evaluate(new PlaybackSnapshot
            {
                UtcNow = DateTime.UtcNow,
                Smtc = new SmtcSnapshot { State = SmtcState.Unavailable },
                Wasapi = new WasapiSnapshot { IsAvailable = false, SpotifyProcessDetected = true },
                WindowTitle = new WindowTitleSnapshot { SpotifyRunning = true, AppearsPlaying = true, RawTitle = "Boards of Canada - Roygbiv" }
            });

            AssertTrue(d.ShouldKeepAwake, "Window title fallback should keep awake");
            AssertEqual("Boards of Canada - Roygbiv", d.CurrentTrack, "Track extracted from window title");
        }

        private static void TestGracePeriodBridgesTrackTransition()
        {
            PlaybackDecisionEngine engine = new PlaybackDecisionEngine(10, 0.0001f);
            DateTime t0 = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

            PlaybackDecision d0 = engine.Evaluate(new PlaybackSnapshot
            {
                UtcNow = t0,
                Smtc = new SmtcSnapshot { State = SmtcState.Playing, TrackTitle = "Track 1" },
                Wasapi = new WasapiSnapshot { IsAvailable = true, SpotifyProcessDetected = true }
            });
            AssertTrue(d0.ShouldKeepAwake, "Track 1 playing");

            PlaybackDecision dGap = engine.Evaluate(new PlaybackSnapshot
            {
                UtcNow = t0.AddSeconds(2),
                Smtc = new SmtcSnapshot { State = SmtcState.Other },
                Wasapi = new WasapiSnapshot { IsAvailable = true, SpotifyProcessDetected = true }
            });
            AssertTrue(dGap.ShouldKeepAwake, "Grace period bridges track change");
            AssertTrue(dGap.InGracePeriod, "In grace period during gap");

            PlaybackDecision d2 = engine.Evaluate(new PlaybackSnapshot
            {
                UtcNow = t0.AddSeconds(4),
                Smtc = new SmtcSnapshot { State = SmtcState.Playing, TrackTitle = "Track 2" },
                Wasapi = new WasapiSnapshot { IsAvailable = true, SpotifyProcessDetected = true }
            });
            AssertTrue(d2.ShouldKeepAwake, "Track 2 playing");
            AssertFalse(d2.InGracePeriod, "No longer in grace period");
        }

        private static void TestResetClearsGracePeriod()
        {
            PlaybackDecisionEngine engine = new PlaybackDecisionEngine(60, 0.0001f);
            DateTime t0 = new DateTime(2026, 11, 20, 12, 0, 0, DateTimeKind.Utc);

            engine.Evaluate(new PlaybackSnapshot
            {
                UtcNow = t0,
                Smtc = new SmtcSnapshot { State = SmtcState.Playing }
            });

            engine.Reset();

            PlaybackDecision d = engine.Evaluate(new PlaybackSnapshot
            {
                UtcNow = t0.AddSeconds(1),
                Smtc = new SmtcSnapshot { State = SmtcState.Paused },
                Wasapi = new WasapiSnapshot { IsAvailable = true, SpotifyProcessDetected = true }
            });
            AssertFalse(d.ShouldKeepAwake, "Reset should clear prior grace period memory");
        }

        private static void TestAppConfigSaveLoadAndNormalize()
        {
            string tempFile = Path.Combine(Path.GetTempPath(), "SpotifyNoSleep_Test_" + Guid.NewGuid().ToString("N") + ".json");
            try
            {
                AppConfig cfg = new AppConfig();
                cfg.PreventLockScreen = false;
                cfg.StrictLockPrevention = true;
                cfg.PollIntervalMs = 100;
                cfg.GracePeriodSeconds = 999;
                cfg.ShowNotifications = true;
                cfg.Save(tempFile);

                AppConfig reloaded = AppConfig.Load(tempFile);
                AssertFalse(reloaded.PreventLockScreen, "PreventLockScreen roundtrip");
                AssertTrue(reloaded.StrictLockPrevention, "StrictLockPrevention roundtrip");
                AssertEqual(500, reloaded.PollIntervalMs, "PollIntervalMs clamped to 500");
                AssertEqual(300, reloaded.GracePeriodSeconds, "GracePeriodSeconds clamped to 300");
                AssertTrue(reloaded.ShowNotifications, "ShowNotifications roundtrip");
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        private static void TestTrayIconFactory()
        {
            TrayVisualState[] states = new TrayVisualState[]
            {
                TrayVisualState.Idle,
                TrayVisualState.Active,
                TrayVisualState.Paused
            };

            for (int i = 0; i < states.Length; i++)
            {
                using (Icon icon = TrayIconFactory.CreateIcon(states[i]))
                {
                    AssertTrue(icon != null, "Icon created for " + states[i]);
                    AssertEqual(32, icon.Width, "Icon width");
                    AssertEqual(32, icon.Height, "Icon height");
                }
            }
        }

        private static void TestPowerManager()
        {
            using (PowerManager pm = new PowerManager(null))
            {
                AssertTrue(pm.HasKernelPowerRequest, "Kernel PowerCreateRequest should succeed");
                pm.ApplyState(true, true, false);
                AssertTrue(pm.IsAwakeActive, "Awake should be active");
                pm.ApplyState(false, true, false);
                AssertFalse(pm.IsAwakeActive, "Awake should be released");
            }
        }

        private static void TestLiveDetectors()
        {
            WasapiSnapshot wasapi = new WasapiDetector().Detect();
            AssertTrue(wasapi.IsAvailable, "WASAPI detector should be available: " + wasapi.ErrorMessage);

            SmtcSnapshot smtc = new SmtcDetector().Detect();
            if (smtc.State == SmtcState.Unavailable)
            {
                AssertTrue(
                    smtc.ErrorMessage.IndexOf("0x80070005", StringComparison.OrdinalIgnoreCase) >= 0,
                    "Unexpected SMTC failure: " + smtc.ErrorMessage);
            }

            WindowTitleSnapshot win = new WindowTitleDetector().Detect();
            AssertTrue(win != null, "WindowTitleDetector returned snapshot");
        }

        private static void TestInstallerContainsPayload()
        {
            string testDir = AppDomain.CurrentDomain.BaseDirectory;
            string appExePath = Path.Combine(testDir, "SpotifyNoSleep.exe");
            string setupExePath = Path.Combine(testDir, "SpotifyNoSleep-Setup.exe");

            AssertTrue(File.Exists(appExePath), "dist/SpotifyNoSleep.exe should exist");
            AssertTrue(File.Exists(setupExePath), "dist/SpotifyNoSleep-Setup.exe should exist");

            byte[] expectedBytes = File.ReadAllBytes(appExePath);
            System.Reflection.Assembly setupAsm = System.Reflection.Assembly.ReflectionOnlyLoadFrom(setupExePath);
            using (Stream payload = setupAsm.GetManifestResourceStream("SpotifyNoSleep.exe"))
            {
                AssertTrue(payload != null, "Installer must embed SpotifyNoSleep.exe resource");
                AssertEqual(expectedBytes.Length, (int)payload.Length, "Embedded payload length must match SpotifyNoSleep.exe");
            }
        }
    }
}
