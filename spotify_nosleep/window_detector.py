from __future__ import annotations

import ctypes
from ctypes import wintypes

from .models import WindowTitleSnapshot
from .win_processes import get_spotify_pids

_IDLE_TITLES = {
    "spotify",
    "spotify free",
    "spotify premium",
    "spotify web player",
    "drag",
}

_user32 = ctypes.WinDLL("user32", use_last_error=True)
_WNDENUMPROC = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)

_user32.EnumWindows.argtypes = [_WNDENUMPROC, wintypes.LPARAM]
_user32.EnumWindows.restype = wintypes.BOOL
_user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
_user32.GetWindowThreadProcessId.restype = wintypes.DWORD
_user32.GetWindowTextLengthW.argtypes = [wintypes.HWND]
_user32.GetWindowTextLengthW.restype = ctypes.c_int
_user32.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
_user32.GetWindowTextW.restype = ctypes.c_int
_user32.GetClassNameW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
_user32.GetClassNameW.restype = ctypes.c_int


class WindowTitleDetector:
    """Fallback detector inspecting Spotify's top-level Chromium window title."""

    def detect(self) -> WindowTitleSnapshot:
        snapshot = WindowTitleSnapshot()
        spotify_pids = get_spotify_pids()
        if not spotify_pids:
            return snapshot

        snapshot.spotify_running = True
        idle_candidate = ""
        playing_candidate = ""

        def _enum_callback(hwnd, _lparam):
            nonlocal idle_candidate, playing_candidate
            pid = wintypes.DWORD(0)
            _user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
            if int(pid.value) not in spotify_pids:
                return True

            length = _user32.GetWindowTextLengthW(hwnd)
            if length <= 0:
                return True

            buf = ctypes.create_unicode_buffer(length + 1)
            if _user32.GetWindowTextW(hwnd, buf, length + 1) <= 0:
                return True

            title = buf.value.strip()
            if not title:
                return True

            cls_buf = ctypes.create_unicode_buffer(64)
            _user32.GetClassNameW(hwnd, cls_buf, 64)
            if "chrome_widgetwin" not in cls_buf.value.lower():
                return True

            if title.lower() in _IDLE_TITLES:
                if not idle_candidate:
                    idle_candidate = title
            else:
                playing_candidate = title
                return False

            return True

        cb = _WNDENUMPROC(_enum_callback)
        _user32.EnumWindows(cb, 0)

        if playing_candidate:
            snapshot.appears_playing = True
            snapshot.raw_title = playing_candidate
        elif idle_candidate:
            snapshot.appears_playing = False
            snapshot.raw_title = idle_candidate

        return snapshot
