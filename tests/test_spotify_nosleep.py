from __future__ import annotations

import tempfile
import unittest
from datetime import datetime, timedelta, timezone
from pathlib import Path

from spotify_nosleep.config import AppConfig
from spotify_nosleep.decision_engine import PlaybackDecisionEngine
from spotify_nosleep.models import (
    PlaybackSnapshot,
    SmtcSnapshot,
    SmtcState,
    WasapiSnapshot,
    WindowTitleSnapshot,
)
from spotify_nosleep.power_manager import PowerManager
from spotify_nosleep.smtc_detector import SmtcDetector
from spotify_nosleep.tray_icons import TrayVisualState, create_tray_hicon, destroy_hicon, render_icon_bgra
from spotify_nosleep.wasapi_detector import WasapiDetector
from spotify_nosleep.window_detector import WindowTitleDetector


class PlaybackDecisionEngineTests(unittest.TestCase):
    def test_smtc_playing_keeps_awake_even_during_silence(self) -> None:
        engine = PlaybackDecisionEngine(grace_period_seconds=10)
        t0 = datetime(2026, 11, 20, 12, 0, 0, tzinfo=timezone.utc)
        decision = engine.evaluate(
            PlaybackSnapshot(
                utc_now=t0,
                smtc=SmtcSnapshot(
                    state=SmtcState.PLAYING,
                    source_app_id="Spotify.exe",
                    track_artist="Daft Punk",
                    track_title="Veridis Quo",
                ),
                wasapi=WasapiSnapshot(
                    is_available=True,
                    spotify_process_detected=True,
                    has_active_session=True,
                    max_peak_level=0.0,
                ),
            )
        )
        self.assertTrue(decision.should_keep_awake)
        self.assertTrue(decision.is_actively_playing)
        self.assertFalse(decision.in_grace_period)
        self.assertEqual(decision.current_track, "Daft Punk - Veridis Quo")

    def test_smtc_paused_releases_after_grace_period(self) -> None:
        engine = PlaybackDecisionEngine(grace_period_seconds=10)
        t0 = datetime(2026, 11, 20, 12, 0, 0, tzinfo=timezone.utc)

        engine.evaluate(
            PlaybackSnapshot(
                utc_now=t0,
                smtc=SmtcSnapshot(state=SmtcState.PLAYING, track_artist="Artist", track_title="Song"),
                wasapi=WasapiSnapshot(
                    is_available=True,
                    spotify_process_detected=True,
                    has_active_session=True,
                    max_peak_level=0.25,
                ),
            )
        )

        # 5 seconds later: paused -> still within grace period
        d5 = engine.evaluate(
            PlaybackSnapshot(
                utc_now=t0 + timedelta(seconds=5),
                smtc=SmtcSnapshot(state=SmtcState.PAUSED),
                wasapi=WasapiSnapshot(
                    is_available=True,
                    spotify_process_detected=True,
                    has_active_session=False,
                    max_peak_level=0.0,
                ),
            )
        )
        self.assertTrue(d5.should_keep_awake)
        self.assertTrue(d5.in_grace_period)

        # 11 seconds later: grace period expired -> release
        d11 = engine.evaluate(
            PlaybackSnapshot(
                utc_now=t0 + timedelta(seconds=11),
                smtc=SmtcSnapshot(state=SmtcState.PAUSED),
                wasapi=WasapiSnapshot(
                    is_available=True,
                    spotify_process_detected=True,
                    has_active_session=False,
                    max_peak_level=0.0,
                ),
            )
        )
        self.assertFalse(d11.should_keep_awake)
        self.assertFalse(d11.in_grace_period)

    def test_audible_wasapi_peak_overrides_stale_smtc_paused(self) -> None:
        engine = PlaybackDecisionEngine(grace_period_seconds=10)
        decision = engine.evaluate(
            PlaybackSnapshot(
                smtc=SmtcSnapshot(state=SmtcState.PAUSED),
                wasapi=WasapiSnapshot(
                    is_available=True,
                    spotify_process_detected=True,
                    has_active_session=True,
                    max_peak_level=0.12,
                ),
            )
        )
        self.assertTrue(decision.should_keep_awake)
        self.assertTrue(decision.is_actively_playing)

    def test_smtc_disabled_wasapi_peak_keeps_awake(self) -> None:
        engine = PlaybackDecisionEngine(grace_period_seconds=10)
        decision = engine.evaluate(
            PlaybackSnapshot(
                smtc=SmtcSnapshot(state=SmtcState.NOT_FOUND),
                wasapi=WasapiSnapshot(
                    is_available=True,
                    spotify_process_detected=True,
                    has_active_session=True,
                    max_peak_level=0.08,
                ),
            )
        )
        self.assertTrue(decision.should_keep_awake)

    def test_smtc_disabled_silent_wasapi_with_idle_window_releases(self) -> None:
        engine = PlaybackDecisionEngine(grace_period_seconds=10)
        decision = engine.evaluate(
            PlaybackSnapshot(
                smtc=SmtcSnapshot(state=SmtcState.NOT_FOUND),
                wasapi=WasapiSnapshot(
                    is_available=True,
                    spotify_process_detected=True,
                    has_active_session=True,
                    max_peak_level=0.0,
                ),
                window_title=WindowTitleSnapshot(
                    spotify_running=True,
                    appears_playing=False,
                    raw_title="Spotify Premium",
                ),
            )
        )
        self.assertFalse(decision.should_keep_awake)

    def test_smtc_disabled_silent_wasapi_minimized_to_tray_keeps_awake(self) -> None:
        engine = PlaybackDecisionEngine(grace_period_seconds=10)
        decision = engine.evaluate(
            PlaybackSnapshot(
                smtc=SmtcSnapshot(state=SmtcState.NOT_FOUND),
                wasapi=WasapiSnapshot(
                    is_available=True,
                    spotify_process_detected=True,
                    has_active_session=True,
                    max_peak_level=0.0,
                ),
                window_title=WindowTitleSnapshot(
                    spotify_running=True,
                    appears_playing=False,
                    raw_title="",
                ),
            )
        )
        self.assertTrue(decision.should_keep_awake)

    def test_spotify_exit_releases_immediately_without_grace_period(self) -> None:
        engine = PlaybackDecisionEngine(grace_period_seconds=30)
        t0 = datetime(2026, 11, 20, 12, 0, 0, tzinfo=timezone.utc)

        engine.evaluate(
            PlaybackSnapshot(
                utc_now=t0,
                smtc=SmtcSnapshot(state=SmtcState.PLAYING),
                wasapi=WasapiSnapshot(
                    is_available=True,
                    spotify_process_detected=True,
                    has_active_session=True,
                    max_peak_level=0.3,
                ),
            )
        )

        d = engine.evaluate(
            PlaybackSnapshot(
                utc_now=t0 + timedelta(seconds=1),
                smtc=SmtcSnapshot(state=SmtcState.NOT_FOUND),
                wasapi=WasapiSnapshot(is_available=True, spotify_process_detected=False),
                window_title=WindowTitleSnapshot(spotify_running=False),
            )
        )
        self.assertFalse(d.should_keep_awake)
        self.assertFalse(d.in_grace_period)

    def test_window_title_fallback(self) -> None:
        engine = PlaybackDecisionEngine(grace_period_seconds=10)
        d = engine.evaluate(
            PlaybackSnapshot(
                smtc=SmtcSnapshot(state=SmtcState.UNAVAILABLE),
                wasapi=WasapiSnapshot(is_available=False, spotify_process_detected=True),
                window_title=WindowTitleSnapshot(
                    spotify_running=True,
                    appears_playing=True,
                    raw_title="Boards of Canada - Roygbiv",
                ),
            )
        )
        self.assertTrue(d.should_keep_awake)
        self.assertEqual(d.current_track, "Boards of Canada - Roygbiv")

    def test_grace_period_bridges_track_change(self) -> None:
        engine = PlaybackDecisionEngine(grace_period_seconds=10)
        t0 = datetime(2026, 11, 20, 12, 0, 0, tzinfo=timezone.utc)

        d0 = engine.evaluate(
            PlaybackSnapshot(
                utc_now=t0,
                smtc=SmtcSnapshot(state=SmtcState.PLAYING, track_title="Track 1"),
                wasapi=WasapiSnapshot(is_available=True, spotify_process_detected=True),
            )
        )
        self.assertTrue(d0.should_keep_awake)

        d_gap = engine.evaluate(
            PlaybackSnapshot(
                utc_now=t0 + timedelta(seconds=2),
                smtc=SmtcSnapshot(state=SmtcState.OTHER),
                wasapi=WasapiSnapshot(is_available=True, spotify_process_detected=True),
            )
        )
        self.assertTrue(d_gap.should_keep_awake)
        self.assertTrue(d_gap.in_grace_period)

        d2 = engine.evaluate(
            PlaybackSnapshot(
                utc_now=t0 + timedelta(seconds=4),
                smtc=SmtcSnapshot(state=SmtcState.PLAYING, track_title="Track 2"),
                wasapi=WasapiSnapshot(is_available=True, spotify_process_detected=True),
            )
        )
        self.assertTrue(d2.should_keep_awake)
        self.assertFalse(d2.in_grace_period)

    def test_reset_clears_grace_period(self) -> None:
        engine = PlaybackDecisionEngine(grace_period_seconds=60)
        t0 = datetime(2026, 11, 20, 12, 0, 0, tzinfo=timezone.utc)

        engine.evaluate(
            PlaybackSnapshot(
                utc_now=t0,
                smtc=SmtcSnapshot(state=SmtcState.PLAYING),
            )
        )
        engine.reset()
        d = engine.evaluate(
            PlaybackSnapshot(
                utc_now=t0 + timedelta(seconds=1),
                smtc=SmtcSnapshot(state=SmtcState.PAUSED),
                wasapi=WasapiSnapshot(is_available=True, spotify_process_detected=True),
            )
        )
        self.assertFalse(d.should_keep_awake)


