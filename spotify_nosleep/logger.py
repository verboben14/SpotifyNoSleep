from __future__ import annotations

import threading
from datetime import datetime
from pathlib import Path

from .config import AppConfig


class AppLogger:
    MAX_BYTES = 512 * 1024

    def __init__(self, log_path: Path | str | None = None, enabled: bool = True) -> None:
        self.log_path = Path(log_path) if log_path is not None else self.get_default_log_path()
        self.enabled = enabled
        self._lock = threading.Lock()

    @staticmethod
    def get_default_log_path() -> Path:
        return AppConfig.get_default_data_dir() / "spotifynosleep.log"

    def info(self, message: str) -> None:
        self._write("INFO", message)

    def warn(self, message: str) -> None:
        self._write("WARN", message)

    def error(self, message: str, exc: BaseException | None = None) -> None:
        full = f"{message}: {exc}" if exc is not None else message
        self._write("ERROR", full)

    def _write(self, level: str, message: str) -> None:
        if not self.enabled:
            return
        with self._lock:
            try:
                self.log_path.parent.mkdir(parents=True, exist_ok=True)
                self._rotate_if_needed()
                timestamp = datetime.now().strftime("%Y-%m-%d %H:%M:%S")
                with self.log_path.open("a", encoding="utf-8") as fh:
                    fh.write(f"{timestamp} [{level}] {message}\n")
            except Exception:
                pass

    def _rotate_if_needed(self) -> None:
        if not self.log_path.exists():
            return
        if self.log_path.stat().st_size < self.MAX_BYTES:
            return
        backup = self.log_path.with_suffix(self.log_path.suffix + ".old")
        if backup.exists():
            backup.unlink()
        self.log_path.replace(backup)
