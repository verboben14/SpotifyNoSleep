from __future__ import annotations

import ctypes
import math
from ctypes import wintypes
from enum import Enum


class TrayVisualState(Enum):
    IDLE = "idle"
    ACTIVE = "active"
    PAUSED = "paused"


class BITMAPINFOHEADER(ctypes.Structure):
    _fields_ = [
        ("biSize", wintypes.DWORD),
        ("biWidth", wintypes.LONG),
        ("biHeight", wintypes.LONG),
        ("biPlanes", wintypes.WORD),
        ("biBitCount", wintypes.WORD),
        ("biCompression", wintypes.DWORD),
        ("biSizeImage", wintypes.DWORD),
        ("biXPelsPerMeter", wintypes.LONG),
        ("biYPelsPerMeter", wintypes.LONG),
        ("biClrUsed", wintypes.DWORD),
        ("biClrImportant", wintypes.DWORD),
    ]


class BITMAPINFO(ctypes.Structure):
    _fields_ = [
        ("bmiHeader", BITMAPINFOHEADER),
        ("bmiColors", wintypes.DWORD * 3),
    ]


class ICONINFO(ctypes.Structure):
    _fields_ = [
        ("fIcon", wintypes.BOOL),
        ("xHotspot", wintypes.DWORD),
        ("yHotspot", wintypes.DWORD),
        ("hbmMask", wintypes.HBITMAP),
        ("hbmColor", wintypes.HBITMAP),
    ]


_gdi32 = ctypes.WinDLL("gdi32", use_last_error=True)
_gdi32.CreateDIBSection.argtypes = [
    wintypes.HDC,
    ctypes.POINTER(BITMAPINFO),
    wintypes.UINT,
    ctypes.POINTER(ctypes.c_void_p),
    wintypes.HANDLE,
    wintypes.DWORD,
]
_gdi32.CreateDIBSection.restype = wintypes.HBITMAP
_gdi32.CreateBitmap.argtypes = [ctypes.c_int, ctypes.c_int, wintypes.UINT, wintypes.UINT, ctypes.c_void_p]
_gdi32.CreateBitmap.restype = wintypes.HBITMAP
_gdi32.DeleteObject.argtypes = [wintypes.HGDIOBJ]
_gdi32.DeleteObject.restype = wintypes.BOOL

_user32 = ctypes.WinDLL("user32", use_last_error=True)
_user32.CreateIconIndirect.argtypes = [ctypes.POINTER(ICONINFO)]
_user32.CreateIconIndirect.restype = wintypes.HICON
_user32.DestroyIcon.argtypes = [wintypes.HICON]
_user32.DestroyIcon.restype = wintypes.BOOL


def _blend(dst: tuple[float, float, float, float], src: tuple[float, float, float, float]) -> tuple[float, float, float, float]:
    sr, sg, sb, sa = src
    dr, dg, db, da = dst
    out_a = sa + da * (1.0 - sa)
    if out_a <= 1e-6:
        return (0.0, 0.0, 0.0, 0.0)
    out_r = (sr * sa + dr * da * (1.0 - sa)) / out_a
    out_g = (sg * sa + dg * da * (1.0 - sa)) / out_a
    out_b = (sb * sa + db * da * (1.0 - sa)) / out_a
    return (out_r, out_g, out_b, out_a)


def _clamp01(v: float) -> float:
    return 0.0 if v < 0.0 else (1.0 if v > 1.0 else v)


def _arc_alpha(x: float, y: float, cx: float, cy: float, radius: float, half_thick: float, max_angle_rad: float) -> float:
    dx = x - cx
    dy = y - cy
    dist = math.hypot(dx, dy)
    if dist < 1e-6:
        return 0.0
    # Angle measured from straight up (-y axis)
    angle = math.atan2(dx, -dy)
    if abs(angle) <= max_angle_rad:
        radial_err = abs(dist - radius)
    else:
        edge_angle = max_angle_rad if angle > 0 else -max_angle_rad
        ex = cx + radius * math.sin(edge_angle)
        ey = cy - radius * math.cos(edge_angle)
        radial_err = math.hypot(x - ex, y - ey)
    return _clamp01(half_thick + 0.65 - radial_err)


