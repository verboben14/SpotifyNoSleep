from __future__ import annotations

import ctypes
from ctypes import wintypes

from .logger import AppLogger

ES_CONTINUOUS = 0x80000000
ES_SYSTEM_REQUIRED = 0x00000001
ES_DISPLAY_REQUIRED = 0x00000002

POWER_REQUEST_CONTEXT_VERSION = 0
POWER_REQUEST_CONTEXT_SIMPLE_STRING = 0x00000001
POWER_REQUEST_DISPLAY_REQUIRED = 0
POWER_REQUEST_SYSTEM_REQUIRED = 1

INPUT_MOUSE = 0
MOUSEEVENTF_MOVE = 0x0001
IDLE_NUDGE_THRESHOLD_MS = 45000
INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value


class _REASON_CONTEXT_UNION(ctypes.Union):
    _fields_ = [("SimpleReasonString", wintypes.LPWSTR)]


class REASON_CONTEXT(ctypes.Structure):
    _fields_ = [
        ("Version", wintypes.ULONG),
        ("Flags", wintypes.DWORD),
        ("Reason", _REASON_CONTEXT_UNION),
    ]


class LASTINPUTINFO(ctypes.Structure):
    _fields_ = [
        ("cbSize", wintypes.UINT),
        ("dwTime", wintypes.DWORD),
    ]


class MOUSEINPUT(ctypes.Structure):
    _fields_ = [
        ("dx", wintypes.LONG),
        ("dy", wintypes.LONG),
        ("mouseData", wintypes.DWORD),
        ("dwFlags", wintypes.DWORD),
        ("time", wintypes.DWORD),
        ("dwExtraInfo", ctypes.c_size_t),
    ]


class KEYBDINPUT(ctypes.Structure):
    _fields_ = [
        ("wVk", wintypes.WORD),
        ("wScan", wintypes.WORD),
        ("dwFlags", wintypes.DWORD),
        ("time", wintypes.DWORD),
        ("dwExtraInfo", ctypes.c_size_t),
    ]


class HARDWAREINPUT(ctypes.Structure):
    _fields_ = [
        ("uMsg", wintypes.DWORD),
        ("wParamL", wintypes.WORD),
        ("wParamH", wintypes.WORD),
    ]


class _INPUT_UNION(ctypes.Union):
    _fields_ = [
        ("mi", MOUSEINPUT),
        ("ki", KEYBDINPUT),
        ("hi", HARDWAREINPUT),
    ]


class INPUT(ctypes.Structure):
    _fields_ = [
        ("type", wintypes.DWORD),
        ("u", _INPUT_UNION),
    ]


_kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
_kernel32.SetThreadExecutionState.argtypes = [wintypes.DWORD]
_kernel32.SetThreadExecutionState.restype = wintypes.DWORD
_kernel32.PowerCreateRequest.argtypes = [ctypes.POINTER(REASON_CONTEXT)]
_kernel32.PowerCreateRequest.restype = wintypes.HANDLE
_kernel32.PowerSetRequest.argtypes = [wintypes.HANDLE, ctypes.c_int]
_kernel32.PowerSetRequest.restype = wintypes.BOOL
_kernel32.PowerClearRequest.argtypes = [wintypes.HANDLE, ctypes.c_int]
_kernel32.PowerClearRequest.restype = wintypes.BOOL
_kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
_kernel32.CloseHandle.restype = wintypes.BOOL
_kernel32.GetTickCount.argtypes = []
_kernel32.GetTickCount.restype = wintypes.DWORD

_user32 = ctypes.WinDLL("user32", use_last_error=True)
_user32.GetLastInputInfo.argtypes = [ctypes.POINTER(LASTINPUTINFO)]
_user32.GetLastInputInfo.restype = wintypes.BOOL
_user32.SendInput.argtypes = [wintypes.UINT, ctypes.POINTER(INPUT), ctypes.c_int]
_user32.SendInput.restype = wintypes.UINT


