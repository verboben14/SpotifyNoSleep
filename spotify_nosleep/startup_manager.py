from __future__ import annotations

import sys
import winreg
from pathlib import Path

RUN_KEY_PATH = r"Software\Microsoft\Windows\CurrentVersion\Run"
APP_VALUE_NAME = "SpotifyNoSleep"


def resolve_pythonw_path() -> Path:
    """Locate pythonw.exe alongside the active Python interpreter for windowless startup."""
    exe = Path(sys.executable)
    if exe.name.lower() == "pythonw.exe":
        return exe
    sibling = exe.with_name("pythonw.exe")
    if sibling.exists():
        return sibling
    return exe


def build_startup_command(entry_script: Path | str) -> str:
    pythonw = resolve_pythonw_path()
    script = Path(entry_script).resolve()
    return f'"{pythonw}" "{script}"'


def is_startup_enabled() -> bool:
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, RUN_KEY_PATH, 0, winreg.KEY_READ) as key:
            value, _ = winreg.QueryValueEx(key, APP_VALUE_NAME)
            return bool(str(value).strip())
    except OSError:
        return False


def get_registered_command() -> str:
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, RUN_KEY_PATH, 0, winreg.KEY_READ) as key:
            value, _ = winreg.QueryValueEx(key, APP_VALUE_NAME)
            return str(value)
    except OSError:
        return ""


def set_startup_enabled(enabled: bool, entry_script: Path | str) -> bool:
    try:
        with winreg.OpenKey(winreg.HKEY_CURRENT_USER, RUN_KEY_PATH, 0, winreg.KEY_SET_VALUE) as key:
            if enabled:
                command = build_startup_command(entry_script)
                winreg.SetValueEx(key, APP_VALUE_NAME, 0, winreg.REG_SZ, command)
            else:
                try:
                    winreg.DeleteValue(key, APP_VALUE_NAME)
                except FileNotFoundError:
                    pass
        return True
    except OSError:
        return False
