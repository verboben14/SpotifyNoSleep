from __future__ import annotations

import ctypes
import time
from ctypes import wintypes

from .com_utils import GUID, com_query_interface, com_release, com_vtable_func
from .models import SmtcSnapshot, SmtcState

RO_INIT_MULTITHREADED = 1
ASYNC_STATUS_STARTED = 0
ASYNC_STATUS_COMPLETED = 1

SMTC_CLASS_NAME = "Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager"
IID_SMTC_MANAGER_STATICS = GUID.from_string("2050C4EE-11A0-57DE-AED7-C97C70338245")
IID_IASYNC_INFO = GUID.from_string("00000036-0000-0000-C000-000000000046")

_combase = ctypes.WinDLL("combase", use_last_error=False)
_combase.RoInitialize.argtypes = [ctypes.c_int]
_combase.RoInitialize.restype = ctypes.c_long
_combase.RoUninitialize.argtypes = []
_combase.RoUninitialize.restype = None
_combase.RoGetActivationFactory.argtypes = [
    ctypes.c_void_p,
    ctypes.POINTER(GUID),
    ctypes.POINTER(ctypes.c_void_p),
]
_combase.RoGetActivationFactory.restype = ctypes.c_long
_combase.WindowsCreateString.argtypes = [wintypes.LPCWSTR, wintypes.UINT, ctypes.POINTER(ctypes.c_void_p)]
_combase.WindowsCreateString.restype = ctypes.c_long
_combase.WindowsDeleteString.argtypes = [ctypes.c_void_p]
_combase.WindowsDeleteString.restype = ctypes.c_long
_combase.WindowsGetStringRawBuffer.argtypes = [ctypes.c_void_p, ctypes.POINTER(wintypes.UINT)]
_combase.WindowsGetStringRawBuffer.restype = ctypes.c_void_p

_PLAYBACK_STATUS_MAP = {
    0: SmtcState.STOPPED,   # Closed
    1: SmtcState.OTHER,     # Opened
    2: SmtcState.OTHER,     # Changing
    3: SmtcState.STOPPED,   # Stopped
    4: SmtcState.PLAYING,   # Playing
    5: SmtcState.PAUSED,    # Paused
}


def _hstring_to_str(hstring_ptr: int) -> str:
    if not hstring_ptr:
        return ""
    length = wintypes.UINT(0)
    raw_ptr = _combase.WindowsGetStringRawBuffer(ctypes.c_void_p(hstring_ptr), ctypes.byref(length))
    if not raw_ptr or length.value == 0:
        return ""
    return ctypes.wstring_at(raw_ptr, length.value)


def _delete_hstring(hstring_ptr: int) -> None:
    if hstring_ptr:
        _combase.WindowsDeleteString(ctypes.c_void_p(hstring_ptr))


def _await_async_operation(async_op_ptr: int, timeout_seconds: float = 1.2) -> tuple[int, str]:
    """Wait for a WinRT IAsyncOperation<T> to complete and return (result_ptr, error_message)."""
    if not async_op_ptr:
        return (0, "null async operation")

    async_info_ptr = com_query_interface(async_op_ptr, IID_IASYNC_INFO)
    if not async_info_ptr:
        return (0, "QueryInterface(IAsyncInfo) failed")

    try:
        # IAsyncInfo::get_Status (slot 7) and get_ErrorCode (slot 8)
        get_status = com_vtable_func(async_info_ptr, 7, ctypes.c_long, ctypes.POINTER(ctypes.c_int))
        get_error = com_vtable_func(async_info_ptr, 8, ctypes.c_long, ctypes.POINTER(ctypes.c_long))
        deadline = time.monotonic() + timeout_seconds
        status = ctypes.c_int(ASYNC_STATUS_STARTED)

        while True:
            hr = get_status(async_info_ptr, ctypes.byref(status))
            if hr != 0:
                return (0, f"IAsyncInfo::get_Status failed (0x{hr & 0xFFFFFFFF:08X})")
            if status.value == ASYNC_STATUS_COMPLETED:
                break
            if status.value != ASYNC_STATUS_STARTED:
                err_code = ctypes.c_long(0)
                get_error(async_info_ptr, ctypes.byref(err_code))
                code_hex = err_code.value & 0xFFFFFFFF
                return (0, f"WinRT async status={status.value} (0x{code_hex:08X})")
            if time.monotonic() >= deadline:
                return (0, "WinRT async operation timed out")
            time.sleep(0.005)

        # IAsyncOperation<T>::GetResults (slot 8)
        get_results = com_vtable_func(async_op_ptr, 8, ctypes.c_long, ctypes.POINTER(ctypes.c_void_p))
        out_result = ctypes.c_void_p()
        hr = get_results(async_op_ptr, ctypes.byref(out_result))
        if hr == 0 and out_result.value:
            return (int(out_result.value), "")
        return (0, f"IAsyncOperation::GetResults failed (0x{hr & 0xFFFFFFFF:08X})")
    finally:
        com_release(async_info_ptr)


