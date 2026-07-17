# -*- coding: utf-8 -*-
# Extrai classes, eventos, assets e handlers da familia Taurus na build v258.
# @category Rakion
from NpcFamilyExtractor import extract_family


CONFIG = {
    "name": "Taurus",
    "default_output": r"C:\temp\client_npc_taurus.txt",
    "descriptors": (0x3538CC30, 0x3538CC90, 0x3538CCF0, 0x3538CD50),
    "set_defaults": (
        0x351260B0,
        0x35126410,
        0x35126FB0,
        0x351272E0,
        0x35127670,
    ),
    "event_table": 0x3538CD90,
    "event_count": 26,
    "default_event": 0x3538CF30,
    "assets": (
        0x352D9F64,
        0x352DA200,
        0x352DA710,
        0x352DA71C,
        0x352DA724,
        0x352DA740,
        0x352DA768,
        0x352DA77C,
        0x352DA790,
        0x352DA7C0,
        0x352B9280,
        0x352D24F4,
        0x352D24C8,
        0x352D24D0,
        0x352BCFB8,
        0x352BCFC0,
    ),
    "raw_records": (("TaurusEvent", 0x3538CD80, 4),),
    "scalars": (
        ("near_range", 0x40000000),
        ("mid_range", 0x41200000),
        ("far_range", 0x42480000),
        ("short_delay", 0x40000000),
        ("long_delay", 0x40400000),
        ("walk_state_scalar", 0x40400000),
        ("charge_state_scalar", 0x40800000),
        ("walk_state_limit", 0x437A0000),
        ("charge_state_limit", 0x435C0000),
        ("reaction_delay_type_2", 0x3FC00000),
        ("reaction_delay_type_3", 0x3F99999A),
        ("reaction_delay_type_4", 0x3F400000),
        ("startup_delay", 0x3E4CCCCD),
    ),
    "helpers": (
        0x35127640,
        0x35127670,
        0x35127800,
        0x35127870,
        0x35127970,
        0x351279F0,
        0x351289D0,
        0x35128C50,
        0x35128DD0,
        0x35128E70,
    ),
}


extract_family(globals(), CONFIG)
