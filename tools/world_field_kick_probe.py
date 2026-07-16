import sys

from world_room_probe import connect, describe, has_prefix, receive, request


def require_prefix(frames: list[bytes], prefix: bytes, message: str) -> None:
    if not has_prefix(frames, prefix):
        raise RuntimeError(message)


def enter_match(master, target, voter) -> None:
    create = (b"REKick\x00\x00probe\x00" + bytes((1, 3, 1))
              + (432).to_bytes(2, "little") + bytes((20, 1, 99, 0)))
    master.sendall(request(0x3B, 2, create))
    created = receive(master)
    create_ack = next(frame for frame in created if frame[:3] == b"\x3b\x00\x00")
    field_id = int.from_bytes(create_ack[3:5], "little")

    target.sendall(request(0x38, 2, field_id.to_bytes(2, "little") + b"\x00"))
    receive(master); receive(target)
    voter.sendall(request(0x38, 2, field_id.to_bytes(2, "little") + b"\x00"))
    receive(master); receive(target); receive(voter)

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


def main() -> None:
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 40708
    master = connect(port, "test", 1)
    target = connect(port, "test2", 9001)
    voter = connect(port, "test3", 3)
    try:
        enter_match(master, target, voter)
        master.sendall(request(0x5D, 5, b"\x01AFK\x00"))
        opened_master = receive(master)
        opened_target = receive(target)
        opened_voter = receive(voter)
        require_prefix(opened_voter, bytes.fromhex("5d000141464b00"),
                       "votação não abriu para o terceiro jogador")
        if has_prefix(opened_master, b"\x5d\x00") or has_prefix(opened_target, b"\x5d\x00"):
            raise RuntimeError("abertura chegou ao host ou ao alvo")

        master.sendall(request(0x40, 6, b"\x01"))
        master_frames = receive(master)
        target_frames = receive(target)
        voter_frames = receive(voter)
        describe("kick-master", master_frames)
        describe("kick-target", target_frames)
        describe("kick-voter", voter_frames)

        cancelled = bytes.fromhex("5f0000010000000001")
        departed = bytes.fromhex("3a0001")
        for label, frames in (("host", master_frames), ("voter", voter_frames)):
            require_prefix(frames, cancelled, f"{label} não recebeu cancelamento 0x5F")
            require_prefix(frames, departed, f"{label} não recebeu saída 0x3A")
        if has_prefix(target_frames, cancelled) or has_prefix(target_frames, departed):
            raise RuntimeError("vítima recebeu broadcasts internos após a remoção")
        for prefix in (b"\x1f\x00", b"\x1e\x00", b"\x36\x00"):
            require_prefix(target_frames, prefix, "vítima não voltou à lista de salas")

        target.sendall(request(0x36, 5, bytes((10,)) + bytes(9)))
        alive = receive(target)
        describe("target-alive", alive)
        require_prefix(alive, b"\x36\x00", "conexão da vítima não permaneceu ativa")
    finally:
        master.close()
        target.close()
        voter.close()


if __name__ == "__main__":
    main()
