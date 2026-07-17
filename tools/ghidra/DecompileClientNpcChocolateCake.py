# -*- coding: utf-8 -*-
# Extrai a classe especial NpcChocolateCake (evento) na build v258.
# @category Rakion
from NpcFamilyExtractor import extract_family


CONFIG = {
    "name": "ChocolateCake",
    "default_output": r"C:\temp\client_npc_chocolatecake.txt",
    "descriptors": (0x3538A790,),
    "set_defaults": (0x350F4D10,),
    "event_table": 0x3538A700,
    "event_count": 6,
    "default_event": 0x3538A760,
    "assets": (),
    "raw_records": (
        ("ChocolateCakeProperties", 0x353A5880, 40),
        ("ChocolateCakeComponents", 0x353A5920, 12),
    ),
    "helpers": (0x350F4D10,),
}


extract_family(globals(), CONFIG)
