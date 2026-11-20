from __future__ import annotations

import argparse
from datetime import datetime, timezone
from pathlib import Path

from . import __version__
from .config import AppConfig
from .decision_engine import PlaybackDecisionEngine
from .logger import AppLogger
from .models import PlaybackSnapshot
from .power_manager import PowerManager
from .smtc_detector import SmtcDetector
from .startup_manager import get_registered_command, is_startup_enabled, set_startup_enabled
from .tray_app import SpotifyNoSleepTrayApp
from .wasapi_detector import WasapiDetector
from .window_detector import WindowTitleDetector


def run_diagnostics() -> int:
    config_path = AppConfig.get_default_config_path()
    config = AppConfig.load(config_path)

    smtc = SmtcDetector().detect()
    wasapi = WasapiDetector().detect()
    window = WindowTitleDetector().detect()

    snapshot = PlaybackSnapshot(
        utc_now=datetime.now(timezone.utc),
        smtc=smtc,
        wasapi=wasapi,
        window_title=window,
    )
    engine = PlaybackDecisionEngine(grace_period_seconds=config.grace_period_seconds)
    decision = engine.evaluate(snapshot)

    with PowerManager() as pm:
        kernel_req_ok = pm.has_kernel_power_request

    startup_status = (
        f"Enabled ({get_registered_command()})"
        if is_startup_enabled()
        else "Disabled"
    )

    print(f"=== SpotifyNoSleep Diagnostics (v{__version__}) ===")
    print(f"Config Path      : {config_path}")
    print(f"Log Path         : {AppLogger.get_default_log_path()}")
    print(f"Windows Startup  : {startup_status}")
    print(f"Kernel PowerReq  : {'Available' if kernel_req_ok else 'Fallback (SetThreadExecutionState)'}")
    print(f"Prevent Lock     : {config.prevent_lock_screen} (Strict Keepalive: {config.strict_lock_prevention})")
    print()
    print("[1] SMTC (Windows Media Control)")
    print(f"    State        : {smtc.state.value}")
    print(f"    SourceAppId  : {smtc.source_app_id or '(none)'}")
    print(f"    Track        : {smtc.display_track or '(none)'}")
    if smtc.error_message:
        print(f"    Error        : {smtc.error_message}")
    print()
    print("[2] WASAPI (Core Audio Sessions)")
    print(f"    Available    : {wasapi.is_available}")
    print(f"    Spotify PID  : {wasapi.spotify_process_detected}")
    print(f"    Active Sess  : {wasapi.has_active_session} (count={wasapi.active_session_count})")
    print(f"    Peak Level   : {wasapi.max_peak_level:.5f}")
    if wasapi.error_message:
        print(f"    Error        : {wasapi.error_message}")
    print()
    print("[3] Window Title Fallback")
    print(f"    Running      : {window.spotify_running}")
    print(f"    Playing?     : {window.appears_playing}")
    print(f"    Title        : {window.raw_title or '(none)'}")
    print()
    print("[Decision]")
    print(f"    Keep Awake   : {decision.should_keep_awake}")
    print(f"    Reason       : {decision.reason}")
    if decision.current_track:
        print(f"    Track        : {decision.current_track}")

    return 0


def main(entry_script: Path, argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(
        prog="SpotifyNoSleep",
        description="Prevent Windows sleep & lock screen whenever Spotify is playing music.",
    )
    parser.add_argument("--diagnose", "-d", action="store_true", help="Inspect live Spotify detection & power readiness")
    parser.add_argument("--enable-startup", action="store_true", help="Register auto-start on Windows logon")
    parser.add_argument("--disable-startup", action="store_true", help="Remove auto-start on Windows logon")
    parser.add_argument("--version", "-v", action="store_true", help="Print version and exit")

    args = parser.parse_args(argv)

    if args.version:
        print(f"SpotifyNoSleep v{__version__}")
        return 0

    if args.enable_startup:
        ok = set_startup_enabled(True, entry_script)
        print(f"Enabled Windows startup: {get_registered_command()}" if ok else "Failed to enable Windows startup.")
        return 0 if ok else 1

    if args.disable_startup:
        ok = set_startup_enabled(False, entry_script)
        print("Disabled Windows startup." if ok else "Failed to disable Windows startup.")
        return 0 if ok else 1

    if args.diagnose:
        return run_diagnostics()

    app = SpotifyNoSleepTrayApp(entry_script=entry_script)
    return app.run()
