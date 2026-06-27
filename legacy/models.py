from dataclasses import dataclass


@dataclass
class MonitorInfo:
    device_name: str
    friendly_name: str
    x: int
    y: int
    width: int
    height: int
    refresh_rate: int
    is_primary: bool
    index: int
