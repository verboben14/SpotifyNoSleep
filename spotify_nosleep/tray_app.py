from __future__ import annotations

import ctypes
import os
import threading
from ctypes import wintypes
from datetime import datetime, timezone
from pathlib import Path

from . import __version__
from .com_utils import GUID
from .config import AppConfig
from .decision_engine import PlaybackDecisionEngine
from .logger import AppLogger
from .models import PlaybackDecision, PlaybackSnapshot
from .power_manager import PowerManager
from .smtc_detector import SmtcDetector
from .startup_manager import is_startup_enabled, set_startup_enabled
from .tray_icons import TrayVisualState, create_tray_hicon, destroy_hicon
from .wasapi_detector import WasapiDetector
from .window_detector import WindowTitleDetector

SINGLE_INSTANCE_MUTEX_NAME = r"Local\SpotifyNoSleep_SingleInstance_Mutex"
ERROR_ALREADY_EXISTS = 183

WM_NULL = 0x0000
WM_DESTROY = 0x0002
WM_QUERYENDSESSION = 0x0011
WM_ENDSESSION = 0x0016
WM_CLOSE = 0x0010
WM_COMMAND = 0x0111
WM_TIMER = 0x0113
WM_POWERBROADCAST = 0x0218
WM_LBUTTONDBLCLK = 0x0203
WM_RBUTTONUP = 0x0205
WM_CONTEXTMENU = 0x007B
WM_USER = 0x0400

WM_TRAYICON = WM_USER + 20
WM_POLL_COMPLETE = WM_USER + 21

PBT_APMSUSPEND = 0x0004
PBT_APMRESUMEAUTOMATIC = 0x0012

NIM_ADD = 0x00000000
NIM_MODIFY = 0x00000001
NIM_DELETE = 0x00000002

NIF_MESSAGE = 0x00000001
NIF_ICON = 0x00000002
NIF_TIP = 0x00000004
NIF_INFO = 0x00000010
NIIF_INFO = 0x00000001

MF_STRING = 0x00000000
MF_SEPARATOR = 0x00000800
MF_GRAYED = 0x00000001
MF_CHECKED = 0x00000008
MF_UNCHECKED = 0x00000000

TPM_LEFTALIGN = 0x0000
TPM_RIGHTBUTTON = 0x0002
TPM_RETURNCMD = 0x0100

ID_STATUS = 1001
ID_TRACK = 1002
ID_PAUSE_RESUME = 1003
ID_PREVENT_LOCK = 1004
ID_STRICT_LOCK = 1005
ID_STARTUP = 1006
ID_NOTIFICATIONS = 1007
ID_OPEN_LOG = 1008
ID_EXIT = 1009

POLL_TIMER_ID = 1

LRESULT = ctypes.c_ssize_t
WPARAM = ctypes.c_size_t
LPARAM = ctypes.c_ssize_t
WNDPROC = ctypes.WINFUNCTYPE(LRESULT, wintypes.HWND, wintypes.UINT, WPARAM, LPARAM)


class WNDCLASSEXW(ctypes.Structure):
    _fields_ = [
        ("cbSize", wintypes.UINT),
        ("style", wintypes.UINT),
        ("lpfnWndProc", WNDPROC),
        ("cbClsExtra", ctypes.c_int),
        ("cbWndExtra", ctypes.c_int),
        ("hInstance", wintypes.HINSTANCE),
        ("hIcon", wintypes.HICON),
        ("hCursor", wintypes.HANDLE),
        ("hbrBackground", wintypes.HBRUSH),
        ("lpszMenuName", wintypes.LPCWSTR),
        ("lpszClassName", wintypes.LPCWSTR),
        ("hIconSm", wintypes.HICON),
    ]


class NOTIFYICONDATAW(ctypes.Structure):
    _fields_ = [
        ("cbSize", wintypes.DWORD),
        ("hWnd", wintypes.HWND),
        ("uID", wintypes.UINT),
        ("uFlags", wintypes.UINT),
        ("uCallbackMessage", wintypes.UINT),
        ("hIcon", wintypes.HICON),
        ("szTip", wintypes.WCHAR * 128),
        ("dwState", wintypes.DWORD),
        ("dwStateMask", wintypes.DWORD),
        ("szInfo", wintypes.WCHAR * 256),
        ("uTimeoutOrVersion", wintypes.UINT),
        ("szInfoTitle", wintypes.WCHAR * 64),
        ("dwInfoFlags", wintypes.DWORD),
        ("guidItem", GUID),
        ("hBalloonIcon", wintypes.HICON),
    ]


_kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
_kernel32.CreateMutexW.argtypes = [ctypes.c_void_p, wintypes.BOOL, wintypes.LPCWSTR]
_kernel32.CreateMutexW.restype = wintypes.HANDLE
_kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
_kernel32.CloseHandle.restype = wintypes.BOOL
_kernel32.GetModuleHandleW.argtypes = [wintypes.LPCWSTR]
_kernel32.GetModuleHandleW.restype = wintypes.HMODULE

_user32 = ctypes.WinDLL("user32", use_last_error=True)
_user32.RegisterClassExW.argtypes = [ctypes.POINTER(WNDCLASSEXW)]
_user32.RegisterClassExW.restype = wintypes.ATOM
_user32.UnregisterClassW.argtypes = [wintypes.LPCWSTR, wintypes.HINSTANCE]
_user32.UnregisterClassW.restype = wintypes.BOOL
_user32.CreateWindowExW.argtypes = [
    wintypes.DWORD, wintypes.LPCWSTR, wintypes.LPCWSTR, wintypes.DWORD,
    ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int,
    wintypes.HWND, wintypes.HMENU, wintypes.HINSTANCE, ctypes.c_void_p,
]
_user32.CreateWindowExW.restype = wintypes.HWND
_user32.DestroyWindow.argtypes = [wintypes.HWND]
_user32.DestroyWindow.restype = wintypes.BOOL
_user32.DefWindowProcW.argtypes = [wintypes.HWND, wintypes.UINT, WPARAM, LPARAM]
_user32.DefWindowProcW.restype = LRESULT
_user32.RegisterWindowMessageW.argtypes = [wintypes.LPCWSTR]
_user32.RegisterWindowMessageW.restype = wintypes.UINT
_user32.GetMessageW.argtypes = [ctypes.POINTER(wintypes.MSG), wintypes.HWND, wintypes.UINT, wintypes.UINT]
_user32.GetMessageW.restype = wintypes.BOOL
_user32.TranslateMessage.argtypes = [ctypes.POINTER(wintypes.MSG)]
_user32.TranslateMessage.restype = wintypes.BOOL
_user32.DispatchMessageW.argtypes = [ctypes.POINTER(wintypes.MSG)]
_user32.DispatchMessageW.restype = LRESULT
_user32.PostMessageW.argtypes = [wintypes.HWND, wintypes.UINT, WPARAM, LPARAM]
_user32.PostMessageW.restype = wintypes.BOOL
_user32.PostQuitMessage.argtypes = [ctypes.c_int]
_user32.PostQuitMessage.restype = None
_user32.SetTimer.argtypes = [wintypes.HWND, ctypes.c_size_t, wintypes.UINT, ctypes.c_void_p]
_user32.SetTimer.restype = ctypes.c_size_t
_user32.KillTimer.argtypes = [wintypes.HWND, ctypes.c_size_t]
_user32.KillTimer.restype = wintypes.BOOL
_user32.CreatePopupMenu.argtypes = []
_user32.CreatePopupMenu.restype = wintypes.HMENU
_user32.AppendMenuW.argtypes = [wintypes.HMENU, wintypes.UINT, ctypes.c_size_t, wintypes.LPCWSTR]
_user32.AppendMenuW.restype = wintypes.BOOL
_user32.TrackPopupMenu.argtypes = [
    wintypes.HMENU, wintypes.UINT, ctypes.c_int, ctypes.c_int,
    ctypes.c_int, wintypes.HWND, ctypes.c_void_p,
]
_user32.TrackPopupMenu.restype = wintypes.BOOL
_user32.DestroyMenu.argtypes = [wintypes.HMENU]
_user32.DestroyMenu.restype = wintypes.BOOL
_user32.GetCursorPos.argtypes = [ctypes.POINTER(wintypes.POINT)]
_user32.GetCursorPos.restype = wintypes.BOOL
_user32.SetForegroundWindow.argtypes = [wintypes.HWND]
_user32.SetForegroundWindow.restype = wintypes.BOOL

