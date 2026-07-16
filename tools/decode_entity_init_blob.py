import argparse
import json
import struct


FAMILIES = {"base", "gold_golem", "chocolate_cake"}


class BlobReader:
    def __init__(self, data: bytes):
        self.data = data
        self.offset = 0

    def take(self, length: int) -> bytes:
        end = self.offset + length
        if end > len(self.data):
            raise ValueError(
                f"init blob truncado em +0x{self.offset:X}: precisa de {length} byte(s)")
        value = self.data[self.offset:end]
        self.offset = end
        return value

    def u8(self) -> int:
        return self.take(1)[0]

    def u32(self) -> int:
        return int.from_bytes(self.take(4), "little")

    def f32(self) -> float:
        return struct.unpack("<f", self.take(4))[0]

    def finish(self) -> None:
        if self.offset != len(self.data):
            raise ValueError(
                f"init blob possui {len(self.data) - self.offset} byte(s) excedente(s)"
            )


def owner_reference(reader: BlobReader) -> dict:
    owner = {
        "owner_index": reader.u8(),
        "entity_class": reader.u8(),
        "entity_index_a": reader.u8(),
        "entity_index_b": reader.u8(),
        "entity_index_c": reader.u8(),
    }
    owner["resolved"] = owner["entity_class"] != 0xFF
    return owner


def decode_base(reader: BlobReader, entity_type: int) -> dict:
    result = {
        "property_26c": reader.f32(),
        "property_7b0": reader.f32(),
        "property_7c4": reader.u8(),
    }
    text = reader.take(reader.u8())
    result["text"] = text.decode("latin-1")
    result["text_hex"] = text.hex()
    if entity_type == 3:
        result["owner_reference"] = owner_reference(reader)
    link_state = reader.u8()
    result["linked_entity_state"] = link_state
    result["linked_entity_state_name"] = {
        0: "absent",
        1: "helper_nonzero",
        2: "helper_zero",
    }.get(link_state, "unknown")
    result["property_7d0"] = reader.u32()
    return result


def decode_gold_golem(reader: BlobReader, entity_type: int) -> dict:
    first = reader.f32()
    second = reader.f32()
    alive = reader.u8()
    result = {
        "property_38ec": first,
        "property_38e8": second,
        "is_alive": bool(alive),
        "is_alive_raw": alive,
    }
    if entity_type == 3:
        result["owner_reference"] = owner_reference(reader)
    return result


def decode_chocolate_cake(reader: BlobReader, entity_type: int) -> dict:
    first = reader.f32()
    second = reader.f32()
    alive = reader.u8()
    result = {
        "property_38e4": first,
        "property_38e0": second,
        "is_alive": bool(alive),
        "is_alive_raw": alive,
    }
    if entity_type == 3:
        result["owner_reference"] = owner_reference(reader)
        result["property_7d0"] = reader.u32()
    return result


def decode_init_blob(family: str, entity_type: int, data: bytes) -> dict:
    if family not in FAMILIES:
        raise ValueError(f"família de init blob desconhecida: {family}")
    if entity_type < 0 or entity_type > 0xFF:
        raise ValueError("entity type deve caber em u8")

    reader = BlobReader(data)
    decoders = {
        "base": decode_base,
        "gold_golem": decode_gold_golem,
        "chocolate_cake": decode_chocolate_cake,
    }
    result = decoders[family](reader, entity_type)
    reader.finish()
    return {"family": family, "entity_type": entity_type, **result}


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Decodifica init blobs polimórficos de NPC do Rakion v258")
    parser.add_argument("family", choices=sorted(FAMILIES))
    parser.add_argument("entity_type", type=lambda value: int(value, 0))
    parser.add_argument("hex", help="init blob hexadecimal")
    args = parser.parse_args()
    data = bytes.fromhex("".join(args.hex.split()))
    print(json.dumps(
        decode_init_blob(args.family, args.entity_type, data), ensure_ascii=False))


if __name__ == "__main__":
    main()
