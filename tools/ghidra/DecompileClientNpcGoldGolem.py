# -*- coding: utf-8 -*-
# Extrai a classe especial NpcGoldGolem (Golden Sword mode) na build v258.
# @category Rakion
from NpcFamilyExtractor import extract_family


CONFIG = {
    "name": "GoldGolem",
    "default_output": r"C:\temp\client_npc_goldgolem.txt",
    "descriptors": (0x3538B0E8,),
    "set_defaults": (0x35101660,),
    "event_table": 0x3538AF78,
    "event_count": 20,
    "default_event": 0x3538B0B8,
    "assets": (),
    "raw_records": (
        ("GoldGolemProperties", 0x353A5CD0, 56),
        ("GoldGolemComponents", 0x353A5DB0, 54),
    ),
    "helpers": (0x35101660,),
}


extract_family(globals(), CONFIG)