_shell32 = ctypes.WinDLL("shell32", use_last_error=True)
_shell32.Shell_NotifyIconW.argtypes = [wintypes.DWORD, ctypes.POINTER(NOTIFYICONDATAW)]
_shell32.Shell_NotifyIconW.restype = wintypes.BOOL


class SpotifyNoSleepTrayApp:
    """Native Win32 system tray monitor keeping Windows awake whenever Spotify plays."""

    def __init__(self, entry_script: Path, config_path: Path | None = None) -> None:
        self._entry_script = entry_script.resolve()
        self._config_path = config_path or AppConfig.get_default_config_path()
        self._config = AppConfig.load(self._config_path)
        self._logger = AppLogger(enabled=self._config.enable_logging)
        self._smtc = SmtcDetector()
        self._wasapi = WasapiDetector()
        self._window = WindowTitleDetector()
        self._engine = PlaybackDecisionEngine(
            grace_period_seconds=self._config.grace_period_seconds,
            audio_peak_threshold=0.0001,
        )
        self._power = PowerManager(self._logger)

        self._mutex_handle: int | None = None
        self._hwnd: int = 0
        self._hinstance = _kernel32.GetModuleHandleW(None)
        self._class_name = "SpotifyNoSleepHiddenTrayWindow"
        self._taskbar_created_msg = _user32.RegisterWindowMessageW("TaskbarCreated")

        self._icons: dict[TrayVisualState, int] = {}
        self._current_icon_state = TrayVisualState.IDLE
        self._current_tooltip = "SpotifyNoSleep - Monitoring"
        self._status_label = "Status: Monitoring (Spotify idle)"
        self._track_label = ""

        self._monitoring_paused = False
        self._poll_lock = threading.Lock()
        self._poll_in_progress = False
        self._latest_snapshot: PlaybackSnapshot | None = None
        self._last_awake_state = False
        self._last_track_logged = ""
        self._closing = False
        self._wndproc_ref = WNDPROC(self._wndproc)

    def run(self) -> int:
        mutex = _kernel32.CreateMutexW(None, True, SINGLE_INSTANCE_MUTEX_NAME)
        last_err = ctypes.get_last_error()
        if mutex and last_err == ERROR_ALREADY_EXISTS:
            _kernel32.CloseHandle(mutex)
            return 0
        self._mutex_handle = int(mutex) if mutex else None

        try:
            self._init_icons()
            self._create_window()
            self._add_tray_icon()

            _user32.SetTimer(self._hwnd, POLL_TIMER_ID, self._config.poll_interval_ms, None)
            self._logger.info(
                f"SpotifyNoSleep v{__version__} started "
                f"(PollInterval={self._config.poll_interval_ms}ms, "
                f"GracePeriod={self._config.grace_period_seconds}s, "
                f"PreventLock={self._config.prevent_lock_screen}, "
                f"StrictLock={self._config.strict_lock_prevention}, "
                f"KernelPowerRequest={self._power.has_kernel_power_request}, "
                f"Startup={is_startup_enabled()})"
            )

            self._start_background_poll()

            msg = wintypes.MSG()
            while _user32.GetMessageW(ctypes.byref(msg), None, 0, 0) > 0:
                _user32.TranslateMessage(ctypes.byref(msg))
                _user32.DispatchMessageW(ctypes.byref(msg))

            return 0
        finally:
            self._cleanup()

    def _init_icons(self) -> None:
        for state in (TrayVisualState.IDLE, TrayVisualState.ACTIVE, TrayVisualState.PAUSED):
            self._icons[state] = create_tray_hicon(state)

    def _create_window(self) -> None:
        wc = WNDCLASSEXW()
        wc.cbSize = ctypes.sizeof(WNDCLASSEXW)
        wc.lpfnWndProc = self._wndproc_ref
        wc.hInstance = self._hinstance
        wc.lpszClassName = self._class_name
        _user32.RegisterClassExW(ctypes.byref(wc))

        hwnd = _user32.CreateWindowExW(
            0,
            self._class_name,
            "SpotifyNoSleep",
            0,
            0, 0, 0, 0,
            None,
            None,
            self._hinstance,
            None,
        )
        if not hwnd:
            raise RuntimeError(f"CreateWindowExW failed (error {ctypes.get_last_error()})")
        self._hwnd = int(hwnd)

    def _build_nid(self, flags: int, tooltip: str | None = None) -> NOTIFYICONDATAW:
        nid = NOTIFYICONDATAW()
        nid.cbSize = ctypes.sizeof(NOTIFYICONDATAW)
        nid.hWnd = self._hwnd
        nid.uID = 1
        nid.uFlags = flags
        nid.uCallbackMessage = WM_TRAYICON
        nid.hIcon = self._icons[self._current_icon_state]
        tip = tooltip if tooltip is not None else self._current_tooltip
        nid.szTip = tip[:127]
        return nid

    def _add_tray_icon(self) -> None:
        nid = self._build_nid(NIF_MESSAGE | NIF_ICON | NIF_TIP)
        _shell32.Shell_NotifyIconW(NIM_ADD, ctypes.byref(nid))

    def _update_tray_icon(self, state: TrayVisualState, tooltip: str) -> None:
        if state == self._current_icon_state and tooltip == self._current_tooltip:
            return
        self._current_icon_state = state
        self._current_tooltip = tooltip[:127]
        nid = self._build_nid(NIF_ICON | NIF_TIP, self._current_tooltip)
        _shell32.Shell_NotifyIconW(NIM_MODIFY, ctypes.byref(nid))

    def _show_balloon(self, title: str, message: str) -> None:
        if not self._config.show_notifications or not self._hwnd:
            return
        nid = self._build_nid(NIF_INFO)
        nid.szInfoTitle = title[:63]
        nid.szInfo = message[:255]
        nid.dwInfoFlags = NIIF_INFO
        _shell32.Shell_NotifyIconW(NIM_MODIFY, ctypes.byref(nid))

    def _remove_tray_icon(self) -> None:
        if not self._hwnd:
            return
        nid = NOTIFYICONDATAW()
        nid.cbSize = ctypes.sizeof(NOTIFYICONDATAW)
        nid.hWnd = self._hwnd
        nid.uID = 1
        _shell32.Shell_NotifyIconW(NIM_DELETE, ctypes.byref(nid))

    def _start_background_poll(self) -> None:
        if self._closing:
            return
        if self._monitoring_paused:
            self._power.release_all()
            self._update_paused_ui()
            return

        with self._poll_lock:
            if self._poll_in_progress:
                return
            self._poll_in_progress = True

        worker = threading.Thread(target=self._poll_worker, daemon=True)
        worker.start()

    def _poll_worker(self) -> None:
        snapshot = PlaybackSnapshot(utc_now=datetime.now(timezone.utc))
        try:
            snapshot.smtc = self._smtc.detect()
        except Exception as exc:
            self._logger.error("SMTC detection error", exc)

        try:
            snapshot.wasapi = self._wasapi.detect()
        except Exception as exc:
            self._logger.error("WASAPI detection error", exc)

        try:
            snapshot.window_title = self._window.detect()
        except Exception as exc:
            self._logger.error("Window title detection error", exc)

        with self._poll_lock:
            self._latest_snapshot = snapshot
            self._poll_in_progress = False

        if self._hwnd and not self._closing:
            _user32.PostMessageW(self._hwnd, WM_POLL_COMPLETE, 0, 0)

    def _handle_poll_complete(self) -> None:
        if self._closing:
            return
        if self._monitoring_paused:
            self._power.release_all()
            self._update_paused_ui()
            return

        with self._poll_lock:
            snapshot = self._latest_snapshot

        if snapshot is None:
            return

        decision = self._engine.evaluate(snapshot)
        self._power.apply_state(
            keep_awake=decision.should_keep_awake,
            prevent_lock_screen=self._config.prevent_lock_screen,
            strict_lock_prevention=self._config.strict_lock_prevention,
        )

        if decision.should_keep_awake != self._last_awake_state:
            self._last_awake_state = decision.should_keep_awake
            if decision.should_keep_awake:
                track_note = decision.current_track or "(unknown)"
                self._logger.info(f"Awake mode ENGAGED ({decision.reason}). Track: {track_note}")
                self._show_balloon("SpotifyNoSleep Active", "Keeping PC awake while Spotify plays.")
            else:
                self._logger.info(f"Awake mode RELEASED ({decision.reason}).")
                self._show_balloon("SpotifyNoSleep Idle", "Spotify stopped. Normal sleep & lock restored.")
        elif (
            decision.should_keep_awake
            and decision.current_track
            and decision.current_track != self._last_track_logged
        ):
            self._last_track_logged = decision.current_track
            self._logger.info(f"Now playing: {decision.current_track}")

        self._update_ui_for_decision(decision)

    def _update_ui_for_decision(self, decision: PlaybackDecision) -> None:
        if decision.should_keep_awake:
            mode_label = (
                "Keeping PC & screen awake"
                if self._config.prevent_lock_screen
                else "Keeping PC awake (sleep only)"
            )
            self._status_label = (
                "Status: Active (grace period)"
                if decision.in_grace_period
                else f"Status: Active - {mode_label}"
            )
            self._track_label = decision.current_track
            tip = (
                f"SpotifyNoSleep: Active\n{decision.current_track}"
                if decision.current_track
                else "SpotifyNoSleep: Active (Spotify playing)"
            )
            self._update_tray_icon(TrayVisualState.ACTIVE, tip)
        else:
            self._status_label = f"Status: Monitoring ({decision.reason})"
            self._track_label = ""
            self._update_tray_icon(TrayVisualState.IDLE, "SpotifyNoSleep: Monitoring (Spotify idle)")

    def _update_paused_ui(self) -> None:
        self._status_label = "Status: Paused by user"
        self._track_label = ""
        self._update_tray_icon(TrayVisualState.PAUSED, "SpotifyNoSleep: Monitoring paused")

    def _show_context_menu(self) -> None:
        menu = _user32.CreatePopupMenu()
        if not menu:
            return

        try:
            _user32.AppendMenuW(menu, MF_STRING | MF_GRAYED, ID_STATUS, self._status_label)
            if self._track_label:
                short_track = self._track_label if len(self._track_label) <= 52 else self._track_label[:49] + "..."
                _user32.AppendMenuW(menu, MF_STRING | MF_GRAYED, ID_TRACK, f"Playing: {short_track}")

            _user32.AppendMenuW(menu, MF_SEPARATOR, 0, None)
            pause_text = "Resume Monitoring" if self._monitoring_paused else "Pause Monitoring"
            _user32.AppendMenuW(menu, MF_STRING, ID_PAUSE_RESUME, pause_text)
            _user32.AppendMenuW(menu, MF_SEPARATOR, 0, None)

            lock_flags = MF_STRING | (MF_CHECKED if self._config.prevent_lock_screen else MF_UNCHECKED)
            _user32.AppendMenuW(menu, lock_flags, ID_PREVENT_LOCK, "Prevent Lock Screen && Display Off")

            strict_flags = MF_STRING | (MF_CHECKED if self._config.strict_lock_prevention else MF_UNCHECKED)
            if not self._config.prevent_lock_screen:
                strict_flags |= MF_GRAYED
            _user32.AppendMenuW(menu, strict_flags, ID_STRICT_LOCK, "Strict Lock Prevention (Input Keepalive)")

            startup_flags = MF_STRING | (MF_CHECKED if is_startup_enabled() else MF_UNCHECKED)
            _user32.AppendMenuW(menu, startup_flags, ID_STARTUP, "Start Automatically with Windows")

            notif_flags = MF_STRING | (MF_CHECKED if self._config.show_notifications else MF_UNCHECKED)
            _user32.AppendMenuW(menu, notif_flags, ID_NOTIFICATIONS, "Show Tray Notifications")

            _user32.AppendMenuW(menu, MF_SEPARATOR, 0, None)
            _user32.AppendMenuW(menu, MF_STRING, ID_OPEN_LOG, "Open Log File")
            _user32.AppendMenuW(menu, MF_STRING, ID_EXIT, "Exit")

            pt = wintypes.POINT()
            _user32.GetCursorPos(ctypes.byref(pt))
            _user32.SetForegroundWindow(self._hwnd)

            cmd = _user32.TrackPopupMenu(
                menu,
                TPM_LEFTALIGN | TPM_RIGHTBUTTON | TPM_RETURNCMD,
                pt.x,
                pt.y,
                0,
                self._hwnd,
                None,
            )
            _user32.PostMessageW(self._hwnd, WM_NULL, 0, 0)
            if cmd:
                self._handle_menu_command(int(cmd))
        finally:
            _user32.DestroyMenu(menu)

    def _handle_menu_command(self, cmd_id: int) -> None:
        if cmd_id == ID_PAUSE_RESUME:
            self._toggle_pause()
        elif cmd_id == ID_PREVENT_LOCK:
            self._config.prevent_lock_screen = not self._config.prevent_lock_screen
            self._config.save(self._config_path)
            self._logger.info(f"PreventLockScreen set to {self._config.prevent_lock_screen}")
            self._start_background_poll()
        elif cmd_id == ID_STRICT_LOCK:
            self._config.strict_lock_prevention = not self._config.strict_lock_prevention
            self._config.save(self._config_path)
            self._logger.info(f"StrictLockPrevention set to {self._config.strict_lock_prevention}")
            self._start_background_poll()
        elif cmd_id == ID_STARTUP:
            target = not is_startup_enabled()
            if set_startup_enabled(target, self._entry_script):
                self._logger.info(f"Start with Windows set to {target}")
            else:
                self._logger.warn("Failed to update Windows startup registry entry.")
        elif cmd_id == ID_NOTIFICATIONS:
            self._config.show_notifications = not self._config.show_notifications
            self._config.save(self._config_path)
        elif cmd_id == ID_OPEN_LOG:
            try:
                if not self._logger.log_path.exists():
                    self._logger.info("Log file opened by user.")
                os.startfile(str(self._logger.log_path))
            except Exception as exc:
                self._logger.error("Failed to open log file", exc)
        elif cmd_id == ID_EXIT:
            self._logger.info("SpotifyNoSleep exiting at user request.")
            _user32.DestroyWindow(self._hwnd)

    def _toggle_pause(self) -> None:
        self._monitoring_paused = not self._monitoring_paused
        if self._monitoring_paused:
            self._engine.reset()
            self._power.release_all()
            self._last_awake_state = False
            self._update_paused_ui()
            self._logger.info("Monitoring paused by user.")
        else:
            self._logger.info("Monitoring resumed by user.")
            self._start_background_poll()

    def _wndproc(self, hwnd, msg, wparam, lparam):
        if msg == self._taskbar_created_msg and self._taskbar_created_msg != 0:
            self._add_tray_icon()
            return 0

        if msg == WM_TIMER and wparam == POLL_TIMER_ID:
            self._start_background_poll()
            return 0

        if msg == WM_POLL_COMPLETE:
            self._handle_poll_complete()
            return 0

        if msg == WM_TRAYICON:
            event = lparam & 0xFFFF
            if event in (WM_RBUTTONUP, WM_CONTEXTMENU):
                self._show_context_menu()
            elif event == WM_LBUTTONDBLCLK:
                self._toggle_pause()
            return 0

        if msg == WM_POWERBROADCAST:
            if wparam == PBT_APMSUSPEND:
                self._logger.info("System suspending; releasing power requests.")
                self._engine.reset()
                self._power.release_all()
                self._last_awake_state = False
            elif wparam == PBT_APMRESUMEAUTOMATIC:
                self._logger.info("System resumed; restarting playback detection.")
                self._engine.reset()
                self._start_background_poll()
            return 1

        if msg in (WM_QUERYENDSESSION, WM_ENDSESSION):
            self._logger.info("Windows session ending; releasing power requests.")
            self._power.release_all()
            if msg == WM_ENDSESSION and wparam:
                _user32.DestroyWindow(hwnd)
            return 1

        if msg == WM_CLOSE:
            _user32.DestroyWindow(hwnd)
            return 0

        if msg == WM_DESTROY:
            self._closing = True
            _user32.KillTimer(hwnd, POLL_TIMER_ID)
            self._remove_tray_icon()
            self._power.release_all()
            _user32.PostQuitMessage(0)
            return 0

        return _user32.DefWindowProcW(hwnd, msg, wparam, lparam)

    def _cleanup(self) -> None:
        self._closing = True
        self._remove_tray_icon()
        self._power.close()

        for hicon in self._icons.values():
            destroy_hicon(hicon)
        self._icons.clear()

        try:
            _user32.UnregisterClassW(self._class_name, self._hinstance)
        except Exception:
            pass

        if self._mutex_handle:
            _kernel32.CloseHandle(self._mutex_handle)
            self._mutex_handle = None
