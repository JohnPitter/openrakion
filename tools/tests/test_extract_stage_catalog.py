import tempfile
import unittest
from pathlib import Path

from tools.extract_stage_catalog import flow_audit, flow_nodes, load_catalog, parse_stage


STAGE = b"""{
Stage : type = [Stage]
 mapid = [3]
 name = [Test]
 time_limit = [288]
 goal = [butchery]
 goalvar = [40]
 rankvar = [class=S|value=100|gold=50|exp=75|multi=4.0]
 // rankvar = [class=D|value=999|gold=999|exp=999|multi=1.0]
 player_min_number = [1]
 player_max_number = [2]
 player_low_level = [2]
 player_high_level = [13]
}
{
NpcSpawn : name = [wave1]
 class = [Nak]
 level = [5]
 npcname = [a,b,c]
 varset = [friendly, marked as target]
}
{
Switch : name = [start]
 condition = [start]
 linktrigger = [spawn]
}
{
Trigger : name = [spawn]
 execution = [spawn npc]
 target = [wave1]
 linktrigger = [finish]
}
{
Trigger : name = [finish]
 execution = [win]
}
"""


class ExtractStageCatalogTests(unittest.TestCase):
    def test_parses_stage_rank_and_npc_spawn(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "stage_003.txt"
            path.write_bytes(STAGE)

            result = parse_stage(path)

        self.assertEqual(3, result["id"])
        self.assertEqual(288, result["time_limit"])
        self.assertEqual({"rank": "S", "value": 100, "gold": 50,
                          "exp": 75, "multiplier": 4.0}, result["ranks"][0])
        self.assertEqual(1, len(result["ranks"]))
        self.assertEqual(3, result["npc_spawns"][0]["count"])
        self.assertTrue(result["npc_spawns"][0]["friendly"])
        self.assertTrue(result["npc_spawns"][0]["target"])
        self.assertEqual(["switch:start"], result["flow_audit"]["roots"])
        self.assertEqual(["trigger:finish"], result["flow_audit"]["reachableWinTriggers"])
        self.assertEqual([], result["flow_audit"]["brokenReferences"])

    def test_flow_audit_reports_broken_and_unreachable_nodes(self):
        nodes = flow_nodes("""{Switch:name=[start] condition=[start] linktrigger=[missing]}
{Trigger:name=[orphan] execution=[win]}""")

        result = flow_audit(nodes)

        self.assertEqual(["trigger:orphan"], result["unreachableNodes"])
        self.assertEqual("missing", result["brokenReferences"][0]["target"])

    def test_catalog_orders_stage_ids_numerically(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "stage_010.txt").write_bytes(STAGE)
            (root / "stage_002.txt").write_bytes(STAGE)

            result = load_catalog(root)

        self.assertEqual([2, 10], [stage["id"] for stage in result])


if __name__ == "__main__":
    unittest.main()
