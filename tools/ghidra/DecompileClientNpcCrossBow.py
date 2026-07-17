# -*- coding: utf-8 -*-
# Extrai classes, eventos, assets e handlers da familia CrossBow na build v258.
# @category Rakion
from NpcFamilyExtractor import extract_family


CONFIG = {
    "name": "CrossBow",
    "default_output": r"C:\temp\client_npc_crossbow.txt",
    "descriptors": (0x3538A7F0, 0x3538A8A0, 0x3538A900, 0x3538A960),
    "set_defaults": (0x350F8050, 0x350F83B0, 0x350F9280, 0x350F95B0, 0x350F98E0),
    "event_table": 0x3538A990,
    "event_count": 30,
    "default_event": 0x3538AB70,
    "extra_event_tables": (
        ("CrossBow2", 0x3538A820, 5, 0x3538A870),
    ),
    "scalars": (
        ("close_attack_range", 0x41200000),
        ("projectile_attack_range", 0x42480000),
        ("line_of_sight_probe", 0x41A00000),
        ("close_attack_delay", 0x40000000),
        ("projectile_attack_delay", 0x40400000),
    ),
    "helpers": (0x350F9F20,),
    "assets": (
        0x352D16B4,
        0x352D1B88,
        0x352D1E78,
        0x352D20D8,
        0x352D2338,
        0x352D1B60,
        0x352D1E34,
        0x352D1DE8,
        0x352D1E10,
        0x352D1E1C,
        0x352D1E28,
        0x352D2348,
        0x352D2358,
        0x352D237C,
        0x352D2390,
        0x352D23A8,
        0x352D23B8,
        0x352D23CC,
        0x352D23DC,
        0x352D23E8,
        0x352D2410,
        0x352D2434,
        0x352D2448,
        0x352D2478,
        0x352D249C,
        0x352D24C8,
        0x352D24D0,
        0x352D24E8,
        0x352B9280,
        0x352D24F4,
        0x352D2500,
    ),
}


extract_family(globals(), CONFIG)
