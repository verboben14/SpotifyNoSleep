from __future__ import annotations

import json
import os
from dataclasses import asdict, dataclass
from pathlib import Path


@dataclass
class AppConfig:
    prevent_lock_screen: bool = True
    strict_lock_prevention: bool = False
    poll_interval_ms: int = 2000
    grace_period_seconds: int = 10
    show_notifications: bool = False
    enable_logging: bool = True

    def normalize(self) -> None:
        self.prevent_lock_screen = bool(self.prevent_lock_screen)
        self.strict_lock_prevention = bool(self.strict_lock_prevention)
        self.show_notifications = bool(self.show_notifications)
        self.enable_logging = bool(self.enable_logging)

        try:
            self.poll_interval_ms = int(self.poll_interval_ms)
        except (TypeError, ValueError):
            self.poll_interval_ms = 2000
        self.poll_interval_ms = max(500, min(30000, self.poll_interval_ms))

        try:
            self.grace_period_seconds = int(self.grace_period_seconds)
        except (TypeError, ValueError):
            self.grace_period_seconds = 10
        self.grace_period_seconds = max(0, min(300, self.grace_period_seconds))

    @staticmethod
    def get_default_data_dir() -> Path:
        appdata = os.environ.get("APPDATA")
        if appdata:
            return Path(appdata) / "SpotifyNoSleep"
        return Path.home() / ".spotify_no_sleep"

    @staticmethod
    def get_default_config_path() -> Path:
        return AppConfig.get_default_data_dir() / "config.json"

    @classmethod
    def load(cls, path: Path | str | None = None) -> "AppConfig":
        target = Path(path) if path is not None else cls.get_default_config_path()
        config = cls()
        try:
            if not target.exists():
                config.save(target)
                return config
            raw = json.loads(target.read_text(encoding="utf-8"))
            if isinstance(raw, dict):
                valid_keys = {
                    "prevent_lock_screen",
                    "strict_lock_prevention",
                    "poll_interval_ms",
                    "grace_period_seconds",
                    "show_notifications",
                    "enable_logging",
                }
                for key in valid_keys:
                    if key in raw:
                        setattr(config, key, raw[key])
        except Exception:
            # Fall back cleanly to defaults if config is corrupted or unreadable.
            pass
        config.normalize()
        return config

    def save(self, path: Path | str | None = None) -> None:
        self.normalize()
        target = Path(path) if path is not None else self.get_default_config_path()
        try:
            target.parent.mkdir(parents=True, exist_ok=True)
            target.write_text(json.dumps(asdict(self), indent=2), encoding="utf-8")
        except Exception:
            pass
