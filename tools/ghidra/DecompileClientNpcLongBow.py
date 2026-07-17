# -*- coding: utf-8 -*-
# Extrai classes, eventos, assets e handlers da familia LongBow na build v258.
# @category Rakion
from NpcFamilyExtractor import extract_family


CONFIG = {
    "name": "LongBow",
    "default_output": r"C:\temp\client_npc_longbow.txt",
    "descriptors": (0x3538B7F8, 0x3538B858, 0x3538B8B8, 0x3538B918),
    "set_defaults": (
        0x3510E340,
        0x3510E670,
        0x3510E9A0,
        0x3510ECD0,
        0x3510F000,
    ),
    "event_table": 0x3538B948,
    "event_count": 22,
    "default_event": 0x3538BAA8,
    "assets": (
        0x352D5FA0,
        0x352D5FC4,
        0x352D6004,
        0x352D6028,
        0x352D6054,
        0x352D6080,
        0x352D6088,
        0x352D6094,
        0x352D60BC,
    ),
    "scalars": (
        ("near_range", 0x40000000),
        ("mid_range", 0x41200000),
        ("projectile_range", 0x42480000),
        ("short_delay", 0x40000000),
        ("projectile_delay", 0x40400000),
        ("line_of_sight_probe", 0x41A00000),
    ),
    "helpers": (0x3510F000, 0x3510F360),
}


extract_family(globals(), CONFIG)
