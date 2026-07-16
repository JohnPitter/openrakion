import tempfile
import unittest
from pathlib import Path

from tools.extract_field_message_catalog import CASES, parse_catalog, render


class ExtractFieldMessageCatalogTests(unittest.TestCase):
    def test_parse_catalog_requires_all_nine_cases(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "catalog.tsv"
            rows = ["logical_type\tdispatcher"]
            rows.extend(f"0x{value:04X}\t0x3610D7C0" for value in CASES)
            path.write_text("\n".join(rows), encoding="utf-8")

            self.assertEqual(sorted(CASES), parse_catalog(path))

    def test_render_keeps_tcp_0c_separate_from_cnet_030c(self) -> None:
        output = render(sorted(CASES), "engine-sha", "rakion-sha")

        self.assertIn("9 cases de gameplay delegados", output)
        self.assertIn("15 cases diretos", output)
        self.assertIn("fila de requests DB no formato", output)
        self.assertIn("`UPDATE CharacterInfo.exp`", output)
        self.assertIn("WorldNet `0x58 [i32 remainingExp]`", output)


if __name__ == "__main__":
    unittest.main()
