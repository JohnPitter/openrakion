import struct
import unittest

from tools.decode_entity_init_blob import decode_init_blob


class DecodeEntityInitBlobTests(unittest.TestCase):
    def test_decodes_base_without_owner(self):
        blob = (struct.pack("<ffB", 1.5, 2.5, 7) + bytes([3]) + b"Nak"
                + bytes([2]) + (0x11223344).to_bytes(4, "little"))

        result = decode_init_blob("base", 2, blob)

        self.assertEqual("Nak", result["text"])
        self.assertEqual(2, result["linked_entity_state"])
        self.assertEqual("helper_zero", result["linked_entity_state_name"])
        self.assertEqual(0x11223344, result["property_7d0"])
        self.assertNotIn("owner_reference", result)

    def test_decodes_base_owner_sentinel(self):
        blob = (struct.pack("<ffB", 0.0, 0.0, 1) + bytes([0])
                + bytes([4, 0xFF, 0xFF, 0xFF, 0xFF, 0]) + bytes(4))

        result = decode_init_blob("base", 3, blob)

        self.assertEqual(4, result["owner_reference"]["owner_index"])
        self.assertFalse(result["owner_reference"]["resolved"])
        self.assertEqual("absent", result["linked_entity_state_name"])

    def test_decodes_gold_golem_alive_and_owner(self):
        blob = struct.pack("<ffB", 3.0, 4.0, 1) + bytes([2, 8, 9, 10, 11])

        result = decode_init_blob("gold_golem", 3, blob)

        self.assertTrue(result["is_alive"])
        self.assertTrue(result["owner_reference"]["resolved"])
        self.assertEqual(8, result["owner_reference"]["entity_class"])

    def test_decodes_chocolate_cake_type_three_tail(self):
        blob = (struct.pack("<ffB", 1.0, 1.0, 0) + bytes([1, 2, 3, 4, 5])
                + (99).to_bytes(4, "little"))

        result = decode_init_blob("chocolate_cake", 3, blob)

        self.assertFalse(result["is_alive"])
        self.assertEqual(99, result["property_7d0"])

    def test_rejects_truncated_and_excess_data(self):
        with self.assertRaisesRegex(ValueError, "truncado"):
            decode_init_blob("gold_golem", 2, bytes(8))
        with self.assertRaisesRegex(ValueError, "excedente"):
            decode_init_blob("gold_golem", 2, bytes(10))


if __name__ == "__main__":
    unittest.main()
