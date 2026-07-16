import struct
import tempfile
import unittest
from pathlib import Path

from tools.extract_cell_catalog import (
    CREATURE_RECORD_SIZE,
    build_catalog,
    normalize_alias,
    parse_creature_list,
    parse_item_record,
)


def item_record(item_id: int, name: str, model: str, level: int = 1) -> bytes:
    marker = struct.pack("<I", item_id) + b"\0\0" + struct.pack("<I", item_id)
    fixed = bytes((31, level)) + struct.pack("<II", 1000, 0) + bytes((1, 0, 8))
    return marker + name.encode("latin1") + b"\0" + b"\xff" * 4 + \
        model.encode("latin1") + b"\0" + fixed


class ExtractCellCatalogTests(unittest.TestCase):
    def test_creature_list_ignores_comments_and_blank_lines(self):
        with tempfile.TemporaryDirectory() as temp:
            path = Path(temp) / "creaturelist.txt"
            path.write_text("Classes\\NpcNak.ecl\n\n// comment\nClasses\\NpcPanzer.ecl\n",
                            encoding="latin1")
            self.assertEqual(
                ["Classes/NpcNak.ecl", "Classes/NpcPanzer.ecl"],
                parse_creature_list(path))

    def test_item_record_reads_name_model_and_fixed_fields(self):
        raw = b"prefix" + item_record(8000, "Nak", "ModelsSV\\NPC\\Nak\\Nak.smc", 4)
        item = parse_item_record(raw, 8000)
        self.assertEqual("Nak", item["name"])
        self.assertEqual("ModelsSV/NPC/Nak/Nak.smc", item["model"])
        self.assertEqual(4, item["required_level"])
        self.assertEqual(8, item["type"])

    def test_alias_normalizes_legacy_typos(self):
        self.assertEqual("blackpanzer", normalize_alias("BlackPenzer"))
        self.assertEqual("assaultpanzer", normalize_alias("AssultPanzer"))

    def test_catalog_binds_index_item_ecl_alias_and_record(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            creature_list = root / "creaturelist.txt"
            creature_list.write_text("Classes\\NpcNak.ecl\n", encoding="latin1")
            items = root / "items.dat"
            items.write_bytes(item_record(8000, "Nak", "ModelsSV\\NPC\\Nak\\Nak.smc"))
            stages = root / "levels"
            stages.mkdir()
            (stages / "stage_001.txt").write_text(
                "NpcSpawn: { class = [nak] } // class = [wrong]\n", encoding="latin1")
            creatures = root / "creatures.dat"
            record = bytearray(CREATURE_RECORD_SIZE)
            struct.pack_into("<I", record, 0x18C, 35)
            struct.pack_into("<H", record, 0x166E, 20)
            struct.pack_into("<H", record, 0x1734, 42)
            struct.pack_into("<H", record, 0x1A4C, 300)
            creatures.write_bytes(record + bytes(4 * 33))

            catalog = build_catalog(
                creature_list, items, stages, creatures, {8000})

            row = catalog["creatures"][0]
            self.assertEqual(8000, row["item_id"])
            self.assertEqual(["nak"], row["stage_aliases"])
            self.assertTrue(row["active_in_sql"])
            self.assertEqual(35, row["creatures_data"]["cumulative_cell_exp"][0])
            self.assertEqual(20, row["creatures_data"]["npc_kill_cp_reward"][0])
            self.assertEqual(42, row["creatures_data"]["summon_cp_cost"][0])
            self.assertEqual(300, row["creatures_data"]["unconsumed_field_1a4c"][0])
            self.assertEqual(132, catalog["creatures_data_trailing_bytes"])
            self.assertTrue(catalog["creatures_data_layout_complete"])
            self.assertEqual([], catalog["unmapped_stage_classes"])


if __name__ == "__main__":
    unittest.main()
