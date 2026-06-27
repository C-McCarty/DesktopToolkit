import ctypes
from ctypes import wintypes
from models import MonitorInfo

user32 = ctypes.windll.user32

DISPLAY_DEVICE_ATTACHED_TO_DESKTOP = 0x00000001
DISPLAY_DEVICE_PRIMARY_DEVICE = 0x00000004

DM_POSITION = 0x00000020

CDS_UPDATEREGISTRY = 0x00000001
CDS_NORESET = 0x10000000

ENUM_CURRENT_SETTINGS = 0xFFFFFFFF

DISP_CHANGE_SUCCESSFUL = 0
DISP_CHANGE_RESTART = 1

# ChangeDisplaySettingsEx return codes → human-readable explanations
DISP_CHANGE_MESSAGES = {
    1: 'The computer must be restarted for the change to take effect.',
    -1: 'The display driver rejected the layout (DISP_CHANGE_FAILED). '
        'Usually means a gap between monitors or no monitor at the (0,0) origin.',
    -2: 'The layout is not a valid graphics mode (DISP_CHANGE_BADMODE).',
    -3: 'The settings could not be written to the registry (DISP_CHANGE_NOTUPDATED).',
    -4: 'Invalid flags passed (DISP_CHANGE_BADFLAGS).',
    -5: 'An invalid parameter was passed (DISP_CHANGE_BADPARAM).',
    -6: 'Invalid for a dual-view display (DISP_CHANGE_BADDUALVIEW).',
}


def _explain(code: int) -> str:
    return DISP_CHANGE_MESSAGES.get(code, f'Unknown error (code {code}).')


class DEVMODE(ctypes.Structure):
    _fields_ = [
        ('dmDeviceName', ctypes.c_wchar * 32),
        ('dmSpecVersion', ctypes.c_ushort),
        ('dmDriverVersion', ctypes.c_ushort),
        ('dmSize', ctypes.c_ushort),
        ('dmDriverExtra', ctypes.c_ushort),
        ('dmFields', ctypes.c_ulong),
        # Display-device union fields (flattened — only used for display, not printer)
        ('dmPositionX', ctypes.c_long),
        ('dmPositionY', ctypes.c_long),
        ('dmDisplayOrientation', ctypes.c_ulong),
        ('dmDisplayFixedOutput', ctypes.c_ulong),
        ('dmColor', ctypes.c_short),
        ('dmDuplex', ctypes.c_short),
        ('dmYResolution', ctypes.c_short),
        ('dmTTOption', ctypes.c_short),
        ('dmCollate', ctypes.c_short),
        ('dmFormName', ctypes.c_wchar * 32),
        ('dmLogPixels', ctypes.c_ushort),
        ('dmBitsPerPel', ctypes.c_ulong),
        ('dmPelsWidth', ctypes.c_ulong),
        ('dmPelsHeight', ctypes.c_ulong),
        ('dmDisplayFlags', ctypes.c_ulong),
        ('dmDisplayFrequency', ctypes.c_ulong),
        ('dmICMMethod', ctypes.c_ulong),
        ('dmICMIntent', ctypes.c_ulong),
        ('dmMediaType', ctypes.c_ulong),
        ('dmDitherType', ctypes.c_ulong),
        ('dmReserved1', ctypes.c_ulong),
        ('dmReserved2', ctypes.c_ulong),
        ('dmPanningWidth', ctypes.c_ulong),
        ('dmPanningHeight', ctypes.c_ulong),
    ]


class DISPLAY_DEVICE(ctypes.Structure):
    _fields_ = [
        ('cb', wintypes.DWORD),
        ('DeviceName', ctypes.c_wchar * 32),
        ('DeviceString', ctypes.c_wchar * 128),
        ('StateFlags', wintypes.DWORD),
        ('DeviceID', ctypes.c_wchar * 128),
        ('DeviceKey', ctypes.c_wchar * 128),
    ]


user32.EnumDisplayDevicesW.argtypes = [
    wintypes.LPCWSTR,
    wintypes.DWORD,
    ctypes.POINTER(DISPLAY_DEVICE),
    wintypes.DWORD,
]
user32.EnumDisplayDevicesW.restype = wintypes.BOOL

user32.EnumDisplaySettingsW.argtypes = [
    wintypes.LPCWSTR,
    wintypes.DWORD,
    ctypes.POINTER(DEVMODE),
]
user32.EnumDisplaySettingsW.restype = wintypes.BOOL

user32.ChangeDisplaySettingsExW.argtypes = [
    wintypes.LPCWSTR,
    ctypes.POINTER(DEVMODE),
    wintypes.HWND,
    wintypes.DWORD,
    wintypes.LPVOID,
]
user32.ChangeDisplaySettingsExW.restype = ctypes.c_long


