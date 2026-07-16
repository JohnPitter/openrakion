import argparse
import json
import struct
import sys


ENTITY_TYPES = {0x0307, 0x0308, 0x0309, 0x030B, 0x030C, 0x0310, 0x0312}
EVENT_NAMES = {
    0x01910006: "ESetWeapon",
    0x01910007: "EShootWeapon",
    0x01910008: "EShootShuriken",
    0x01910009: "ERequestHoldAttack",
    0x0191000A: "EHoldAttack",
    0x0191000B: "EPlayerDamage",
    0x0191000C: "EPlayerRemainHP",
    0x01910016: "EPlayerDeath",
    0x01910017: "ERespawn",
    0x01910025: "EUsePotion",
    0x044D000B: "EGoldSword",
    0x044D0015: "EMasterGolemDamage",
    0x04650000: "EMasterGolemRespawn",
    0x04690000: "EGoldGolemRespawn",
    0x04690001: "EGoldGolemRebirth",
}
PLAYER_ACTION_STATES = ["Normal", "Attack", "Damage", "NoState"]
PLAYER_ACTION_CODES = [
    "None", "Stand", "Idle00", "Idle01", "Forward", "Backward", "Left", "Right",
    "ForwardLeft", "ForwardRight", "BackwardLeft", "BackwardRight", "Jump", "Land",
    "Rise", "RollFront", "RollBack", "Guard", "StruckGuard", "GuardMove",
    "GuardBackward", "GuardLeft", "GuardRight", "GuardForwardLeft", "GuardForwardRight",
    "GuardBackwardLeft", "GuardBackwardRight", "WeaponChangeTo1", "WeaponChangeTo2",
    "TryHold", "TurnLeft", "TurnRight",
]


def u16(data: bytes, offset: int) -> int:
    return int.from_bytes(data[offset:offset + 2], "little")


def u32(data: bytes, offset: int) -> int:
    return int.from_bytes(data[offset:offset + 4], "little")


def s16(data: bytes, offset: int) -> int:
    return int.from_bytes(data[offset:offset + 2], "little", signed=True)


def f32(data: bytes, offset: int) -> float:
    return struct.unpack_from("<f", data, offset)[0]


def vector3(data: bytes, offset: int) -> list[float]:
    return [f32(data, offset), f32(data, offset + 4), f32(data, offset + 8)]


def plausible_type(raw_type: int) -> bool:
    logical = raw_type & 0x7FFF
    return logical in ENTITY_TYPES or logical in {
        0x0203, 0x0304, 0x0305, 0x030A, 0x030D, 0x030F, 0x0311,
        0x0313, 0x0315, 0x0319, 0x4000,
    }


def transport_offset(data: bytes) -> int:
    if len(data) >= 7 and plausible_type(u16(data, 0)):
        return 0
    if len(data) >= 15 and plausible_type(u16(data, 8)):
        return 8
    raise ValueError("tipo P2P direto/relay não reconhecido")