class SmtcDetector:
    """Reads Spotify playback status and track metadata via Windows WinRT SMTC (combase.dll)."""

    def detect(self) -> SmtcSnapshot:
        snapshot = SmtcSnapshot(state=SmtcState.UNAVAILABLE)
        ro_hr = _combase.RoInitialize(RO_INIT_MULTITHREADED)
        should_uninit = ro_hr in (0, 1)

        class_hstring = 0
        statics_ptr = 0
        req_op_ptr = 0
        manager_ptr = 0
        sessions_ptr = 0

        try:
            hstr = ctypes.c_void_p()
            hr = _combase.WindowsCreateString(SMTC_CLASS_NAME, len(SMTC_CLASS_NAME), ctypes.byref(hstr))
            if hr != 0 or not hstr.value:
                snapshot.error_message = f"WindowsCreateString failed (0x{hr & 0xFFFFFFFF:08X})"
                return snapshot
            class_hstring = int(hstr.value)

            out_statics = ctypes.c_void_p()
            hr = _combase.RoGetActivationFactory(
                ctypes.c_void_p(class_hstring),
                ctypes.byref(IID_SMTC_MANAGER_STATICS),
                ctypes.byref(out_statics),
            )
            if hr != 0 or not out_statics.value:
                snapshot.error_message = f"RoGetActivationFactory failed (0x{hr & 0xFFFFFFFF:08X})"
                return snapshot
            statics_ptr = int(out_statics.value)

            # IGlobalSystemMediaTransportControlsSessionManagerStatics::RequestAsync (slot 6)
            request_async = com_vtable_func(statics_ptr, 6, ctypes.c_long, ctypes.POINTER(ctypes.c_void_p))
            out_op = ctypes.c_void_p()
            hr = request_async(statics_ptr, ctypes.byref(out_op))
            if hr != 0 or not out_op.value:
                snapshot.error_message = f"RequestAsync failed (0x{hr & 0xFFFFFFFF:08X})"
                return snapshot
            req_op_ptr = int(out_op.value)

            manager_ptr, err_msg = _await_async_operation(req_op_ptr, timeout_seconds=1.5)
            if not manager_ptr:
                snapshot.error_message = f"SMTC SessionManager RequestAsync failed: {err_msg}"
                return snapshot

            # IGlobalSystemMediaTransportControlsSessionManager::GetSessions (slot 7)
            get_sessions = com_vtable_func(manager_ptr, 7, ctypes.c_long, ctypes.POINTER(ctypes.c_void_p))
            out_sessions = ctypes.c_void_p()
            hr = get_sessions(manager_ptr, ctypes.byref(out_sessions))
            if hr != 0 or not out_sessions.value:
                snapshot.state = SmtcState.NOT_FOUND
                return snapshot
            sessions_ptr = int(out_sessions.value)

            # IVectorView::get_Size (slot 7) and GetAt (slot 6)
            get_size = com_vtable_func(sessions_ptr, 7, ctypes.c_long, ctypes.POINTER(wintypes.UINT))
            count = wintypes.UINT(0)
            if get_size(sessions_ptr, ctypes.byref(count)) != 0 or count.value == 0:
                snapshot.state = SmtcState.NOT_FOUND
                return snapshot

            get_at = com_vtable_func(
                sessions_ptr,
                6,
                ctypes.c_long,
                wintypes.UINT,
                ctypes.POINTER(ctypes.c_void_p),
            )

            best_state = SmtcState.NOT_FOUND
            best_app_id = ""
            best_title = ""
            best_artist = ""

            for i in range(count.value):
                out_sess = ctypes.c_void_p()
                if get_at(sessions_ptr, i, ctypes.byref(out_sess)) != 0 or not out_sess.value:
                    continue

                sess_ptr = int(out_sess.value)
                try:
                    app_id = self._get_source_app_id(sess_ptr)
                    if "spotify" not in app_id.lower():
                        continue

                    state = self._get_playback_state(sess_ptr)
                    if best_state == SmtcState.NOT_FOUND or state == SmtcState.PLAYING:
                        best_state = state
                        best_app_id = app_id
                        if state == SmtcState.PLAYING:
                            best_title, best_artist = self._get_media_properties(sess_ptr)
                            break
                finally:
                    com_release(sess_ptr)

            snapshot.state = best_state
            snapshot.source_app_id = best_app_id
            snapshot.track_title = best_title
            snapshot.track_artist = best_artist
            return snapshot

        except Exception as exc:
            snapshot.state = SmtcState.UNAVAILABLE
            snapshot.error_message = str(exc)
            return snapshot
        finally:
            com_release(sessions_ptr)
            com_release(manager_ptr)
            com_release(req_op_ptr)
            com_release(statics_ptr)
            _delete_hstring(class_hstring)
            if should_uninit:
                _combase.RoUninitialize()

    @staticmethod
    def _get_source_app_id(session_ptr: int) -> str:
        # IGlobalSystemMediaTransportControlsSession::get_SourceAppUserModelId (slot 6)
        get_aumid = com_vtable_func(session_ptr, 6, ctypes.c_long, ctypes.POINTER(ctypes.c_void_p))
        out_hstr = ctypes.c_void_p()
        if get_aumid(session_ptr, ctypes.byref(out_hstr)) != 0 or not out_hstr.value:
            return ""
        hstr_ptr = int(out_hstr.value)
        try:
            return _hstring_to_str(hstr_ptr)
        finally:
            _delete_hstring(hstr_ptr)

    @staticmethod
    def _get_playback_state(session_ptr: int) -> SmtcState:
        # IGlobalSystemMediaTransportControlsSession::GetPlaybackInfo (slot 9)
        get_info = com_vtable_func(session_ptr, 9, ctypes.c_long, ctypes.POINTER(ctypes.c_void_p))
        out_info = ctypes.c_void_p()
        if get_info(session_ptr, ctypes.byref(out_info)) != 0 or not out_info.value:
            return SmtcState.OTHER

        info_ptr = int(out_info.value)
        try:
            # IGlobalSystemMediaTransportControlsSessionPlaybackInfo::get_PlaybackStatus (slot 7)
            get_status = com_vtable_func(info_ptr, 7, ctypes.c_long, ctypes.POINTER(ctypes.c_int))
            status = ctypes.c_int(-1)
            if get_status(info_ptr, ctypes.byref(status)) != 0:
                return SmtcState.OTHER
            return _PLAYBACK_STATUS_MAP.get(status.value, SmtcState.OTHER)
        finally:
            com_release(info_ptr)

    @staticmethod
    def _get_media_properties(session_ptr: int) -> tuple[str, str]:
        # IGlobalSystemMediaTransportControlsSession::TryGetMediaPropertiesAsync (slot 7)
        try_get_props = com_vtable_func(session_ptr, 7, ctypes.c_long, ctypes.POINTER(ctypes.c_void_p))
        out_op = ctypes.c_void_p()
        if try_get_props(session_ptr, ctypes.byref(out_op)) != 0 or not out_op.value:
            return ("", "")

        op_ptr = int(out_op.value)
        props_ptr = 0
        try:
            props_ptr, _ = _await_async_operation(op_ptr, timeout_seconds=0.5)
            if not props_ptr:
                return ("", "")

            title = SmtcDetector._get_hstring_prop(props_ptr, 6)   # get_Title
            artist = SmtcDetector._get_hstring_prop(props_ptr, 9)  # get_Artist
            return (title, artist)
        finally:
            com_release(props_ptr)
            com_release(op_ptr)

    @staticmethod
    def _get_hstring_prop(obj_ptr: int, slot: int) -> str:
        getter = com_vtable_func(obj_ptr, slot, ctypes.c_long, ctypes.POINTER(ctypes.c_void_p))
        out_hstr = ctypes.c_void_p()
        if getter(obj_ptr, ctypes.byref(out_hstr)) != 0 or not out_hstr.value:
            return ""
        hstr_ptr = int(out_hstr.value)
        try:
            return _hstring_to_str(hstr_ptr)
        finally:
            _delete_hstring(hstr_ptr)
