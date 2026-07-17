# -*- coding: utf-8 -*-
# Extrai classes, eventos, assets e handlers da familia SoulCannon na build v258.
# @category Rakion
from NpcFamilyExtractor import extract_family


CONFIG = {
    "name": "SoulCannon",
    "default_output": r"C:\temp\client_npc_soulcannon.txt",
    "descriptors": (0x3538C880, 0x3538C8E0, 0x3538C940, 0x3538C9A0),
    "set_defaults": (
        0x351207B0,
        0x35120F50,
        0x35121440,
        0x35121930,
        0x35123AF0,
    ),
    "event_table": 0x3538C9D0,
    "event_count": 29,
    "default_event": 0x3538CBA0,
    "assets": (
        0x352C5DBC,
        0x352C5DFD,
        0x352C5E2D,
        0x352C5E59,
        0x352D9914,
        0x352D9938,
        0x352D9980,
        0x352D99A8,
        0x352D99CC,
        0x352D9A14,
        0x352D9A24,
        0x352D9A34,
        0x352D9A44,
        0x352D9A70,
        0x352E3D0D,
        0x352E3D28,
        0x352B9280,
        0x352D24F4,
    ),
    "scalars": (
        ("near_attack_range", 0x40000000),
        ("mid_attack_range", 0x41200000),
        ("long_attack_range", 0x42480000),
        ("short_attack_delay", 0x40000000),
        ("long_attack_delay", 0x40400000),
        ("short_line_of_sight_probe", 0x41F00000),
        ("long_line_of_sight_probe", 0x42700000),
    ),
    "helpers": (0x35123AF0, 0x35124710, 0x351251B0),
}


extract_family(globals(), CONFIG)