def decode_entity(logical_type: int, payload: bytes) -> dict:
    if logical_type == 0x030A and len(payload) == 19:
        packed_state = payload[2]
        action_code = payload[3]
        result = {
            "delta_milliseconds": u16(payload, 0),
            "source_echo": packed_state & 0x1F,
            "player_action_state": PLAYER_ACTION_STATES[(packed_state >> 5) & 0x03],
            "action_code": action_code,
            "position": [s16(payload, 4), s16(payload, 6), s16(payload, 8)],
            "angle_word": s16(payload, 10),
            "angle_byte": payload[12],
            "action_vector": [s16(payload, 13), s16(payload, 15), s16(payload, 17)],
        }
        if action_code < len(PLAYER_ACTION_CODES):
            result["action_name"] = PLAYER_ACTION_CODES[action_code]
        return result
    if logical_type in {0x0307, 0x0308, 0x0309} and len(payload) >= 28:
        return {
            "first_index": payload[0],
            "second_index": payload[1],
            "entity_field": u16(payload, 2),
            "placement_hex": payload[4:28].hex(),
            "init_blob_hex": payload[28:].hex(),
        }
    if logical_type == 0x030B and len(payload) == 13:
        if payload[2] not in {2, 3, 4}:
            raise ValueError("kind de entidade 0x030B inválido")
        return {
            "timing_or_state": u16(payload, 0),
            "entity_kind": payload[2],
            "group_index": payload[3],
            "entity_index": payload[4],
            "position": [s16(payload, 5), s16(payload, 7), s16(payload, 9)],
            "heading": s16(payload, 11),
        }
    if logical_type == 0x030C and len(payload) >= 12:
        length = u32(payload, 8)
        if len(payload) != 12 + length:
            raise ValueError("tamanho interno do evento 0x030C diverge do datagrama")
        event_type = u32(payload, 4)
        result = {
            "sender_slot": payload[0],
            "entity_route": payload[1],
            "primary_entity_slot": payload[2],
            "secondary_entity_slot": payload[3],
            "event_type": event_type,
            "declared_length": length,
            "event_payload_hex": payload[12:12 + length].hex(),
        }
        if event_type in EVENT_NAMES:
            result["event_name"] = EVENT_NAMES[event_type]
        event_payload = payload[12:12 + length]
        if event_type == 0x01910006 and length == 8:
            result["set_weapon"] = {
                "weapon_selector": int.from_bytes(event_payload[0:4], "little", signed=True),
                "argument": int.from_bytes(event_payload[4:8], "little", signed=True),
            }
        elif event_type == 0x01910007 and length == 28:
            result["shoot_weapon"] = {
                "first_vector": vector3(event_payload, 0),
                "second_vector": vector3(event_payload, 12),
                "shoot_type": event_payload[24],
                "reserved_hex": event_payload[25:28].hex(),
            }
        elif event_type == 0x01910008 and length == 28:
            result["shoot_shuriken"] = {
                "first_vector": vector3(event_payload, 0),
                "second_vector": vector3(event_payload, 12),
                "projectile_count": event_payload[24],
                "variant": event_payload[25],
                "reserved": u16(event_payload, 26),
            }
        elif event_type == 0x01910009 and length == 16:
            result["request_hold_attack"] = {
                "entity_word": u32(event_payload, 0),
                "entity_index": event_payload[4],
                "entity_sub_index": event_payload[5],
                "reserved": u16(event_payload, 6),
                "maximum_distance": f32(event_payload, 8),
                "argument": u32(event_payload, 12),
            }
        elif event_type == 0x0191000A and length == 16:
            result["hold_attack"] = {
                "entity_word": u32(event_payload, 0),
                "entity_index": event_payload[4],
                "entity_sub_index": event_payload[5],
                "reserved_0": u16(event_payload, 6),
                "argument": u32(event_payload, 8),
                "actor_index": event_payload[12],
                "actor_sub_index": event_payload[13],
                "reserved_1": u16(event_payload, 14),
            }
        elif event_type == 0x0191000B and length == 40:
            result["player_damage"] = {
                "player_id": u32(event_payload, 0),
                "damage_type": event_payload[4],
                "damage_motion_type": event_payload[5],
                "reserved": u16(event_payload, 6),
                "first_damage_value": f32(event_payload, 8),
                "second_damage_value": f32(event_payload, 12),
                "first_vector": vector3(event_payload, 16),
                "second_vector": vector3(event_payload, 28),
            }
        elif event_type == 0x0191000C and length == 12:
            result["player_vitals"] = {
                "player_id": u32(payload, 12),
                "hp": f32(payload, 16),
                "ap": f32(payload, 20),
            }
        elif event_type == 0x01910016 and length == 12:
            result["player_death"] = {"death_vector": vector3(event_payload, 0)}
        elif event_type == 0x01910025 and length == 8:
            result["potion"] = {
                "kind": u32(payload, 12),
                "argument": int.from_bytes(payload[16:20], "little", signed=True),
            }
        elif event_type == 0x044D000B and length == 4:
            result["gold_sword"] = {
                "enabled": bool(payload[12]),
                "secondary": payload[13],
            }
        elif event_type == 0x044D0015 and length == 8:
            result["master_golem_values"] = [u32(payload, 12), u32(payload, 16)]
        return result
    if logical_type == 0x0310 and len(payload) >= 3:
        return {"source": payload[0], "entity_kind": payload[1], "entity_index": payload[2]}
    if logical_type == 0x0312 and payload:
        count = payload[0]
        if count > 0x41 or len(payload) != 1 + count * 2:
            raise ValueError("count/tamanho de 0x0312 divergente")
        pairs = [list(payload[index:index + 2]) for index in range(1, len(payload), 2)]
        return {"count": count, "items": pairs}
    return {"payload_hex": payload.hex()}


def decode(data: bytes) -> dict:
    offset = transport_offset(data)
    raw_type = u16(data, offset)
    logical_type = raw_type & 0x7FFF
    result = {
        "relay_header_hex": data[:offset].hex() or None,
        "raw_type": f"0x{raw_type:04X}",
        "logical_type": f"0x{logical_type:04X}",
        "reliable": bool(raw_type & 0x8000),
        "sequence": u32(data, offset + 2),
        "source_slot": data[offset + 6],
    }
    payload = data[offset + 7:]
    if logical_type == 0x4000 and len(payload) >= 4:
        result["ack_sequence"] = u32(payload, 0)
    else:
        result["body"] = decode_entity(logical_type, payload)
    return result


def input_lines(values: list[str]):
    if values:
        yield from values
        return
    yield from sys.stdin


def main() -> None:
    parser = argparse.ArgumentParser(description="Decodifica datagramas UDP/P2P do Rakion v258")
    parser.add_argument("hex", nargs="*", help="datagrama hexadecimal; sem argumentos, lê stdin")
    args = parser.parse_args()
    for line in input_lines(args.hex):
        value = "".join(line.split())
        if not value:
            continue
        try:
            print(json.dumps(decode(bytes.fromhex(value)), ensure_ascii=False))
        except (ValueError, IndexError) as error:
            print(json.dumps({"error": str(error), "hex": value}), file=sys.stderr)


if __name__ == "__main__":
    main()
