from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime, timezone
from enum import Enum


class SmtcState(Enum):
    UNAVAILABLE = "Unavailable"
    NOT_FOUND = "NotFound"
    PLAYING = "Playing"
    PAUSED = "Paused"
    STOPPED = "Stopped"
    OTHER = "Other"


@dataclass
class SmtcSnapshot:
    state: SmtcState = SmtcState.UNAVAILABLE
    source_app_id: str = ""
    track_title: str = ""
    track_artist: str = ""
    error_message: str = ""

    @property
    def display_track(self) -> str:
        artist = self.track_artist.strip()
        title = self.track_title.strip()
        if artist and title:
            return f"{artist} - {title}"
        return title


@dataclass
class WasapiSnapshot:
    is_available: bool = False
    spotify_process_detected: bool = False
    has_active_session: bool = False
    max_peak_level: float = 0.0
    active_session_count: int = 0
    error_message: str = ""


@dataclass
class WindowTitleSnapshot:
    spotify_running: bool = False
    appears_playing: bool = False
    raw_title: str = ""


@dataclass
class PlaybackSnapshot:
    utc_now: datetime = field(default_factory=lambda: datetime.now(timezone.utc))
    smtc: SmtcSnapshot = field(default_factory=SmtcSnapshot)
    wasapi: WasapiSnapshot = field(default_factory=WasapiSnapshot)
    window_title: WindowTitleSnapshot = field(default_factory=WindowTitleSnapshot)


@dataclass
class PlaybackDecision:
    should_keep_awake: bool = False
    is_actively_playing: bool = False
    in_grace_period: bool = False
    reason: str = ""
    current_track: str = ""
