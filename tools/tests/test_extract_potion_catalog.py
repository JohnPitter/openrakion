import struct
import sys
import unittest
from pathlib import Path


sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from extract_potion_catalog import potion_item_records, script_semantics


class PotionCatalogTests(unittest.TestCase):
    def test_item_record_resolves_shared_script(self):
        data = bytearray(500)
        struct.pack_into("<I", data, 20, 12001)
        data[24:26] = b"\0\0"
        struct.pack_into("<I", data, 26, 12000)
        data[30:39] = b"HP(30EA)"
        data[39] = 0
        script = b"Scripts\\item\\12000.lua"
        data[100:100 + len(script)] = script
        data[100 + len(script)] = 0
        description = b"Restore HP"
        start = 101 + len(script)
        data[start:start + len(description)] = description
        data[start + len(description)] = 0

        self.assertEqual([{
            "item_id": 12001,
            "family_id": 12000,
            "name": "HP(30EA)",
            "script_id": 12000,
            "description": "Restore HP",
        }], potion_item_records(bytes(data)))

    def test_restore_and_chaos_semantics(self):
        restore = script_semantics(12000, "pLocalPlayer:AddHP(pLocalPlayer:GetMaxHP() * 0.2);\n"
                                  "pLocalPlayer:UseHPPotion();")
        chaos = script_semantics(12060, "return pLocalPlayer:IsChargeChaosPoint();\n"
                                "pLocalPlayer:UseChaosPotion(2);")

        self.assertEqual("HP", restore["resource"])
        self.assertEqual(0.2, restore["ratio"])
        self.assertEqual("UseHPPotion", restore["sender"])
        self.assertEqual(2, chaos["argument"])
        self.assertEqual("IsChargeChaosPoint", chaos["guard"])


if __name__ == "__main__":
    unittest.main()
