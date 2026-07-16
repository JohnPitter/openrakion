import socket
import sys

from world_room_probe import login_frame, receive, request


def login_with_udp_credentials(port: int, account: str, character_id: int):
    tcp = socket.create_connection(("127.0.0.1", port), timeout=5)
    tcp.settimeout(0.3)
    tcp.sendall(login_frame(account))
    frames = receive(tcp, 0.4)
    login = next((frame for frame in frames if frame.startswith(b"\x0c\x00")), None)
    if login is None or len(login) < 13:
        encoded = ",".join(frame.hex() for frame in frames)
        tcp.close()
        raise RuntimeError(f"{account}: resposta 0x0c ausente; frames={encoded}")
    slot = int.from_bytes(login[7:9], "little")
    key = int.from_bytes(login[9:13], "little")
    if key == 0:
        raise RuntimeError(f"{account}: chave UDP zerada")
    return tcp, slot, key, character_id


def handshake_packet(packet_type: int, slot: int, key: int, local_port: int, echo_data: int) -> bytes:
    return (packet_type.to_bytes(2, "little") + bytes(5) + slot.to_bytes(2, "little")
            + key.to_bytes(4, "little") + socket.inet_aton("127.0.0.1")
            + local_port.to_bytes(2, "big") + echo_data.to_bytes(4, "little"))


def open_udp_pair(world_port: int, slot: int, key: int):
    sockets = []
    for packet_type, remote_port, result in ((0x0201, world_port, 0), (0x0202, world_port + 1, 1)):
        udp = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        udp.bind(("127.0.0.1", 0))
        udp.settimeout(0.5)
        echo_data = 0x10203040 + slot + result
        packet = handshake_packet(packet_type, slot, key, udp.getsockname()[1], echo_data)
        udp.sendto(packet, ("127.0.0.1", remote_port))
        echo = udp.recv(64)
        expected = (b"\x01\x02" + echo_data.to_bytes(4, "little") + bytes((result, result))
                    + echo_data.to_bytes(4, "little"))
        if echo != expected:
            raise RuntimeError(f"slot {slot}: echo UDP{result + 1} divergente: {echo.hex()}")
        sockets.append(udp)
    return sockets


def expect_no_packet(udp: socket.socket, label: str) -> None:
    try:
        packet = udp.recv(2048)
    except socket.timeout:
        return
    raise RuntimeError(f"{label} recebeu pacote indevido: {packet.hex()}")


def endpoint_bytes(udp: socket.socket) -> bytes:
    host, port = udp.getsockname()
    return socket.inet_aton(host) + port.to_bytes(2, "big")


def reliable_entity(packet_type: int, sequence: int, source: int, body: bytes) -> bytes:
    return (packet_type.to_bytes(2, "little") + sequence.to_bytes(4, "little")
            + bytes((source,)) + body)


