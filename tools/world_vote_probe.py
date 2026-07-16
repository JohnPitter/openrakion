import sys

from world_room_probe import connect, describe, has_prefix, receive, request


def main() -> None:
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 40708
    master = connect(port, "test", 1)
    target = connect(port, "test2", 9001)
    voter = connect(port, "test3", 3)
    try:
        create = (b"REVote\x00\x00probe\x00" + bytes((1, 3, 1))
                  + (432).to_bytes(2, "little") + bytes((20, 1, 99, 0)))
        master.sendall(request(0x3B, 2, create))
        created = receive(master)
        create_ack = next(frame for frame in created if frame[:3] == b"\x3b\x00\x00")
        field_id = int.from_bytes(create_ack[3:5], "little")

        target.sendall(request(0x38, 2, field_id.to_bytes(2, "little") + b"\x00"))
        receive(master)
        receive(target)
        voter.sendall(request(0x38, 2, field_id.to_bytes(2, "little") + b"\x00"))
        receive(master)
        receive(target)
        receive(voter)

        target.sendall(request(0x3D, 3, b"\x01"))
        receive(master); receive(target); receive(voter)
        voter.sendall(request(0x3D, 3, b"\x01"))
        receive(master); receive(target); receive(voter)
        master.sendall(request(0x43, 3, bytes(8)))
        receive(master); receive(target); receive(voter)

        master.sendall(request(0x4B, 4, bytes(72)))
        receive(master)
        target.sendall(request(0x4B, 4, bytes(72)))
        receive(master); receive(target)
        voter.sendall(request(0x4B, 4, bytes(72)))
        receive(master); receive(target); receive(voter)

        master.sendall(request(0x5D, 5, b"\x01AFK\x00"))
        opened_master = receive(master)
        opened_target = receive(target)
        opened_voter = receive(voter)
        describe("vote-open-master", opened_master)
        describe("vote-open-target", opened_target)
        describe("vote-open-voter", opened_voter)
        expected_open = bytes.fromhex("5d000141464b00")
        if has_prefix(opened_master, expected_open) or has_prefix(opened_target, expected_open):
            raise RuntimeError("abertura do voto chegou ao opener ou ao alvo")
        if not has_prefix(opened_voter, expected_open):
            raise RuntimeError("abertura 0x5D não chegou ao terceiro jogador")

        target.sendall(request(0x5E, 5, b"\x01"))
        target_error = receive(target)
        describe("target-cannot-vote", target_error)
        if not has_prefix(target_error, bytes.fromhex("5f0005")):
            raise RuntimeError("alvo não recebeu status 5 ao tentar votar")

        voter.sendall(request(0x5E, 5, b"\x01"))
        final_master = receive(master)
        final_target = receive(target)
        final_voter = receive(voter)
        describe("vote-final-master", final_master)
        describe("vote-final-target", final_target)
        describe("vote-final-voter", final_voter)
        expected_final = bytes.fromhex("5f0000000302000001")
        for label, frames in (("master", final_master), ("target", final_target), ("voter", final_voter)):
            if not has_prefix(frames, expected_final):
                raise RuntimeError(f"resultado 0x5F não chegou a {label}")
    finally:
        master.close()
        target.close()
        voter.close()


if __name__ == "__main__":
    main()