def _new_devmode() -> DEVMODE:
    dm = DEVMODE()
    dm.dmSize = ctypes.sizeof(DEVMODE)
    return dm


def _new_display_device() -> DISPLAY_DEVICE:
    dd = DISPLAY_DEVICE()
    dd.cb = ctypes.sizeof(DISPLAY_DEVICE)
    return dd


def get_monitors() -> list:
    monitors = []
    index = 1
    adapter_idx = 0

    while True:
        dd = _new_display_device()
        if not user32.EnumDisplayDevicesW(None, adapter_idx, ctypes.byref(dd), 0):
            break
        adapter_idx += 1

        if not (dd.StateFlags & DISPLAY_DEVICE_ATTACHED_TO_DESKTOP):
            continue

        device_name = dd.DeviceName
        is_primary = bool(dd.StateFlags & DISPLAY_DEVICE_PRIMARY_DEVICE)

        # Try to get the monitor's friendly name from the second-level enumeration
        mon_dd = _new_display_device()
        friendly_name = dd.DeviceString.strip()
        if user32.EnumDisplayDevicesW(device_name, 0, ctypes.byref(mon_dd), 0):
            candidate = mon_dd.DeviceString.strip()
            if candidate:
                friendly_name = candidate

        dm = _new_devmode()
        if not user32.EnumDisplaySettingsW(device_name, ENUM_CURRENT_SETTINGS, ctypes.byref(dm)):
            continue

        monitors.append(MonitorInfo(
            device_name=device_name,
            friendly_name=friendly_name,
            x=dm.dmPositionX,
            y=dm.dmPositionY,
            width=dm.dmPelsWidth,
            height=dm.dmPelsHeight,
            refresh_rate=dm.dmDisplayFrequency,
            is_primary=is_primary,
            index=index,
        ))
        index += 1

    return monitors


def _rects_touch(a, b) -> bool:
    """True if rectangles a and b share an edge or overlap (no gap)."""
    ax0, ay0, ax1, ay1 = a.x, a.y, a.x + a.width, a.y + a.height
    bx0, by0, bx1, by1 = b.x, b.y, b.x + b.width, b.y + b.height
    # Horizontal overlap of the spans, allowing edge-adjacency
    h_overlap = ax0 <= bx1 and bx0 <= ax1
    v_overlap = ay0 <= by1 and by0 <= ay1
    return h_overlap and v_overlap


def find_stranded(monitors: list) -> list:
    """
    Return monitors that don't touch any other monitor (gap on all sides).
    A contiguous layout is required by Windows; stranded monitors cause
    DISP_CHANGE_FAILED. With a single monitor, nothing is stranded.
    """
    if len(monitors) < 2:
        return []
    stranded = []
    for m in monitors:
        if not any(_rects_touch(m, o) for o in monitors if o is not m):
            stranded.append(m)
    return stranded


def apply_monitors(monitors: list) -> tuple:
    """
    Apply new X/Y positions using the two-phase commit pattern:
      Phase 1 — per-monitor UPDATEREGISTRY+NORESET
      Phase 2 — final NULL commit

    Returns (success: bool, error_message: str).

    Positions are normalized relative to the PRIMARY monitor, which Windows
    always pins to (0,0). Every other monitor is positioned relative to it
    (negative coordinates are valid). Anchoring on the bounding-box corner
    instead would leave the primary at a nonzero position and the driver
    rejects that contradiction with DISP_CHANGE_FAILED (-1).
    """
    if not monitors:
        return False, 'No monitors to apply.'

    # Anchor on the primary monitor so it lands exactly at (0,0).
    primary = next((m for m in monitors if m.is_primary), monitors[0])
    anchor_x, anchor_y = primary.x, primary.y

    for m in monitors:
        dm = _new_devmode()
        if not user32.EnumDisplaySettingsW(m.device_name, ENUM_CURRENT_SETTINGS, ctypes.byref(dm)):
            return False, f'Could not read settings for {m.device_name}'

        dm.dmPositionX = m.x - anchor_x
        dm.dmPositionY = m.y - anchor_y
        dm.dmFields = DM_POSITION

        ret = user32.ChangeDisplaySettingsExW(
            m.device_name,
            ctypes.byref(dm),
            None,
            CDS_UPDATEREGISTRY | CDS_NORESET,
            None,
        )
        if ret < 0:
            return False, f'Failed on {m.friendly_name}: {_explain(ret)}'

    # Commit all changes simultaneously
    ret = user32.ChangeDisplaySettingsExW(None, None, None, 0, None)
    if ret < 0:
        return False, f'Commit failed: {_explain(ret)}'

    return True, ''
