import sys

from world_room_probe import connect, describe, has_prefix, receive, request


def expect_both(label: str, first: list[bytes], second: list[bytes], prefix: bytes) -> None:
    describe(label + "-first", first)
    describe(label + "-second", second)
    if not has_prefix(first, prefix) or not has_prefix(second, prefix):
        raise RuntimeError(f"{label} não chegou aos dois membros: {prefix.hex()}")


def main() -> None:
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 40708
    first = connect(port, "test", 1)
    second = connect(port, "test2", 9001)
    try:
        create_payload = (b"REDeathmatch\x00\x00probe\x00" + bytes((1, 2, 1))
                          + (432).to_bytes(2, "little") + bytes((13, 1, 99, 0)))
        first.sendall(request(0x3B, 2, create_payload))
        created = receive(first)
        describe("create-deathmatch", created)
        create_ack = next((frame for frame in created
                           if len(frame) >= 5 and frame[:3] == b"\x3b\x00\x00"), None)
        if create_ack is None:
            raise RuntimeError("sala Deathmatch não foi criada")
        field_id = int.from_bytes(create_ack[3:5], "little")

        second.sendall(request(0x38, 2, field_id.to_bytes(2, "little") + b"\x00"))
        receive(first)
        joined = receive(second)
        joined_member = next((frame for frame in joined
                              if len(frame) >= 5 and frame[:3] == b"\x38\x00\x00"), None)
        if joined_member is None:
            raise RuntimeError("segundo membro não entrou na sala Deathmatch")
        killer_seat = joined_member[3]

        second.sendall(request(0x3D, 3, b"\x01"))
        receive(first)
        receive(second)
        first.sendall(request(0x43, 3, bytes(8)))
        receive(first)
        receive(second)

        first.sendall(request(0x4B, 4, bytes(72)))
        receive(first)
        second.sendall(request(0x4B, 4, bytes(72)))
        receive(first)
        receive(second)

        for sequence in range(5, 11):
            first.sendall(request(0x4F, sequence, bytes((8, killer_seat))))
            receive(first)
            receive(second)

        first.sendall(request(0x4F, 11, bytes((8, killer_seat))))
        first_result = receive(first)
        second_result = receive(second)
        expect_both("deathmatch-special-kill", first_result, second_result,
                    bytes((0x4F, 0, 0, 8, killer_seat, 0, 14)))
        expect_both("deathmatch-round-end", first_result, second_result,
                    bytes.fromhex("4a0001000000"))

        print(f"OK field={field_id} victim=0 killer={killer_seat} score=0/14")
        print("OK Deathmatch encerrou por frag individual sem incrementar Wins0/Wins1")
    finally:
        first.close()
        second.close()


if __name__ == "__main__":
    main()
