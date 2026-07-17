# -*- coding: utf-8 -*-
# Extrai classes, eventos, assets e handlers da familia Blazer na build v258.
# @category Rakion
from NpcFamilyExtractor import extract_family


CONFIG = {
    "name": "Blazer",
    "default_output": r"C:\temp\client_npc_blazer.txt",
    "descriptors": (0x3538BB38, 0x3538BB98, 0x3538BBF8, 0x3538BC58),
    "set_defaults": (0x35111130, 0x351114C0, 0x35111D90, 0x35112120, 0x351124B0),
    "event_table": 0x3538BC88,
    "event_count": 13,
    "default_event": 0x3538BD58,
    "assets": (
        0x352D60E4,
        0x352D6364,
        0x352D65E8,
        0x352D6848,
        0x352D08E4,
        0x352D6340,
        0x352D65C0,
        0x352D6AA8,
        0x352D6ACC,
        0x352D6D40,
        0x352D6D68,
        0x352D6D70,
        0x352D6DB0,
        0x352B9280,
        0x352DFD84,
        0x352B7910,
        0x352E008C,
    ),
    "scalars": (
        ("projectile_attack_range", 0x42480000),
        ("line_of_sight_probe", 0x41A00000),
        ("projectile_attack_delay", 0x40400000),
    ),
    "helpers": (0x351134E0, 0x3517F420),
}


extract_family(globals(), CONFIG)