def render_icon_bgra(state: TrayVisualState, size: int = 32) -> bytearray:
    """Render a crisp anti-aliased 32-bit BGRA tray icon pixel buffer."""
    if state == TrayVisualState.ACTIVE:
        base_rgb = (29.0, 185.0, 84.0)      # Spotify Green
    elif state == TrayVisualState.PAUSED:
        base_rgb = (229.0, 160.0, 13.0)     # Warm Amber
    else:
        base_rgb = (110.0, 118.0, 129.0)    # Neutral Slate

    buf = bytearray(size * size * 4)
    center = (size - 1) / 2.0
    outer_r = (size / 2.0) - 1.2

    for py in range(size):
        for px in range(size):
            px_f = float(px)
            py_f = float(py)

            # Base circle with smooth anti-aliased edge
            d_center = math.hypot(px_f - center, py_f - center)
            circle_a = _clamp01(outer_r + 0.65 - d_center)
            color = (0.0, 0.0, 0.0, 0.0)
            if circle_a > 0.0:
                color = _blend(color, (base_rgb[0], base_rgb[1], base_rgb[2], circle_a))

                # Subtle dark rim for crisp contrast on light/dark taskbars
                rim_a = _clamp01(1.0 - abs(d_center - (outer_r - 0.5))) * 0.32 * circle_a
                if rim_a > 0.0:
                    color = _blend(color, (0.0, 0.0, 0.0, rim_a))

            if state == TrayVisualState.PAUSED:
                # Two crisp vertical pause bars
                in_left = (9.5 <= px_f <= 13.5) and (9.0 <= py_f <= 22.5)
                in_right = (17.5 <= px_f <= 21.5) and (9.0 <= py_f <= 22.5)
                if in_left or in_right:
                    color = _blend(color, (255.0, 255.0, 255.0, 0.98))
            else:
                # Three curved sound-wave arcs (Spotify-inspired)
                a1 = _arc_alpha(px_f, py_f, center, center + 11.5, 19.5, 1.35, 0.44)
                a2 = _arc_alpha(px_f, py_f, center, center + 11.0, 14.5, 1.25, 0.43)
                a3 = _arc_alpha(px_f, py_f, center, center + 10.5, 9.8, 1.15, 0.40)
                wave_a = max(a1, a2, a3)
                if wave_a > 0.0:
                    color = _blend(color, (255.0, 255.0, 255.0, wave_a * 0.96))

                # Bright yellow awake indicator dot in top-right when actively keeping PC awake
                if state == TrayVisualState.ACTIVE:
                    dot_cx = size - 6.2
                    dot_cy = 6.2
                    d_dot = math.hypot(px_f - dot_cx, py_f - dot_cy)
                    halo_a = _clamp01(5.1 - d_dot)
                    if halo_a > 0.0:
                        color = _blend(color, (20.0, 20.0, 20.0, halo_a * 0.92))
                    core_a = _clamp01(3.6 - d_dot)
                    if core_a > 0.0:
                        color = _blend(color, (255.0, 232.0, 96.0, core_a))

            idx = (py * size + px) * 4
            r, g, b, a = color
            # Premultiplied BGRA for crisp alpha-blended Win32 tray icons
            buf[idx + 0] = int(_clamp01(b / 255.0) * a * 255.0 + 0.5)
            buf[idx + 1] = int(_clamp01(g / 255.0) * a * 255.0 + 0.5)
            buf[idx + 2] = int(_clamp01(r / 255.0) * a * 255.0 + 0.5)
            buf[idx + 3] = int(a * 255.0 + 0.5)

    return buf


def create_tray_hicon(state: TrayVisualState, size: int = 32) -> int:
    """Create a Win32 HICON handle for the given visual state. Caller must destroy with destroy_hicon."""
    pixels = render_icon_bgra(state, size=size)

    bmi = BITMAPINFO()
    bmi.bmiHeader.biSize = ctypes.sizeof(BITMAPINFOHEADER)
    bmi.bmiHeader.biWidth = size
    bmi.bmiHeader.biHeight = -size  # top-down DIB
    bmi.bmiHeader.biPlanes = 1
    bmi.bmiHeader.biBitCount = 32
    bmi.bmiHeader.biCompression = 0  # BI_RGB

    bits_ptr = ctypes.c_void_p()
    hbm_color = _gdi32.CreateDIBSection(None, ctypes.byref(bmi), 0, ctypes.byref(bits_ptr), None, 0)
    if not hbm_color or not bits_ptr.value:
        raise RuntimeError("CreateDIBSection failed")

    hbm_mask = _gdi32.CreateBitmap(size, size, 1, 1, None)
    if not hbm_mask:
        _gdi32.DeleteObject(hbm_color)
        raise RuntimeError("CreateBitmap failed")

    try:
        ctypes.memmove(bits_ptr.value, (ctypes.c_ubyte * len(pixels)).from_buffer(pixels), len(pixels))
        info = ICONINFO()
        info.fIcon = True
        info.xHotspot = 0
        info.yHotspot = 0
        info.hbmMask = hbm_mask
        info.hbmColor = hbm_color
        hicon = _user32.CreateIconIndirect(ctypes.byref(info))
        if not hicon:
            raise RuntimeError("CreateIconIndirect failed")
        return int(hicon)
    finally:
        _gdi32.DeleteObject(hbm_color)
        _gdi32.DeleteObject(hbm_mask)


def destroy_hicon(hicon: int | None) -> None:
    if hicon:
        _user32.DestroyIcon(hicon)
