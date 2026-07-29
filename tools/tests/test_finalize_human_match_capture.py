import json
import struct
import tempfile
import unittest
from pathlib import Path

from tools.finalize_human_match_capture import finalize


class FinalizeHumanMatchCaptureTests(unittest.TestCase):
    def test_correlates_client_actions_and_server_frames(self):
        with tempfile.TemporaryDirectory() as temporary:
            directory = Path(temporary)
            self.write_session(directory)
            self.write_client_actions(directory)
            self.write_server_event(directory)

            finalize(directory)

            events = [
                json.loads(line)
                for line in (directory / "timeline.jsonl")
                .read_text(encoding="utf-8").splitlines()
            ]
            self.assertEqual(3, len(events))
            self.assertEqual([10, 20, 30],
                             [event["relative_ms"] for event in events])
            self.assertEqual(["Attack", "W"],
                             events[1]["decoded"]["controls"])
            summary = (directory / "summary.md").read_text(encoding="utf-8")
            self.assertIn("Eventos correlacionados: `3`", summary)
            self.assertIn("`W`: 1", summary)
            manifest = json.loads(
                (directory / "manifest.json").read_text(encoding="utf-8"))
            self.assertTrue(any(
                item["name"] == "timeline.csv" for item in manifest["files"]))

    @staticmethod
    def write_session(directory: Path):
        (directory / "session.json").write_text(json.dumps({
            "startUtc": "2026-07-29T12:00:00+00:00",
            "startTick": 1000,
            "clientRoots": ["client-a", "client-b"],
        }), encoding="utf-8")

    @staticmethod
    def write_client_actions(directory: Path):
        movement = bytes.fromhex(
            "650020005E01000092090000A5000000000000")
        (directory / "openrakion_action_capture_42.csv").write_text(
            f"1010,030A,{len(movement)},{movement.hex().upper()}\r\n",
            encoding="ascii")

        action = bytearray(76)
        action[0x10:0x14] = (0x21).to_bytes(4, "little")
        struct.pack_into("<fff", action, 0x40, 1.0, 0.0, -6.0)
        (directory / "openrakion_player_action_42.csv").write_text(
            f"1020,00ABCDEF,{action.hex().upper()}\r\n",
            encoding="ascii")

    @staticmethod
    def write_server_event(directory: Path):
        event = {
            "utc": "2026-07-29T12:00:00.030+00:00",
            "tick": 1030,
            "channel": "TCP",
            "direction": "C2S",
            "field": 1,
            "seat": 0,
            "status": "InField",
            "type": "0x004F",
            "length": 2,
            "detail": "opcode=0x004F",
            "hex": "0801",
        }
        (directory / "server_match.jsonl").write_text(
            json.dumps(event) + "\n", encoding="utf-8")


if __name__ == "__main__":
    unittest.main()
