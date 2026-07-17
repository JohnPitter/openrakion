import sys
import unittest
from pathlib import Path


sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from extract_script_api_usage import find_api_usage


class ScriptApiUsageTests(unittest.TestCase):
    def test_reports_exact_api_names_with_lines(self):
        entries = [
            ("scripts\\item\\12070.lua", b"p:GetCP()\np:AddCP(p:GetMaxCP() * 0.3)\n"),
            ("scripts\\other.lua", b"local GetCPBonus = 1\n"),
        ]

        self.assertEqual([{
            "entry": "scripts\\item\\12070.lua",
            "apis": ["GetCP", "AddCP", "GetMaxCP"],
            "lines": [
                {"number": 1, "apis": ["GetCP"], "text": "p:GetCP()"},
                {
                    "number": 2,
                    "apis": ["AddCP", "GetMaxCP"],
                    "text": "p:AddCP(p:GetMaxCP() * 0.3)",
                },
            ],
        }], find_api_usage(entries, ("GetCP", "AddCP", "ReduceCP", "GetMaxCP")))


if __name__ == "__main__":
    unittest.main()
