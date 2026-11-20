from __future__ import annotations

import math
from datetime import datetime

from .models import (
    PlaybackDecision,
    PlaybackSnapshot,
    SmtcSnapshot,
    SmtcState,
    WasapiSnapshot,
    WindowTitleSnapshot,
)


class PlaybackDecisionEngine:
    """Combines SMTC, WASAPI, and WindowTitle snapshots with track-change grace hysteresis."""

    def __init__(self, grace_period_seconds: int = 10, audio_peak_threshold: float = 0.0001) -> None:
        self.grace_period_seconds = max(0, int(grace_period_seconds))
        self.audio_peak_threshold = max(0.0, float(audio_peak_threshold))
        self._last_confirmed_playing_utc: datetime | None = None
        self._last_track: str = ""

    def reset(self) -> None:
        self._last_confirmed_playing_utc = None
        self._last_track = ""

    def evaluate(self, snapshot: PlaybackSnapshot) -> PlaybackDecision:
        smtc = snapshot.smtc or SmtcSnapshot()
        wasapi = snapshot.wasapi or WasapiSnapshot()
        window = snapshot.window_title or WindowTitleSnapshot()

        track = smtc.display_track
        if not track and window.appears_playing and window.raw_title.strip():
            track = window.raw_title.strip()

        actively_playing = False
        reason = ""

        # 1. Authoritative SMTC "Playing" state (Desktop, Store/MSIX, works even when muted/quiet)
        if smtc.state == SmtcState.PLAYING:
            actively_playing = True
            reason = "SMTC reports Spotify is playing"
        # 2. Audible WASAPI audio output from Spotify.exe (overrides stale SMTC or disabled overlay)
        elif wasapi.is_available and wasapi.has_active_session and wasapi.max_peak_level > self.audio_peak_threshold:
            actively_playing = True
            reason = "WASAPI detects active Spotify audio output"
        # 3. Explicit SMTC Paused/Stopped with no audible WASAPI peak
        elif smtc.state in (SmtcState.PAUSED, SmtcState.STOPPED):
            actively_playing = False
            reason = (
                "SMTC reports Spotify is paused"
                if smtc.state == SmtcState.PAUSED
                else "SMTC reports Spotify is stopped"
            )
        # 4. SMTC unavailable or no Spotify SMTC session (e.g., overlay disabled in Spotify settings)
        else:
            if wasapi.is_available and wasapi.has_active_session:
                window_explicitly_idle = (
                    window.spotify_running
                    and not window.appears_playing
                    and bool(window.raw_title.strip())
                )
                if not window_explicitly_idle:
                    actively_playing = True
                    reason = "WASAPI reports active Spotify audio session"
                else:
                    actively_playing = False
                    reason = "Spotify audio session is silent and window is idle"
            elif window.spotify_running and window.appears_playing:
                actively_playing = True
                reason = "Spotify window title indicates active playback"

        if actively_playing:
            self._last_confirmed_playing_utc = snapshot.utc_now
            if track:
                self._last_track = track

            return PlaybackDecision(
                should_keep_awake=True,
                is_actively_playing=True,
                in_grace_period=False,
                reason=reason,
                current_track=track or self._last_track,
            )

        spotify_present = (
            smtc.state not in (SmtcState.UNAVAILABLE, SmtcState.NOT_FOUND)
            or wasapi.spotify_process_detected
            or window.spotify_running
        )

        if not spotify_present:
            self._last_confirmed_playing_utc = None
            self._last_track = ""
            return PlaybackDecision(
                should_keep_awake=False,
                is_actively_playing=False,
                in_grace_period=False,
                reason="Spotify is not running",
                current_track="",
            )

        if self._last_confirmed_playing_utc is not None and self.grace_period_seconds > 0:
            elapsed = (snapshot.utc_now - self._last_confirmed_playing_utc).total_seconds()
            if 0 <= elapsed < self.grace_period_seconds:
                remaining = int(math.ceil(self.grace_period_seconds - elapsed))
                return PlaybackDecision(
                    should_keep_awake=True,
                    is_actively_playing=False,
                    in_grace_period=True,
                    reason=f"Grace period ({remaining}s remaining) after pause/track change",
                    current_track=self._last_track,
                )

        self._last_confirmed_playing_utc = None
        self._last_track = ""
        return PlaybackDecision(
            should_keep_awake=False,
            is_actively_playing=False,
            in_grace_period=False,
            reason=reason or "Spotify is idle",
            current_track="",
        )