class ConfigAndWindowsIntegrationTests(unittest.TestCase):
    def test_config_save_load_and_clamp(self) -> None:
        with tempfile.TemporaryDirectory() as tmpdir:
            cfg_path = Path(tmpdir) / "config.json"
            cfg = AppConfig(
                prevent_lock_screen=False,
                strict_lock_prevention=True,
                poll_interval_ms=100,
                grace_period_seconds=999,
                show_notifications=True,
            )
            cfg.save(cfg_path)

            reloaded = AppConfig.load(cfg_path)
            self.assertFalse(reloaded.prevent_lock_screen)
            self.assertTrue(reloaded.strict_lock_prevention)
            self.assertEqual(reloaded.poll_interval_ms, 500)
            self.assertEqual(reloaded.grace_period_seconds, 300)
            self.assertTrue(reloaded.show_notifications)

    def test_tray_icons_render_and_create_hicon(self) -> None:
        for state in (TrayVisualState.IDLE, TrayVisualState.ACTIVE, TrayVisualState.PAUSED):
            pixels = render_icon_bgra(state, size=32)
            self.assertEqual(len(pixels), 32 * 32 * 4)
            self.assertGreater(sum(pixels), 0)
            hicon = create_tray_hicon(state, size=32)
            self.assertGreater(hicon, 0)
            destroy_hicon(hicon)

    def test_power_manager_acquire_and_release(self) -> None:
        with PowerManager() as pm:
            self.assertTrue(pm.has_kernel_power_request)
            pm.apply_state(keep_awake=True, prevent_lock_screen=True, strict_lock_prevention=False)
            self.assertTrue(pm.is_awake_active)
            pm.apply_state(keep_awake=False)
            self.assertFalse(pm.is_awake_active)

    def test_live_detectors_do_not_crash(self) -> None:
        smtc = SmtcDetector().detect()
        if smtc.state == SmtcState.UNAVAILABLE:
            # Restricted/sandboxed tokens may receive E_ACCESSDENIED (0x80070005) from NPSMSvc.
            self.assertIn("0x80070005", smtc.error_message, f"Unexpected SMTC failure: {smtc.error_message}")

        wasapi = WasapiDetector().detect()
        self.assertTrue(wasapi.is_available, f"WASAPI failed: {wasapi.error_message}")

        window = WindowTitleDetector().detect()
        self.assertIsInstance(window.spotify_running, bool)


if __name__ == "__main__":
    unittest.main(verbosity=2)
