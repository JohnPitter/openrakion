import unittest

from tools.decode_gameplay_p2p import decode


class DecodeGameplayP2PTests(unittest.TestCase):
    @staticmethod
    def entity_event(event_id: int, payload: bytes) -> bytes:
        return (bytes.fromhex("0C83010000000000010000")
                + event_id.to_bytes(4, "little")
                + len(payload).to_bytes(4, "little") + payload)

    def test_decodes_captured_player_action_state_and_code(self):
        packet = bytes.fromhex("0A032700000000650020005E01000092090000A5000000000000")

        result = decode(packet)

        self.assertEqual("Attack", result["body"]["player_action_state"])
        self.assertEqual(0, result["body"]["source_echo"])
        self.assertEqual("None", result["body"]["action_name"])
        self.assertEqual([350, 0, 2450], result["body"]["position"])

    def test_decodes_direct_reliable_entity(self):
        header = (0x8308).to_bytes(2, "little") + (1).to_bytes(4, "little") + bytes([4])
        body = bytes([4, 1]) + (7).to_bytes(2, "little") + bytes(24)

        result = decode(header + body)

        self.assertEqual("0x0308", result["logical_type"])
        self.assertTrue(result["reliable"])
        self.assertEqual(1, result["sequence"])
        self.assertEqual(4, result["source_slot"])
        self.assertEqual(7, result["body"]["entity_field"])

    def test_decodes_relay_header_and_entity_state(self):
        relay = bytes.fromhex("1234567890abcdef")
        header = (0x030B).to_bytes(2, "little") + (9).to_bytes(4, "little") + bytes([2])
        body = ((15).to_bytes(2, "little") + bytes([4, 0, 1])
                + (10).to_bytes(2, "little", signed=True)
                + (-20).to_bytes(2, "little", signed=True)
                + (30).to_bytes(2, "little", signed=True)
                + (-90).to_bytes(2, "little", signed=True))

        result = decode(relay + header + body)

        self.assertEqual(relay.hex(), result["relay_header_hex"])
        self.assertEqual([10, -20, 30], result["body"]["position"])
        self.assertEqual(-90, result["body"]["heading"])

    def test_decodes_transport_ack(self):
        packet = bytes.fromhex("0040a30000000a9a000000")

        result = decode(packet)

        self.assertEqual("0x4000", result["logical_type"])
        self.assertEqual(0x9A, result["ack_sequence"])

    def test_rejects_truncated_map_item_snapshot(self):
        packet = ((0x8312).to_bytes(2, "little") + (1).to_bytes(4, "little")
                  + bytes([0, 2, 1, 1]))

        with self.assertRaisesRegex(ValueError, "count/tamanho"):
            decode(packet)

    def test_decodes_captured_reliable_player_event(self):
        packet = bytes.fromhex("0C839A00000000000100002A0091010400000001000000")

        result = decode(packet)

        self.assertEqual("0x030C", result["logical_type"])
        self.assertEqual(1, result["body"]["entity_route"])
        self.assertEqual(0x0191002A, result["body"]["event_type"])
        self.assertEqual("01000000", result["body"]["event_payload_hex"])

    def test_decodes_captured_player_vitals(self):
        packet = bytes.fromhex(
            "0C839D00000000000100000C0091010C000000000000000000C2420000C242")

        result = decode(packet)

        self.assertEqual(0, result["body"]["player_vitals"]["player_id"])
        self.assertEqual(97.0, result["body"]["player_vitals"]["hp"])
        self.assertEqual(97.0, result["body"]["player_vitals"]["ap"])

    def test_decodes_player_damage_event(self):
        payload = bytes.fromhex(
            "070000000B0434120000C03F0000204000004040000080400000A040"
            "0000C0400000E04000000041")

        result = decode(self.entity_event(0x0191000B, payload))["body"]

        self.assertEqual("EPlayerDamage", result["event_name"])
        self.assertEqual(11, result["player_damage"]["damage_type"])
        self.assertEqual(4, result["player_damage"]["damage_motion_type"])
        self.assertEqual(1.5, result["player_damage"]["first_damage_value"])
        self.assertEqual([6.0, 7.0, 8.0], result["player_damage"]["second_vector"])

    def test_decodes_player_death_vector(self):
        payload = bytes.fromhex("000080BF0000003F00001040")

        result = decode(self.entity_event(0x01910016, payload))["body"]

        self.assertEqual("EPlayerDeath", result["event_name"])
        self.assertEqual([-1.0, 0.5, 2.25], result["player_death"]["death_vector"])

    def test_decodes_potion_event(self):
        packet = (bytes.fromhex("0C83010000000000010000")
                  + (0x01910025).to_bytes(4, "little")
                  + (8).to_bytes(4, "little")
                  + (7).to_bytes(4, "little")
                  + (-2).to_bytes(4, "little", signed=True))

        result = decode(packet)

        self.assertEqual("EUsePotion", result["body"]["event_name"])
        self.assertEqual({"kind": 7, "argument": -2}, result["body"]["potion"])

    def test_decodes_gold_sword_event(self):
        packet = (bytes.fromhex("0C83020000000100010000")
                  + (0x044D000B).to_bytes(4, "little")
                  + (4).to_bytes(4, "little") + bytes([1, 0, 0, 0]))

        result = decode(packet)

        self.assertEqual("EGoldSword", result["body"]["event_name"])
        self.assertEqual({"enabled": True, "secondary": 0},
                         result["body"]["gold_sword"])

    def test_decodes_weapon_and_shuriken_event_payloads(self):
        shot = bytes.fromhex(
            "0000803F0000004000004040000080400000A0400000C04002010203")
        shuriken = bytes.fromhex(
            "00001041000000410000E0400000C0400000A040000080400901BBAA")

        shot_result = decode(self.entity_event(0x01910007, shot))["body"]
        shuriken_result = decode(self.entity_event(0x01910008, shuriken))["body"]

        self.assertEqual("EShootWeapon", shot_result["event_name"])
        self.assertEqual([1.0, 2.0, 3.0], shot_result["shoot_weapon"]["first_vector"])
        self.assertEqual(2, shot_result["shoot_weapon"]["shoot_type"])
        self.assertEqual("010203", shot_result["shoot_weapon"]["reserved_hex"])
        self.assertEqual(9, shuriken_result["shoot_shuriken"]["projectile_count"])
        self.assertEqual(0xAABB, shuriken_result["shoot_shuriken"]["reserved"])

    def test_decodes_hold_attack_event_payloads(self):
        request = bytes.fromhex("443322110A0B34120000484288776655")
        hold = bytes.fromhex("04030201050608070D0C0B0A090AD0C0")

        request_result = decode(self.entity_event(0x01910009, request))["body"]
        hold_result = decode(self.entity_event(0x0191000A, hold))["body"]

        self.assertEqual(50.0,
                         request_result["request_hold_attack"]["maximum_distance"])
        self.assertEqual(0x11223344,
                         request_result["request_hold_attack"]["entity_word"])
        self.assertEqual(0x0A0B0C0D, hold_result["hold_attack"]["argument"])
        self.assertEqual(0xC0D0, hold_result["hold_attack"]["reserved_1"])


if __name__ == "__main__":
    unittest.main()
