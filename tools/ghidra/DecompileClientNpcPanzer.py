# -*- coding: utf-8 -*-
# Extrai classes, eventos, assets e handlers da familia Panzer na build v258.
# @category Rakion
from NpcFamilyExtractor import extract_family


CONFIG = {
    "name": "Panzer",
    "default_output": r"C:\temp\client_npc_panzer.txt",
    "descriptors": (0x3538C348, 0x3538C3A8, 0x3538C408, 0x3538C468),
    "set_defaults": (0x3511B890, 0x3511BBF0, 0x3511BF50, 0x3511C2B0, 0x3511C610),
    "event_table": 0x3538C498,
    "event_count": 34,
    "default_event": 0x3538C6B8,
    "assets": (
        0x352D7DBC,
        0x352D828C,
        0x352D8510,
        0x352D8770,
        0x352D89D0,
        0x352D89E0,
        0x352D89F4,
        0x352D8A28,
        0x352D8A4C,
        0x352D8A6C,
        0x352D8A8C,
        0x352D8AB4,
        0x352B9280,
        0x352D24F4,
        0x352D8268,
        0x352D84E8,
    ),
    "scalars": (
        ("close_attack_split", 0x3F19999A),
        ("attack_variant_split", 0x3F666666),
    ),
}


extract_family(globals(), CONFIG)
