import sys

from world_room_probe import connect, describe, has_prefix, receive, request


def expect_both(label: str, first_frames: list[bytes], second_frames: list[bytes], prefix: bytes) -> None:
    describe(label + "-first", first_frames)
    describe(label + "-second", second_frames)
    if not has_prefix(first_frames, prefix) or not has_prefix(second_frames, prefix):
        raise RuntimeError(f"{label} não chegou aos dois membros: {prefix.hex()}")


def main() -> None:
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 40708
    first = connect(port, "test", 1)
    second = connect(port, "test2", 9001)
    try:
        create_payload = (b"RECombat\x00probe\x00battle\x00" + bytes((1, 3, 1))
                          + (432).to_bytes(2, "little") + bytes((20, 1, 99, 0)))
        first.sendall(request(0x3B, 2, create_payload))
        created = receive(first)
        describe("create-team-death", created)
        create_ack = next((frame for frame in created if frame[:3] == b"\x3b\x00\x00"), None)
        if create_ack is None:
            raise RuntimeError("sala Team Death não foi criada")
        field_id = int.from_bytes(create_ack[3:5], "little")

        second.sendall(request(0x38, 2, field_id.to_bytes(2, "little") + b"probe\x00"))
        receive(first)
        joined = receive(second)
        describe("join", joined)
        joined_member = next((frame for frame in joined
                              if frame[:5] == b"\x38\x00\x00\x01\x03"), None)
        if joined_member is None:
            raise RuntimeError("entrada do segundo membro não foi publicada")
        target_session_slot = int.from_bytes(joined_member[5:7], "little")

        second.sendall(request(0x3E, 3))
        team_first = receive(first)
        team_second = receive(second)
        expect_both("change-team", team_first, team_second, bytes.fromhex("3e0000010a"))

        second.sendall(request(0x3D, 4, bytes((1,))))
        receive(first)
        receive(second)
        first.sendall(request(0x43, 3, bytes(8)))
        receive(first)
        receive(second)

        first.sendall(request(0x4B, 4, bytes(72)))
        receive(first)
        second.sendall(request(0x4B, 5, bytes(72)))
        receive(first)
        receive(second)

        first.sendall(request(0x72, 5, target_session_slot.to_bytes(2, "little")))
        invitation = receive(second)
        describe("field-invitation", invitation)
        expected_invitation_suffix = (
            field_id.to_bytes(2, "little")
            + bytes.fromhex("010301630001b001")
            + b"RECombat\x00battle\x00")
        if not any(frame[:2] == b"\x72\x00"
                   and expected_invitation_suffix in frame
                   for frame in invitation):
            raise RuntimeError("convite 0x72 não preservou remetente, fieldRef e blob da sala")

        second.sendall(request(0x47, 6, b"hello\x00"))
        expect_both("field-chat", receive(first), receive(second), bytes.fromhex("47000a68656c6c6f00"))

        first.sendall(request(0x4F, 6, bytes((8, 10))))
        expect_both("special-kill", receive(first), receive(second), bytes.fromhex("4f0000080a0002"))

        first.sendall(request(0x4F, 7, bytes((0, 10))))
        expect_both("respawn-kill", receive(first), receive(second), bytes.fromhex("4f0000000a0003"))

        first.sendall(request(0x46, 8, bytes((2,))))
        host_exit_first = receive(first)
        host_exit_second = receive(second)
        expect_both("host-exit", host_exit_first, host_exit_second, bytes.fromhex("460000"))
        if not has_prefix(host_exit_second, bytes.fromhex("4a0001000001")):
            raise RuntimeError("give-up do último jogador do time 0 não encerrou o round para o peer")

        second.sendall(request(0x46, 7, bytes((2,))))
        ended_first = receive(first)
        ended_second = receive(second)
        expect_both("member-exit", ended_first, ended_second, bytes.fromhex("46000a"))
        if not has_prefix(ended_first, bytes.fromhex("440000")):
            raise RuntimeError("fim PvP curto 0x44 reason=0 não chegou ao host")
        if not has_prefix(ended_second, bytes.fromhex("440000")):
            raise RuntimeError("fim PvP curto 0x44 reason=0 não chegou ao membro")
        expected_match_end = bytes.fromhex("440000000000000000000000")
        if expected_match_end not in ended_first or expected_match_end not in ended_second:
            raise RuntimeError("fim PvP não preservou o frame curto com padding AES zerado")
    finally:
        first.close()
        second.close()


if __name__ == "__main__":
    main()
