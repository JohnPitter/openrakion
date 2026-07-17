# -*- coding: utf-8 -*-
# Extrai a classe especial NpcMasterGolem (objetivo Golem War) na build v258.
# @category Rakion
from NpcFamilyExtractor import extract_family


CONFIG = {
    "name": "MasterGolem",
    "default_output": r"C:\temp\client_npc_mastergolem.txt",
    "descriptors": (0x3538BF38,),
    "set_defaults": (0x35115350,),
    "event_table": 0x3538BDC8,
    "event_count": 20,
    "default_event": 0x3538BF08,
    "assets": (),
    "raw_records": (
        ("MasterGolemProperties", 0x353A66D0, 32),
        ("MasterGolemComponents", 0x353A6750, 36),
    ),
    "helpers": (0x35115350,),
}


extract_family(globals(), CONFIG)
