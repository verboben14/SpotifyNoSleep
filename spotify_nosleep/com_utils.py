from __future__ import annotations

import ctypes
import uuid
from ctypes import wintypes

_ole32 = ctypes.WinDLL("ole32", use_last_error=False)
_ole32.CoTaskMemFree.argtypes = [ctypes.c_void_p]
_ole32.CoTaskMemFree.restype = None


class GUID(ctypes.Structure):
    _fields_ = [
        ("Data1", wintypes.DWORD),
        ("Data2", wintypes.WORD),
        ("Data3", wintypes.WORD),
        ("Data4", ctypes.c_ubyte * 8),
    ]

    @classmethod
    def from_string(cls, guid_str: str) -> "GUID":
        return cls.from_buffer_copy(uuid.UUID(guid_str).bytes_le)


def com_vtable_func(ptr: int, slot: int, restype, *argtypes):
    """Build a callable WINFUNCTYPE bound to a COM object's vtable slot."""
    vtable = ctypes.cast(ctypes.c_void_p(ptr), ctypes.POINTER(ctypes.c_void_p))[0]
    fn_addr = ctypes.cast(ctypes.c_void_p(vtable), ctypes.POINTER(ctypes.c_void_p))[slot]
    proto = ctypes.WINFUNCTYPE(restype, ctypes.c_void_p, *argtypes)
    return proto(fn_addr)


def com_query_interface(ptr: int, iid: GUID) -> int:
    """Call IUnknown::QueryInterface (slot 0). Returns new pointer int or 0."""
    if not ptr:
        return 0
    qi = com_vtable_func(
        ptr,
        0,
        ctypes.c_long,
        ctypes.POINTER(GUID),
        ctypes.POINTER(ctypes.c_void_p),
    )
    out = ctypes.c_void_p()
    hr = qi(ptr, ctypes.byref(iid), ctypes.byref(out))
    if hr == 0 and out.value:
        return int(out.value)
    return 0


def com_release(ptr: int | None) -> None:
    """Safely call IUnknown::Release (slot 2)."""
    if not ptr:
        return
    try:
        rel = com_vtable_func(ptr, 2, wintypes.ULONG)
        rel(ptr)
    except Exception:
        pass


def co_task_mem_free(ptr: int | None) -> None:
    if ptr:
        _ole32.CoTaskMemFree(ctypes.c_void_p(ptr))
