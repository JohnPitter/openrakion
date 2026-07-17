# -*- coding: utf-8 -*-
# Extrai classes, eventos, assets e handlers da familia IceWind na build v258.
# @category Rakion
from NpcFamilyExtractor import extract_family


CONFIG = {
    "name": "IceWind",
    "default_output": r"C:\temp\client_npc_icewind.txt",
    "descriptors": (0x3538B4C8, 0x3538B528, 0x3538B588, 0x3538B5E8),
    "set_defaults": (0x3510E0E0, 0x3510E190),
    "event_table": 0x3538B618,
    "event_count": 21,
    "default_event": 0x3538B768,
    "assets": (
        0x352CD0D4,
        0x352D5380,
        0x352D53A8,
        0x352CA4D4,
        0x352C76D4,
        0x352CA2C4,
        0x352B7938,
        0x352D5030,
        0x352D5054,
        0x352D5078,
        0x352D5094,
        0x352D50B8,
        0x352B7098,
        0x352DE8C0,
        0x352DE8A4,
        0x352DE888,
        0x352DE86C,
        0x352DE8DC,
    ),
    "scalars": (
        ("los_probe", 0x41A00000),
        ("flight_band_interp", 0x3F000000),
        ("flight_response_hi", 0x3FC00000),
        ("flight_response_lo", 0xBFC00000),
        ("selector_retry_delay", 0x3E800000),
        ("loadice_sound_volume", 0x40A00000),
        ("no_target_sentinel", 0x4E6E6B28),
    ),
    "raw_records": (
        ("IceWindClassDef", 0x3538B780, 6),
        ("CIceWindClassDef", 0x35387F40, 6),
        ("CIceWindEventTable", 0x35387F04, 12),
        ("IceWindProperties", 0x353A62C0, 40),
        ("IceWindComponents", 0x353A6360, 24),
        ("CIceWindProperties", 0x3539F100, 56),
        ("CIceWindComponents", 0x3539F1E0, 16),
    ),
    "helpers": (
        0x3510A8B0,
        0x3510ACC0,
        0x3510B050,
        0x3510B3E0,
        0x3510E190,
        0x3510E0E0,
        0x3510BAB0,
        0x350AF960,
        0x350AFB80,
        0x350AF010,
        0x350AF6D0,
        0x350AEBC0,
        0x350AF900,
        0x350AEA20,
        0x350AF100,
    ),
}


extract_family(globals(), CONFIG)
