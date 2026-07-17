# -*- coding: utf-8 -*-
# Extrai classes, eventos, assets e handlers da familia Dragon na build v258.
# @category Rakion
from NpcFamilyExtractor import extract_family


CONFIG = {
    "name": "Dragon",
    "default_output": r"C:\temp\client_npc_dragon.txt",
    "descriptors": (0x3538AC00, 0x3538AC60, 0x3538ACC0, 0x3538AD20),
    "set_defaults": (0x350F0C80, 0x350FD110),
    "event_table": 0x3538AD50,
    "event_count": 27,
    "default_event": 0x3538AF00,
    "assets": (
        0x352D3128,
        0x352D314C,
        0x352D3174,
        0x352D319C,
        0x352D31BC,
        0x352D31E4,
        0x352D31F8,
        0x352D3208,
        0x352D3220,
        0x352D322C,
        0x352D3244,
        0x352D3268,
    ),
    "scalars": (
        ("flight_upper_bound", 0x40B00000),
        ("flight_response_scale", 0xC0000000),
        ("flight_target_height", 0x40A80000),
        ("movement_response_a", 0x3FCCCCCD),
        ("movement_response_b", 0x3FB33333),
    ),
    "helpers": (
        0x350F0C80,
        0x350FD070,
        0x350FD110,
        0x350FD160,
        0x350FD1F0,
        0x350FD280,
        0x350FD310,
        0x350FD3A0,
        0x350FEA30,
        0x350FF3B0,
    ),
}


extract_family(globals(), CONFIG)
