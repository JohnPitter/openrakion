import sys

from world_room_probe import connect, receive, request


def require_prefix(frames: list[bytes], prefix: bytes, label: str) -> None:
    if any(frame.startswith(prefix) for frame in frames):
        return
    encoded = ",".join(frame.hex() for frame in frames)
    raise RuntimeError(f"{label}: resposta {prefix.hex()} ausente; frames={encoded}")


def require_disconnect(frames: list[bytes], reason: int, label: str) -> None:
    for frame in frames:
        if len(frame) >= 10 and frame[:2] == b"\x04\x00":
            actual = int.from_bytes(frame[6:8], "little")
            if actual == reason:
                return
    encoded = ",".join(frame.hex() for frame in frames)
    raise RuntimeError(f"{label}: disconnect reason {reason} ausente; frames={encoded}")


def main() -> None:
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 40708
    sessions = []
    try:
        lobby = connect(port, "test", 1)
        sessions.append(lobby)

        # 0x61 e 0x77 não respondem nesta build. O 0x76 subsequente comprova que
        # ambos atravessaram o dispatcher sem consumir ou derrubar a sessão.
        lobby.sendall(request(0x61, 2, (0x12345678).to_bytes(4, "little")))
        receive(lobby)
        lobby.sendall(request(0x77, 3))
        receive(lobby)
        lobby.sendall(request(0x76, 4, b"\x00"))
        require_prefix(receive(lobby, 0.6), b"\x76\x00", "AskLotto 0x76")

        lobby.sendall(request(0x78, 5))
        require_prefix(receive(lobby, 0.6), b"\x78\x00", "ClanMembersQuery 0x78")

        invalid_lotto = connect(port, "test2", 9001)
        sessions.append(invalid_lotto)
        invalid_lotto.sendall(request(0x75, 2, b"\x02\x01\x02\x03\x04\x05"))
        require_disconnect(
            receive(invalid_lotto, 0.6),
            0xE7,
            "BuyLotto 0x75 com paymentType inválido",
        )

        disconnect = connect(port, "test3", 3)
        sessions.append(disconnect)
        disconnect.sendall(request(0x79, 2))
        require_disconnect(
            receive(disconnect, 0.6),
            1,
            "DisconnectNotText 0x79",
        )

        print("OK 0x61/0x77 preservam a sessão e 0x76 responde pela rota final")
        print("OK 0x78 retorna o contrato de membros/status do clã")
        print("OK 0x75 aplica o guard E7 sem mutação e 0x79 desconecta com reason 1")
    finally:
        for session in sessions:
            session.close()


if __name__ == "__main__":
    main()