class PowerManager:
    """Manages Windows kernel PowerRequests, SetThreadExecutionState, and optional input keepalive."""

    def __init__(self, logger: AppLogger | None = None) -> None:
        self._logger = logger
        self._reason_text = ctypes.create_unicode_buffer("Spotify is actively playing music")
        self._power_handle: int | None = self._create_power_request()
        self._system_held = False
        self._display_held = False
        self._is_awake_active = False
        self._closed = False

    @property
    def is_awake_active(self) -> bool:
        return self._is_awake_active

    @property
    def has_kernel_power_request(self) -> bool:
        return self._power_handle is not None

    def apply_state(
        self,
        keep_awake: bool,
        prevent_lock_screen: bool = True,
        strict_lock_prevention: bool = False,
    ) -> None:
        if self._closed:
            return

        if keep_awake:
            self._set_request(POWER_REQUEST_SYSTEM_REQUIRED, True, is_display=False)
            self._set_request(POWER_REQUEST_DISPLAY_REQUIRED, prevent_lock_screen, is_display=True)

            flags = ES_CONTINUOUS | ES_SYSTEM_REQUIRED
            if prevent_lock_screen:
                flags |= ES_DISPLAY_REQUIRED
            _kernel32.SetThreadExecutionState(flags)

            if prevent_lock_screen and strict_lock_prevention:
                self._nudge_input_if_user_idle()

            self._is_awake_active = True
        else:
            self.release_all()

    def release_all(self) -> None:
        self._set_request(POWER_REQUEST_DISPLAY_REQUIRED, False, is_display=True)
        self._set_request(POWER_REQUEST_SYSTEM_REQUIRED, False, is_display=False)
        _kernel32.SetThreadExecutionState(ES_CONTINUOUS)
        self._is_awake_active = False

    def close(self) -> None:
        if self._closed:
            return
        self._closed = True
        self.release_all()
        if self._power_handle is not None:
            try:
                _kernel32.CloseHandle(self._power_handle)
            except Exception:
                pass
            self._power_handle = None

    def __enter__(self) -> "PowerManager":
        return self

    def __exit__(self, *_exc) -> None:
        self.close()

    def _create_power_request(self) -> int | None:
        try:
            ctx = REASON_CONTEXT()
            ctx.Version = POWER_REQUEST_CONTEXT_VERSION
            ctx.Flags = POWER_REQUEST_CONTEXT_SIMPLE_STRING
            ctx.Reason.SimpleReasonString = ctypes.cast(self._reason_text, wintypes.LPWSTR)

            handle = _kernel32.PowerCreateRequest(ctypes.byref(ctx))
            if not handle or handle == INVALID_HANDLE_VALUE:
                return None
            return int(handle)
        except Exception as exc:
            if self._logger:
                self._logger.warn(f"PowerCreateRequest unavailable; using SetThreadExecutionState fallback: {exc}")
            return None

    def _set_request(self, request_type: int, desired: bool, is_display: bool) -> None:
        if self._power_handle is None:
            return
        currently_held = self._display_held if is_display else self._system_held
        if desired == currently_held:
            return

        try:
            if desired:
                if _kernel32.PowerSetRequest(self._power_handle, request_type):
                    if is_display:
                        self._display_held = True
                    else:
                        self._system_held = True
            else:
                if _kernel32.PowerClearRequest(self._power_handle, request_type):
                    if is_display:
                        self._display_held = False
                    else:
                        self._system_held = False
        except Exception:
            pass

    @staticmethod
    def get_user_idle_ms() -> int:
        info = LASTINPUTINFO()
        info.cbSize = ctypes.sizeof(LASTINPUTINFO)
        if not _user32.GetLastInputInfo(ctypes.byref(info)):
            return 0
        now_ticks = _kernel32.GetTickCount()
        return (now_ticks - info.dwTime) & 0xFFFFFFFF

    def _nudge_input_if_user_idle(self) -> None:
        if self.get_user_idle_ms() < IDLE_NUDGE_THRESHOLD_MS:
            return
        inp = INPUT()
        inp.type = INPUT_MOUSE
        inp.u.mi.dx = 0
        inp.u.mi.dy = 0
        inp.u.mi.mouseData = 0
        inp.u.mi.dwFlags = MOUSEEVENTF_MOVE
        inp.u.mi.time = 0
        inp.u.mi.dwExtraInfo = 0
        _user32.SendInput(1, ctypes.byref(inp), ctypes.sizeof(INPUT))
