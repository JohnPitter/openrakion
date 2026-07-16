import tempfile
import unittest
from pathlib import Path

from tools.extract_world_response_catalog import (
    SIMPLE_RESPONSE_CONTRACTS,
    parse_callbacks,
    parse_dispatch,
    response_family,
)


class WorldResponseCatalogTests(unittest.TestCase):
    def test_parse_rejects_incomplete_dispatch(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "dispatch.tsv"
            path.write_text(
                "opcode\thandler\tdestino\n0x00\tvirtual+0x15C\tcallback+0x15C\n",
                encoding="utf-8",
            )
            with self.assertRaisesRegex(ValueError, "88 cases"):
                parse_dispatch(path)

    def test_response_family_respects_state_boundaries(self):
        self.assertEqual("sessão/login", response_family(0x0C))
        self.assertEqual("personagem", response_family(0x14))
        self.assertEqual("canal", response_family(0x22))
        self.assertEqual("inventário/progressão", response_family(0x34))
        self.assertEqual("lista/sala", response_family(0x3B))
        self.assertEqual("field/partida", response_family(0x53))
        self.assertEqual("eventos/presentes", response_family(0x6B))
        self.assertEqual("lista/sala", response_family(0x72))
        self.assertEqual("inventário/progressão", response_family(0x74))

    def test_parse_callbacks_rejects_incomplete_catalog(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "callbacks.tsv"
            path.write_text(
                "opcode\tdestino\timplementacao\n0x00\tcallback+0x15C\t0x00472B50\n",
                encoding="utf-8",
            )
            with self.assertRaisesRegex(ValueError, "88 cases"):
                parse_callbacks(path)

    def test_simple_contracts_do_not_invent_scalar_for_raw_pointer(self):
        contracts = {opcode: layout for opcode, layout, _, _ in SIMPLE_RESPONSE_CONTRACTS}

        self.assertEqual("corpo não lido", contracts[0x67])
        self.assertEqual("corpo não lido", contracts[0x68])
        self.assertEqual("ponteiro bruto", contracts[0x69])
        self.assertEqual("ponteiro bruto", contracts[0x6A])


if __name__ == "__main__":
    unittest.main()
