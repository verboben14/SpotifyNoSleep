from __future__ import annotations

import ctypes
from ctypes import wintypes

from .com_utils import GUID, co_task_mem_free, com_query_interface, com_release, com_vtable_func
from .models import WasapiSnapshot
from .win_processes import get_spotify_pids

COINIT_MULTITHREADED = 0x0
CLSCTX_ALL = 23
E_RENDER = 0
DEVICE_STATE_ACTIVE = 0x00000001
AUDIO_SESSION_STATE_ACTIVE = 1

CLSID_MM_DEVICE_ENUMERATOR = GUID.from_string("BCDE0395-E52F-467C-8E3D-C4579291692E")
IID_IMM_DEVICE_ENUMERATOR = GUID.from_string("A95664D2-9614-4F35-A746-DE8DB63617E6")
IID_IAUDIO_SESSION_MANAGER2 = GUID.from_string("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F")
IID_IAUDIO_SESSION_CONTROL2 = GUID.from_string("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D")
IID_IAUDIO_METER_INFORMATION = GUID.from_string("C02216F6-8C67-4B5B-9D00-D008E73E0064")

_ole32 = ctypes.WinDLL("ole32", use_last_error=False)
_ole32.CoInitializeEx.argtypes = [ctypes.c_void_p, wintypes.DWORD]
_ole32.CoInitializeEx.restype = ctypes.c_long
_ole32.CoUninitialize.argtypes = []
_ole32.CoUninitialize.restype = None
_ole32.CoCreateInstance.argtypes = [
    ctypes.POINTER(GUID),
    ctypes.c_void_p,
    wintypes.DWORD,
    ctypes.POINTER(GUID),
    ctypes.POINTER(ctypes.c_void_p),
]
_ole32.CoCreateInstance.restype = ctypes.c_long


