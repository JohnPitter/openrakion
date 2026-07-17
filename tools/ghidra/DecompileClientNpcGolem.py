# -*- coding: utf-8 -*-
# Extrai classes, eventos, assets e handlers da familia Golem na build v258.
# @category Rakion
from NpcFamilyExtractor import extract_family


CONFIG = {
    "name": "Golem",
    "default_output": r"C:\temp\client_npc_golem.txt",
    "descriptors": (0x3538B148, 0x3538B1A8, 0x3538B208, 0x3538B268),
    "set_defaults": (
        0x35106380,
        0x35106890,
        0x35106BC0,
        0x35106EF0,
        0x35107220,
        0x3510A100,
    ),
    "event_table": 0x3538B298,
    "event_count": 18,
    "default_event": 0x3538B3B8,
    "extra_event_tables": (
        ("GolemStoneDebris", 0x3538B428, 2, 0x3538B418),
    ),
    "assets": (
        0x352D0888,
        0x352D08A8,
        0x352D1148,
        0x352D32C8,
        0x352D3350,
        0x352D335C,
        0x352D35FD,
        0x352D371C,
        0x352D372C,
        0x352D375C,
        0x352D4365,
        0x352D4393,
        0x352D43CF,
        0x352D45AB,
        0x352D45E3,
        0x352D461B,
        0x352D4653,
        0x352D468B,
        0x352D8B3B,
        0x352D8D10,
    ),
    "scalars": (
        ("near_attack_range", 0x40000000),
        ("mid_attack_range", 0x41200000),
        ("long_attack_range", 0x42480000),
        ("short_attack_delay", 0x40000000),
        ("long_attack_delay", 0x40400000),
        ("short_line_of_sight_probe", 0x41F00000),
        ("long_line_of_sight_probe", 0x40A00000),
    ),
    "helpers": (0x35107220, 0x3510A100),
}


extract_family(globals(), CONFIG)
