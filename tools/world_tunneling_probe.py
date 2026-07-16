import sys

from world_room_probe import receive, request
from world_udp_probe import login_with_udp_credentials, open_udp_pair


def has_frame(frames, prefix: bytes) -> bool:
    return any(frame.startswith(prefix) for frame in frames)


def joined_tunneling_flag(frame: bytes) -> int:
    if len(frame) < 9 or not frame.startswith(b"\x38\x00\x00"):
        raise RuntimeError(f"frame PlayerJoined inválido: {frame.hex()}")
    cursor = 8
    for _ in range(2):
        end = frame.find(b"\x00", cursor)
        if end < 0:
            raise RuntimeError(f"string truncada no PlayerJoined: {frame.hex()}")
        cursor = end + 1
    if cursor >= len(frame):
        raise RuntimeError(f"flag de tunneling ausente: {frame.hex()}")
    return frame[cursor]


def main() -> None:
    world_port = int(sys.argv[1]) if len(sys.argv) > 1 else 40708
    clients = []
    udp_pair = []
    try:
        clients.append(login_with_udp_credentials(world_port, "test", 1))
        clients.append(login_with_udp_credentials(world_port, "test2", 9001))
        host, host_slot, host_key, host_character = clients[0]
        peer, _, _, peer_character = clients[1]
        udp_pair = open_udp_pair(world_port, host_slot, host_key)

        for tcp in (host, peer):
            tcp.sendall(request(0x0E, 1))
            receive(tcp)
        host.sendall(request(0x14, 2, host_character.to_bytes(4, "little") + bytes(4)))
        peer.sendall(request(0x14, 2, peer_character.to_bytes(4, "little") + bytes(4)))
        receive(host)
        receive(peer)

        room = (b"Tunnel\x00\x00probe\x00" + bytes((1, 1, 1))
                + (432).to_bytes(2, "little") + bytes((1, 1, 99, 0)))
        host.sendall(request(0x3B, 3, room))
        created = receive(host)
        ack = next((frame for frame in created
                    if len(frame) >= 5 and frame[:3] == b"\x3b\x00\x00"), None)
        if ack is None:
            raise RuntimeError("criação da sala de tunneling não retornou 0x3B")
        field_id = int.from_bytes(ack[3:5], "little")

        peer.sendall(request(0x38, 3, field_id.to_bytes(2, "little") + b"\x00"))
        joined = receive(host)
        receive(peer)
        player_joined = next((frame for frame in joined
                              if frame.startswith(b"\x38\x00\x00\x01")), None)
        if player_joined is None or joined_tunneling_flag(player_joined) != 1:
            frames = ",".join(frame.hex() for frame in joined)
            raise RuntimeError(f"roster não publicou peer sem UDP como tunneling; frames={frames}")

        peer.sendall(request(0x3E, 4))
        receive(host)
        receive(peer)
        peer.sendall(request(0x3D, 5, b"\x01"))
        receive(host)
        receive(peer)
        host.sendall(request(0x43, 4))
        receive(host)
        receive(peer)

        host.sendall(request(0x45, 5))
        receive(host)
        receive(peer)
        peer.sendall(request(0x45, 6))
        host_enter = receive(host)
        peer_enter = receive(peer)
        if not has_frame(host_enter, b"\x54\x00") or not has_frame(peer_enter, b"\x54\x00"):
            raise RuntimeError("agregado 0x54 não foi publicado no FieldGameEnter do peer")

        host.sendall(request(0x4B, 6, bytes(72)))
        receive(host)
        receive(peer)
        peer.sendall(request(0x4B, 7, bytes(72)))
        receive(host)
        receive(peer)

        host.sendall(request(0x56, 7, (3).to_bytes(2, "little") + b"abc"))
        if not has_frame(receive(peer), b"\x57\x00\x03\x00abc"):
            raise RuntimeError("TunnelAll direto→tunneling não chegou ao peer")
        if has_frame(receive(host), b"\x57\x00"):
            raise RuntimeError("TunnelAll retornou ao sender")

        peer.sendall(request(0x56, 8, (3).to_bytes(2, "little") + b"def"))
        if not has_frame(receive(host), b"\x57\x00\x03\x00def"):
            raise RuntimeError("TunnelAll tunneling→direto não chegou ao peer")

        host.sendall(request(0x57, 8, b"\x0a" + (3).to_bytes(2, "little") + b"xyz"))
        if not has_frame(receive(peer), b"\x57\x00\x03\x00xyz"):
            raise RuntimeError("TunnelOne direto→tunneling não chegou ao alvo")

        print("OK roster marca peer sem UDP com flag de tunneling")
        print("OK 0x54 agrega presença e TunnelAll/One relayam somente quando uma ponta usa tunnel")
    finally:
        for udp in udp_pair:
            udp.close()
        for client in clients:
            client[0].close()


if __name__ == "__main__":
    main()
