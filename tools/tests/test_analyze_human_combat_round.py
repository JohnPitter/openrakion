import json
import tempfile
import unittest
from pathlib import Path

from tools.analyze_human_combat_round import analyze, write_report


class AnalyzeHumanCombatRoundTests(unittest.TestCase):
    def test_scopes_round_and_groups_attack_sequences(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            timeline = directory / "timeline.jsonl"
            events = [
                self.world(100, "0x004B"),
                self.animation(110, 42, "Attack", [25]),
                self.animation(500, 42, "Attack", [24]),
                self.animation(700, 42, "Damage", [1, 2, 1]),
                self.world(900, "0x004F", seat=1),
                self.animation(1600, 42, "Attack", [10]),
                self.world(2000, "0x0050"),
                self.animation(2100, 42, "Attack", [99]),
            ]
            timeline.write_text(
                "".join(json.dumps(event) + "\n" for event in events),
                encoding="utf-8",
            )

            result = analyze(timeline)
            write_report(directory, result)

            self.assertEqual(1900, result["duration_ms"])
            self.assertEqual(1, result["attacks"]["42"][25])
            self.assertNotIn(99, result["attacks"]["42"])
            self.assertEqual(1, result["attack_sequences"]["42"][(25, 24)])
            self.assertEqual(1, result["attack_sequences"]["42"][(10,)])
            self.assertEqual(
                390,
                result["attack_transitions"]["42"][(25, 24)]["median_ms"],
            )
            self.assertEqual(1, len(result["deaths"]))
            self.assertTrue((directory / "round-analysis.md").exists())

    @staticmethod
    def world(relative_ms: int, type_value: str, seat: int = 0) -> dict:
        return {
            "relative_ms": relative_ms,
            "stream": "world",
            "direction": "C2S",
            "type": type_value,
            "seat": seat,
            "hex": "00",
        }

    @staticmethod
    def animation(
        relative_ms: int,
        pid: int,
        kind: str,
        arguments: list[int],
    ) -> dict:
        return {
            "relative_ms": relative_ms,
            "stream": "local_peer_action",
            "pid": pid,
            "decoded": {
                "body": {
                    "animation_kind": kind,
                    "arguments": arguments,
                }
            },
        }


if __name__ == "__main__":
    unittest.main()
