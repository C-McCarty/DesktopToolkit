import json
from models import MonitorInfo


def save_profile(monitors: list, path: str, name: str = '') -> None:
    data = {
        'name': name,
        'monitors': [
            {
                'device_name': m.device_name,
                'friendly_name': m.friendly_name,
                'x': m.x,
                'y': m.y,
            }
            for m in monitors
        ],
    }
    with open(path, 'w', encoding='utf-8') as f:
        json.dump(data, f, indent=2)


def load_profile(path: str) -> tuple:
    """
    Returns (entries: list[dict], name: str) or raises on parse error.
    Each entry has keys: device_name, x, y.
    """
    with open(path, 'r', encoding='utf-8') as f:
        data = json.load(f)
    entries = data.get('monitors', [])
    name = data.get('name', '')
    return entries, name


def apply_profile_to_monitors(entries: list, monitors: list) -> list:
    """
    Match saved entries to live monitors by device_name and return the list of
    (monitor, new_x, new_y) tuples for monitors that have a saved position.
    """
    by_device = {e['device_name']: e for e in entries}
    matched = []
    for m in monitors:
        if m.device_name in by_device:
            entry = by_device[m.device_name]
            matched.append((m, entry['x'], entry['y']))
    return matched
