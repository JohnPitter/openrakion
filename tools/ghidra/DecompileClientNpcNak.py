# -*- coding: utf-8 -*-
# Extrai classes, eventos, assets e handlers da familia Nak na build v258.
# @category Rakion
from NpcFamilyExtractor import extract_family


CONFIG = {
    "name": "Nak",
    "default_output": r"C:\temp\client_npc_nak.txt",
    "descriptors": (0x3538BF98, 0x3538BFF8, 0x3538C058, 0x3538C0B8),
    "set_defaults": (0x35117D30, 0x35118140, 0x35118470, 0x351187A0, 0x35118AD0),
    "event_table": 0x3538C0E8,
    "event_count": 29,
    "default_event": 0x3538C2B8,
    "assets": (
        0x352D7CE8,
        0x352D7CF4,
        0x352D7D18,
        0x352D7D38,
        0x352D7D5C,
        0x352D7D80,
        0x352D7D8C,
        0x352D7D9C,
    ),
    "scalars": (("attack_range", 0x40400000),),
    "helpers": (
        0x35118B00,
        0x35118B10,
        0x35118B30,
        0x35118B90,
        0x35118F50,
        0x351191B0,
        0x35119DD0,
        0x35119E70,
        0x3511A520,
    ),
}


extract_family(globals(), CONFIG)