class WasapiDetector:
    """Inspects Windows Core Audio (WASAPI) sessions across all active render endpoints."""

    def detect(self) -> WasapiSnapshot:
        snapshot = WasapiSnapshot()
        spotify_pids = get_spotify_pids()
        snapshot.spotify_process_detected = len(spotify_pids) > 0

        com_init_hr = _ole32.CoInitializeEx(None, COINIT_MULTITHREADED)
        should_uninit = com_init_hr in (0, 1)  # S_OK or S_FALSE

        enumerator_ptr = 0
        devices_ptr = 0

        try:
            out_enum = ctypes.c_void_p()
            hr = _ole32.CoCreateInstance(
                ctypes.byref(CLSID_MM_DEVICE_ENUMERATOR),
                None,
                CLSCTX_ALL,
                ctypes.byref(IID_IMM_DEVICE_ENUMERATOR),
                ctypes.byref(out_enum),
            )
            if hr != 0 or not out_enum.value:
                snapshot.is_available = False
                snapshot.error_message = f"CoCreateInstance(MMDeviceEnumerator) failed (0x{hr & 0xFFFFFFFF:08X})"
                return snapshot

            enumerator_ptr = int(out_enum.value)

            # IMMDeviceEnumerator::EnumAudioEndpoints (slot 3)
            enum_endpoints = com_vtable_func(
                enumerator_ptr,
                3,
                ctypes.c_long,
                ctypes.c_int,
                wintypes.DWORD,
                ctypes.POINTER(ctypes.c_void_p),
            )
            out_devices = ctypes.c_void_p()
            hr = enum_endpoints(enumerator_ptr, E_RENDER, DEVICE_STATE_ACTIVE, ctypes.byref(out_devices))
            if hr != 0 or not out_devices.value:
                snapshot.is_available = False
                snapshot.error_message = f"EnumAudioEndpoints failed (0x{hr & 0xFFFFFFFF:08X})"
                return snapshot

            devices_ptr = int(out_devices.value)
            snapshot.is_available = True

            # IMMDeviceCollection::GetCount (slot 3)
            get_count = com_vtable_func(devices_ptr, 3, ctypes.c_long, ctypes.POINTER(wintypes.UINT))
            device_count = wintypes.UINT(0)
            if get_count(devices_ptr, ctypes.byref(device_count)) != 0:
                return snapshot

            # IMMDeviceCollection::Item (slot 4)
            get_item = com_vtable_func(
                devices_ptr,
                4,
                ctypes.c_long,
                wintypes.UINT,
                ctypes.POINTER(ctypes.c_void_p),
            )

            for i in range(device_count.value):
                device_out = ctypes.c_void_p()
                if get_item(devices_ptr, i, ctypes.byref(device_out)) == 0 and device_out.value:
                    device_ptr = int(device_out.value)
                    try:
                        self._inspect_device(device_ptr, spotify_pids, snapshot)
                    finally:
                        com_release(device_ptr)

        except Exception as exc:
            snapshot.is_available = False
            snapshot.error_message = str(exc)
        finally:
            com_release(devices_ptr)
            com_release(enumerator_ptr)
            if should_uninit:
                _ole32.CoUninitialize()

        return snapshot

    def _inspect_device(self, device_ptr: int, spotify_pids: set[int], snapshot: WasapiSnapshot) -> None:
        session_mgr_ptr = 0
        session_enum_ptr = 0

        try:
            # IMMDevice::Activate (slot 3)
            activate = com_vtable_func(
                device_ptr,
                3,
                ctypes.c_long,
                ctypes.POINTER(GUID),
                wintypes.DWORD,
                ctypes.c_void_p,
                ctypes.POINTER(ctypes.c_void_p),
            )
            out_mgr = ctypes.c_void_p()
            hr = activate(
                device_ptr,
                ctypes.byref(IID_IAUDIO_SESSION_MANAGER2),
                CLSCTX_ALL,
                None,
                ctypes.byref(out_mgr),
            )
            if hr != 0 or not out_mgr.value:
                return
            session_mgr_ptr = int(out_mgr.value)

            # IAudioSessionManager2::GetSessionEnumerator (slot 5)
            get_enum = com_vtable_func(
                session_mgr_ptr,
                5,
                ctypes.c_long,
                ctypes.POINTER(ctypes.c_void_p),
            )
            out_enum = ctypes.c_void_p()
            if get_enum(session_mgr_ptr, ctypes.byref(out_enum)) != 0 or not out_enum.value:
                return
            session_enum_ptr = int(out_enum.value)

            # IAudioSessionEnumerator::GetCount (slot 3)
            get_count = com_vtable_func(session_enum_ptr, 3, ctypes.c_long, ctypes.POINTER(ctypes.c_int))
            session_count = ctypes.c_int(0)
            if get_count(session_enum_ptr, ctypes.byref(session_count)) != 0:
                return

            # IAudioSessionEnumerator::GetSession (slot 4)
            get_session = com_vtable_func(
                session_enum_ptr,
                4,
                ctypes.c_long,
                ctypes.c_int,
                ctypes.POINTER(ctypes.c_void_p),
            )

            for i in range(session_count.value):
                out_ctrl = ctypes.c_void_p()
                if get_session(session_enum_ptr, i, ctypes.byref(out_ctrl)) != 0 or not out_ctrl.value:
                    continue

                ctrl_ptr = int(out_ctrl.value)
                ctrl2_ptr = 0
                meter_ptr = 0
                try:
                    ctrl2_ptr = com_query_interface(ctrl_ptr, IID_IAUDIO_SESSION_CONTROL2)
                    if not ctrl2_ptr:
                        continue

                    if not self._is_spotify_session(ctrl2_ptr, spotify_pids):
                        continue

                    snapshot.spotify_process_detected = True

                    # IAudioSessionControl::GetState (slot 3)
                    get_state = com_vtable_func(ctrl2_ptr, 3, ctypes.c_long, ctypes.POINTER(ctypes.c_int))
                    state_val = ctypes.c_int(0)
                    if get_state(ctrl2_ptr, ctypes.byref(state_val)) == 0 and state_val.value == AUDIO_SESSION_STATE_ACTIVE:
                        snapshot.has_active_session = True
                        snapshot.active_session_count += 1

                        meter_ptr = com_query_interface(ctrl_ptr, IID_IAUDIO_METER_INFORMATION)
                        if meter_ptr:
                            # IAudioMeterInformation::GetPeakValue (slot 3)
                            get_peak = com_vtable_func(meter_ptr, 3, ctypes.c_long, ctypes.POINTER(ctypes.c_float))
                            peak = ctypes.c_float(0.0)
                            if get_peak(meter_ptr, ctypes.byref(peak)) == 0 and peak.value > snapshot.max_peak_level:
                                snapshot.max_peak_level = float(peak.value)
                finally:
                    com_release(meter_ptr)
                    com_release(ctrl2_ptr)
                    com_release(ctrl_ptr)
        finally:
            com_release(session_enum_ptr)
            com_release(session_mgr_ptr)

    @staticmethod
    def _is_spotify_session(ctrl2_ptr: int, spotify_pids: set[int]) -> bool:
        # IAudioSessionControl2::GetProcessId (slot 14)
        get_pid = com_vtable_func(ctrl2_ptr, 14, ctypes.c_long, ctypes.POINTER(wintypes.DWORD))
        pid = wintypes.DWORD(0)
        if get_pid(ctrl2_ptr, ctypes.byref(pid)) == 0 and pid.value != 0 and int(pid.value) in spotify_pids:
            return True

        # Fallback: IAudioSessionControl2::GetSessionIdentifier (slot 12)
        get_id = com_vtable_func(ctrl2_ptr, 12, ctypes.c_long, ctypes.POINTER(ctypes.c_void_p))
        id_ptr = ctypes.c_void_p()
        if get_id(ctrl2_ptr, ctypes.byref(id_ptr)) == 0 and id_ptr.value:
            try:
                session_id = ctypes.wstring_at(id_ptr.value)
                if "spotify.exe" in session_id.lower():
                    return True
            finally:
                co_task_mem_free(int(id_ptr.value))

        return False