def main() -> None:
    world_port = int(sys.argv[1]) if len(sys.argv) > 1 else 40708
    expect_relay = "--expect-no-relay" not in sys.argv[2:]
    clients = []
    udp_pairs = []
    try:
        for account, character_id in (("test", 1), ("test2", 9001), ("test3", 3)):
            clients.append(login_with_udp_credentials(world_port, account, character_id))
        tcp1, slot1, key1, _ = clients[0]
        tcp2, slot2, key2, _ = clients[1]
        tcp3, slot3, key3, _ = clients[2]
        if len({slot1, slot2, slot3}) != 3 or len({key1, key2, key3}) != 3:
            raise RuntimeError(
                f"slots ou chaves UDP não são distintos: "
                f"slots={slot1},{slot2},{slot3} keys={key1:08x},{key2:08x},{key3:08x}")

        udp_pairs = [open_udp_pair(world_port, slot, key)
                     for slot, key in ((slot1, key1), (slot2, key2), (slot3, key3))]

        # O cliente registra os dois endpoints UDP e consulta 0x0E antes de
        # selecionar o personagem com 0x14. O World original rejeita 0x0E se
        # user+0x14A4 já estiver preenchido.
        for index, (tcp, _, _, _) in enumerate(clients):
            tcp.sendall(request(0x0E, 1))
            endpoint_frames = receive(tcp)
            expected = b"\x0e\x00\x00" + endpoint_bytes(udp_pairs[index][0]) + endpoint_bytes(udp_pairs[index][1])
            if not any(frame.startswith(expected) for frame in endpoint_frames):
                raise RuntimeError(f"sessão {index}: 0x0E não publicou endpoint observado + P2P anunciado")

        for tcp, _, _, character_id in clients:
            tcp.sendall(request(0x14, 2, character_id.to_bytes(4, "little") + bytes(4)))
            receive(tcp)

        invalid = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        invalid.bind(("127.0.0.1", 0))
        invalid.settimeout(0.3)
        invalid.sendto(handshake_packet(0x0202, slot1, key2, invalid.getsockname()[1], 7),
                       ("127.0.0.1", world_port + 1))
        expect_no_packet(invalid, "handshake com chave de outra sessão")
        invalid.close()

        room_a = b"UdpA\x00\x00probe\x00" + bytes((1, 1, 1)) + (432).to_bytes(2, "little") + bytes((1, 1, 99, 0))
        tcp1.sendall(request(0x3B, 3, room_a))
        created = receive(tcp1)
        create_ack = next((frame for frame in created
                           if len(frame) >= 5 and frame[:3] == b"\x3b\x00\x00"), None)
        if create_ack is None:
            created_hex = ",".join(frame.hex() for frame in created)
            raise RuntimeError(
                f"ack 0x3B não publicou o ID da sala UDP recém-criada; frames={created_hex}")
        field_id = int.from_bytes(create_ack[3:5], "little")
        tcp2.sendall(request(0x38, 3, field_id.to_bytes(2, "little") + b"\x00"))
        joined_frames = receive(tcp1) + receive(tcp2)
        peer_wire = (udp_pairs[1][0].getsockname()[1].to_bytes(2, "big")
                     + socket.inet_aton("127.0.0.1")
                     + udp_pairs[1][1].getsockname()[1].to_bytes(2, "big"))
        if not any(frame.startswith(b"\x38\x00") and peer_wire in frame for frame in joined_frames):
            frames = ",".join(frame.hex() for frame in joined_frames)
            raise RuntimeError(
                f"roster 0x38 não publicou {peer_wire.hex()} em network byte order; frames={frames}")

        room_b = b"UdpB\x00\x00probe\x00" + bytes((1, 1, 1)) + (432).to_bytes(2, "little") + bytes((1, 1, 99, 0))
        tcp3.sendall(request(0x3B, 3, room_b))
        receive(tcp3)

        old_udp2 = udp_pairs[0][1]
        migrated_udp2 = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
        migrated_udp2.bind(("127.0.0.1", 0))
        migrated_udp2.settimeout(0.5)
        migration_echo = 0x55667788
        migrated_udp2.sendto(
            handshake_packet(0x0202, slot1, key1, migrated_udp2.getsockname()[1], migration_echo),
            ("127.0.0.1", world_port + 1))
        expected_migration = (b"\x01\x02" + migration_echo.to_bytes(4, "little")
                              + b"\x01\x01" + migration_echo.to_bytes(4, "little"))
        if migrated_udp2.recv(64) != expected_migration:
            raise RuntimeError("migração autenticada do endpoint UDP2 não recebeu o echo original")

        migration_action = bytes.fromhex("0f03280000000000080001000003")
        old_udp2.sendto(migration_action, ("127.0.0.1", world_port + 1))
        expect_no_packet(udp_pairs[1][1], "peer após uso do endpoint antigo")
        migrated_udp2.sendto(migration_action, ("127.0.0.1", world_port + 1))
        if expect_relay:
            if udp_pairs[1][1].recv(2048) != migration_action:
                raise RuntimeError("novo endpoint autenticado não assumiu a rota UDP2")
        else:
            expect_no_packet(udp_pairs[1][1], "peer com relay compatível desativado")
            print("OK modo fiel: handshake/roster ativos e Port2 não retransmite gameplay")
            return
        old_udp2.close()
        udp_pairs[0][1] = migrated_udp2

        placement = bytes(24)
        actions = (
            ("legacy-0401", bytes.fromhex("0104aabbccddeeff")),
            ("move-030a", bytes.fromhex("0a032700000000650020005e01000092090000a5000000000000")),
            ("keys-030f", bytes.fromhex("0f03280000000000080001000003")),
            ("attack-0311", bytes.fromhex("11032900000000000100")),
            ("reliable-0304", bytes.fromhex("040308000000ff00d81fc000")),
            ("ack-0305", bytes.fromhex("050305000000000aae11dd0000")),
            ("address-0319", bytes.fromhex("19030e0000000000")),
            ("sync-830c", bytes.fromhex("0c839a00000000000100002a0091010400000001000000")),
            ("sync-ack-4000", bytes.fromhex("0040a3000000009a000000")),
            ("sync-8315", bytes.fromhex("1583d70000000003")),
            ("sync-8313", bytes.fromhex("138375040000000000")),
            ("npc-create-8307", reliable_entity(
                0x8307, 0x101, 0, bytes((0, 1)) + (7).to_bytes(2, "little")
                + placement + b"\xaa\x55")),
            ("master-golem-create-8308", reliable_entity(
                0x8308, 0x102, 0, bytes((0, 1)) + (8).to_bytes(2, "little")
                + placement)),
            ("map-npc-create-8309", reliable_entity(
                0x8309, 0x103, 0, bytes((0, 2)) + (9).to_bytes(2, "little")
                + placement)),
            ("entity-state-830b", reliable_entity(
                0x830B, 0x104, 0, (15).to_bytes(2, "little") + bytes((2, 0, 1))
                + bytes(8))),
            ("map-npc-action-8310", reliable_entity(
                0x8310, 0x105, 0, bytes((0, 3, 1)))),
            ("map-items-8312", reliable_entity(
                0x8312, 0x106, 0, bytes((2, 1, 1, 2, 0)))),
        )
        for label, action in actions:
            udp_pairs[0][1].sendto(action, ("127.0.0.1", world_port + 1))
            try:
                relayed = udp_pairs[1][1].recv(2048)
            except socket.timeout as exc:
                raise RuntimeError(f"{label} não foi relayado") from exc
            if relayed != action:
                raise RuntimeError(f"{label}: peer recebeu bytes divergentes: {relayed.hex()}")
            expect_no_packet(udp_pairs[0][1], "sender")
            expect_no_packet(udp_pairs[2][1], "sessão de outro field")

        malformed_entity = reliable_entity(0x8312, 0x107, 0, bytes((2, 1, 1)))
        udp_pairs[0][1].sendto(malformed_entity, ("127.0.0.1", world_port + 1))
        expect_no_packet(udp_pairs[1][1], "peer após snapshot NPC truncado")

        forged_source = bytes.fromhex("1583d80000000103")
        udp_pairs[0][1].sendto(forged_source, ("127.0.0.1", world_port + 1))
        expect_no_packet(udp_pairs[1][1], "peer após source seat forjado")

        tcp2.sendall(request(0x3D, 4, b"\x01"))
        receive(tcp1)
        receive(tcp2)
        tcp1.sendall(request(0x43, 4))
        receive(tcp1)
        receive(tcp2)
        tcp1.sendall(request(0x4B, 5, bytes(72)))
        receive(tcp1)
        tcp2.sendall(request(0x4B, 5, bytes(72)))
        receive(tcp1)
        receive(tcp2)

        tcp1.sendall(request(0x56, 6, (3).to_bytes(2, "little") + b"abc"))
        tunnel_all_sender = receive(tcp1)
        tunnel_all_peer = receive(tcp2)
        tunnel_all_other_field = receive(tcp3)
        if any(frame.startswith(b"\x57\x00") for frame in tunnel_all_sender):
            raise RuntimeError("TunnelAll voltou ao sender")
        if any(frame.startswith(b"\x57\x00") for frame in tunnel_all_peer):
            raise RuntimeError("TunnelAll duplicou o canal direto em peer sem tunneling")
        if any(frame.startswith(b"\x57\x00") for frame in tunnel_all_other_field):
            raise RuntimeError("TunnelAll cruzou fields")

        tcp1.sendall(request(0x57, 7, b"\x01" + (3).to_bytes(2, "little") + b"xyz"))
        tunnel_one = receive(tcp2)
        if any(frame.startswith(b"\x57\x00") for frame in tunnel_one):
            raise RuntimeError("TunnelOne duplicou o canal direto em peer sem tunneling")
        if any(frame.startswith(b"\x57\x00") for frame in receive(tcp3)):
            raise RuntimeError("TunnelOne chegou à sessão de outro field")

        tick = 0x12345678
        tcp2.sendall(request(0x59, 6, b"\x00" + tick.to_bytes(4, "little")))
        ping_request = receive(tcp1)
        expected_request = b"\x59\x00" + slot2.to_bytes(2, "little") + tick.to_bytes(4, "little")
        if not any(frame.startswith(expected_request) for frame in ping_request):
            raise RuntimeError("PingRequest não chegou ao host com o slot global do sender")

        tcp1.sendall(request(0x5A, 8, slot2.to_bytes(2, "little") + tick.to_bytes(4, "little")))
        ping_response = receive(tcp2)
        expected_response = b"\x5a\x00\x00\x00" + tick.to_bytes(4, "little")
        if not any(frame.startswith(expected_response) for frame in ping_response):
            raise RuntimeError("PingResponse não chegou ao alvo com o seat local do responder")

        tcp1.sendall(request(0x62, 9, b"\x01"))
        slot_udp = receive(tcp2)
        if not any(frame.startswith(b"\x62\x00\x00") for frame in slot_udp):
            raise RuntimeError("FieldSlotUdp 0x62 não chegou ao seat alvo com o seat do sender")

        print(f"OK slots={slot1},{slot2},{slot3} keys={key1:08x},{key2:08x},{key3:08x}")
        print("OK 0x0E/0x38 publicam endpoint observado + P2P anunciado em network byte order")
        print("OK handshake e source seat inválidos rejeitados; relay de ação/reliable/sync somente ao peer do mesmo field")
        print("OK novo handshake autenticado migra UDP2 e invalida imediatamente a rota antiga")
        print("OK TunnelAll/One não duplicam pares diretos; Ping e FieldSlotUdp seguem o original")
    finally:
        for pair in udp_pairs:
            for udp in pair:
                udp.close()
        for tcp, _, _, _ in clients:
            tcp.close()


if __name__ == "__main__":
    main()
