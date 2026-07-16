import sys
import time

from world_room_probe import connect, describe, receive, request


def has(frames: list[bytes], prefix: bytes) -> bool:
    return any(frame.startswith(prefix) for frame in frames)


def main() -> None:
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 40708
    first = connect(port, "test", 1)
    second = connect(port, "test2", 9001)
    try:
        create_payload = (b"REAdmin\x00\x00probe\x00" + bytes((1, 1, 1))
                          + (432).to_bytes(2, "little") + bytes((1, 1, 99, 0)))
        first.sendall(request(0x3B, 2, create_payload))
        describe("create", receive(first))

        list_payload = (bytes((10,)) + (0).to_bytes(2, "little")
                        + bytes((1, 0, 1, 0, 0, 0, 0)))
        second.sendall(request(0x36, 2, list_payload))
        listed = receive(second)
        describe("list", listed)
        second.sendall(request(0x39, 3))
        joined = receive(second)
        describe("quick-join", joined)
        if not has(joined, b"\x37\x00") or not has(joined, b"\x38\x00\x00"):
            raise RuntimeError("quick join não entregou snapshot e entrada incremental")
        if has(joined, b"\x26\x00"):
            raise RuntimeError("quick join recebeu o ack 0x26 exclusivo do caminho mode=0")

        second.sendall(request(0x3E, 4))
        team_frames = receive(first) + receive(second)
        describe("change-team", team_frames)
        if sum(frame.startswith(b"\x3e\x00\x00\x01\x0a") for frame in team_frames) != 2:
            raise RuntimeError("troca de time não chegou aos dois membros")

        first.sendall(request(0x42, 3, bytes((2, 0))))
        slot_frames = receive(first) + receive(second)
        describe("lock-slot", slot_frames)
        if sum(frame.startswith(b"\x42\x00\x02\x00") for frame in slot_frames) != 2:
            raise RuntimeError("lock de slot não foi transmitido")

        rule = b"REChanged\x00\x00admin\x00" + bytes((2, 1)) + (432).to_bytes(
            2, "little") + bytes((1, 99))
        first.sendall(request(0x41, 4, rule))
        rule_frames = receive(first) + receive(second)
        describe("change-rule", rule_frames)
        if sum(frame.startswith(b"\x41\x00REChanged\x00") for frame in rule_frames) != 2:
            raise RuntimeError("mudança de regra não foi transmitida")

        first.sendall(request(0x3C, 5))
        master_frames = receive(first) + receive(second)
        describe("change-master", master_frames)
        if sum(frame.startswith(b"\x3c\x00\x0a") for frame in master_frames) != 2:
            raise RuntimeError("troca de host não foi transmitida")

        first.sendall(request(0x40, 6, bytes((10,))))
        receive(first)
        first.sendall(request(0x3D, 7, b"\x01"))
        proof_member = receive(first) + receive(second)
        describe("non-host-kick-proof", proof_member)
        if not has(proof_member, b"\x3d\x00\x00\x01"):
            raise RuntimeError("não-host removeu o host ou perdeu a sala")

        second.sendall(request(0x40, 5, bytes((0,))))
        kicked = receive(first)
        kick_host = receive(second)
        describe("host-kick", kicked + kick_host)
        if not has(kicked, b"\x36\x00") or not has(kick_host, b"\x3a\x00\x00"):
            raise RuntimeError("vítima não recebeu kick e retorno à lista")

        second.sendall(request(0x3F, 6))
        closed = receive(second)
        describe("close-room", closed)
        if not has(closed, b"\x36\x00\x00"):
            raise RuntimeError("close não devolveu o host à lista vazia")
        time.sleep(0.1)
    finally:
        first.close()
        second.close()


if __name__ == "__main__":
    main()
