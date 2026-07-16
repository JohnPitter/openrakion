import unittest

from tools.extract_entity_event_catalog import (
    EventRuntime,
    build_export_inventory,
    format_ghidra_input,
    format_markdown,
    parse_runtime_report,
    validate_runtime,
    validate_runtime_source,
)


class ExtractEntityEventCatalogTests(unittest.TestCase):
    def test_builds_sorted_event_export_inventory(self):
        exports = {
            "?GetSizeOf@EZeta@@UAEJXZ": 0x20,
            "??0EZeta@@QAE@XZ": 0x30,
            "?GetSizeOf@EAlpha@@UAEJXZ": 0x10,
            "?GetSizeOf@NotAnEvent@@UAEJXZ": 0x40,
        }

        result = build_export_inventory(exports, 0x35000000)

        self.assertEqual(["EAlpha", "EZeta"], [item.name for item in result])
        self.assertIsNone(result[0].constructor_va)
        self.assertEqual(0x35000030, result[1].constructor_va)
        self.assertIn("EZeta\t0x35000020\t0x35000030", format_ghidra_input(result))

    def test_parses_and_formats_runtime_catalog(self):
        report = (
            "name\tget_size_va\tconstructor_va\ttotal_size\tevent_id\tstatus\n"
            "EAlpha\t0x35100000\t0x35100010\t0x10\t0x01910001\tok\n"
        )

        events = parse_runtime_report(report)
        validate_runtime(events)
        markdown = format_markdown(events, "ABCD")

        self.assertEqual(16, events[0].total_size)
        self.assertIn("`0x01910001`", markdown)
        self.assertIn("| 16 | 8 |", markdown)
        self.assertIn("`abcd`", markdown)

    def test_allows_event_ids_scoped_by_entity_class(self):
        events = [
            EventRuntime("EA", 1, 2, 8, 0x100, "ok"),
            EventRuntime("EB", 3, 4, 8, 0x100, "ok"),
        ]

        validate_runtime(events)

    def test_rejects_duplicate_event_names(self):
        events = [
            EventRuntime("EA", 1, 2, 8, 0x100, "ok"),
            EventRuntime("EA", 3, 4, 8, 0x101, "ok"),
        ]

        with self.assertRaisesRegex(ValueError, "duplicado"):
            validate_runtime(events)

    def test_rejects_stale_runtime_addresses(self):
        exports = build_export_inventory({
            "?GetSizeOf@EA@@UAEJXZ": 0x10,
            "??0EA@@QAE@XZ": 0x20,
        }, 0x35000000)
        runtime = [EventRuntime("EA", 0x35000011, 0x35000020, 8, 1, "ok")]

        with self.assertRaisesRegex(ValueError, "endereços runtime"):
            validate_runtime_source(runtime, exports)


if __name__ == "__main__":
    unittest.main()
