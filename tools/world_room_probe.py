import socket
import sys
import time
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes

KEY = bytes.fromhex("e13a7ef5372c104d4eceb30c5626a48e")


def encrypt(plain: bytes) -> bytes:
    plain += bytes((-len(plain)) % 12)
    raw = b"".join((0xC47F).to_bytes(4, "little") + plain[i:i + 12]
                   for i in range(0, len(plain), 12))
    body = Cipher(algorithms.AES(KEY), modes.ECB()).encryptor().update(raw)
    return (len(body) + 2).to_bytes(2, "little") + body


def decrypt_frames(data: bytes) -> list[bytes]:
    frames = []
    offset = 0
    while offset + 2 <= len(data):
        size = int.from_bytes(data[offset:offset + 2], "little")
        body = data[offset + 2:offset + size]
        raw = Cipher(algorithms.AES(KEY), modes.ECB()).decryptor().update(body)
        frames.append(b"".join(raw[i + 4:i + 16] for i in range(0, len(raw), 16)))
        offset += size
    return frames


def receive(sock: socket.socket, delay: float = 0.25) -> list[bytes]:
    time.sleep(delay)
    data = b""
    while True:
        try:
            chunk = sock.recv(16384)
            if not chunk:
                break
            data += chunk
        except socket.timeout:
            break
    return decrypt_frames(data)


def login_frame(account: str) -> bytes:
    plain = (b"\x0c\x00\x00\x00\x00D\x00" + account.encode("ascii") + b"\x00"
             + account.encode("ascii") + b"\x00\x01\x00\x00\xdfish")
    return encrypt(plain)


def request(opcode: int, sequence: int, payload: bytes = b"") -> bytes:
    return encrypt(opcode.to_bytes(2, "little") + sequence.to_bytes(2, "little") + payload)


def connect(port: int, account: str, character_id: int) -> socket.socket:
    sock = socket.create_connection(("127.0.0.1", port), timeout=5)
    sock.settimeout(0.3)
    sock.sendall(login_frame(account))
    receive(sock, 0.4)
    sock.sendall(request(0x14, 1, character_id.to_bytes(4, "little") + bytes(4)))
    receive(sock)
    return sock


def describe(label: str, frames: list[bytes]) -> None:
    print(label)
    for frame in frames:
        print(frame.hex())


def has_prefix(frames: list[bytes], prefix: bytes) -> bool:
    return any(frame.startswith(prefix) for frame in frames)


def parse_single_room_list(frame: bytes) -> dict[str, int | str]:
    if frame[:3] != b"\x36\x00\x01" or len(frame) < 31:
        raise RuntimeError("frame 0x36 não contém exatamente uma entrada completa")
    name_end = frame.find(b"\x00", 28)
    if name_end < 0 or name_end + 2 >= len(frame):
        raise RuntimeError("entrada 0x36 não contém nome/marker válidos")
    return {
        "field_id": int.from_bytes(frame[3:5], "little"),
        "has_password": frame[5],
        "in_game": frame[6],
        "map": frame[7],
        "mode": frame[8],
        "min_level": frame[9],
        "max_level": frame[10],
        "option": frame[11],
        "round": frame[12],
        "max_rounds": frame[13],
        "players": frame[14],
        "capacity": frame[15],
        "master_seat": int.from_bytes(frame[20:22], "little"),
        "name": frame[28:name_end].decode("ascii"),
        "marker": int.from_bytes(frame[name_end + 1:name_end + 3], "little"),
    }


def main() -> None:
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 40708
    first = connect(port, "test", 1)
    second = connect(port, "test2", 9001)
    try:
        create_payload = (b"REMulti\x00pw\x00probe\x00" + bytes((1, 1, 1))
                          + (432).to_bytes(2, "little") + bytes((1, 1, 99, 0)))
        first.sendall(request(0x3B, 2, create_payload))
        describe("create", receive(first))

        # cursor 0, pagina para frente, somente mode=1, sem bypass de nível/capacidade.
        list_payload = (bytes((10,)) + (0).to_bytes(2, "little")
                        + bytes((1, 0, 1, 0, 0, 0, 0)))
        second.sendall(request(0x36, 2, list_payload))
        listed = receive(second)
        describe("list", listed)
        if not any(frame[:3] == b"\x36\x00\x01" for frame in listed):
            raise RuntimeError("segunda sessão não recebeu uma sala na lista")

        list_frame = next(frame for frame in listed if frame[:3] == b"\x36\x00\x01")
        room = parse_single_room_list(list_frame)
        expected = {
            "has_password": 1, "in_game": 0, "map": 1, "mode": 1,
            "min_level": 1, "max_level": 99, "option": 0, "round": 0,
            "max_rounds": 1, "players": 1, "capacity": 12,
            "master_seat": 0, "name": "REMulti", "marker": 0,
        }
        for key, value in expected.items():
            if room[key] != value:
                raise RuntimeError(f"entrada 0x36 inválida em {key}: {room[key]!r} != {value!r}")
        field_id = int(room["field_id"])

        second.sendall(request(0x38, 3, field_id.to_bytes(2, "little") + b"wrong\x00"))
        wrong_password = receive(second)
        describe("wrong-password", wrong_password)
        if not any(frame[:3] == b"\x38\x00\x03" for frame in wrong_password):
            raise RuntimeError("senha incorreta não foi rejeitada")

        second.sendall(request(0x38, 4, field_id.to_bytes(2, "little") + b"pw\x00"))
        joined = receive(second)
        joined_existing = receive(first)
        describe("join", joined)
        describe("join-existing-member", joined_existing)
        if any(frame[:2] == b"\x26\x00" for frame in joined):
            raise RuntimeError("sala competitiva recebeu o ack 0x26 exclusivo de mode=0")
        if not any(frame[:2] == b"\x37\x00" for frame in joined):
            raise RuntimeError("segunda sessão não recebeu o roster completo 0x37")
        if not any(frame[:5] == b"\x38\x00\x00\x01\x03"
                   for frame in joined + joined_existing):
            raise RuntimeError("entrada do segundo jogador não foi publicada via 0x38")

        second.sendall(request(0x43, 5))
        denied = receive(second)
        describe("non-host-start", denied)
        if not any(frame[:3] == b"\x43\x00\x01" for frame in denied):
            raise RuntimeError("membro não-host conseguiu iniciar a partida")

        first.sendall(request(0x43, 3))
        not_ready = receive(first)
        describe("host-start-before-ready", not_ready)
        if not any(frame[:3] == b"\x43\x00\x02" for frame in not_ready):
            raise RuntimeError("host iniciou com membro ainda não pronto")

        second.sendall(request(0x3D, 6, b"\x01"))
        ready_second = receive(second)
        ready_first = receive(first)
        describe("ready-member", ready_second)
        if not any(frame[:2] == b"\x3d\x00" for frame in ready_second + ready_first):
            raise RuntimeError("estado de pronto não foi transmitido à sala")

        first.sendall(request(0x43, 4))
        started_first = receive(first)
        started_second = receive(second)
        describe("host-start", started_first + started_second)
        if sum(frame[:3] == b"\x43\x00\x00"
               for frame in started_first + started_second) != 2:
            raise RuntimeError("start não foi confirmado para os dois membros")

        # O cliente real pede o tempo entre 0x43 e o primeiro 0x4B. O World original já
        # promove user+0x1440 para 3 no start; este request protege essa transição.
        first.sendall(request(0x48, 5))
        pre_spawn_time = receive(first)
        describe("pre-spawn-time", pre_spawn_time)
        if not any(frame[:2] == b"\x48\x00" for frame in pre_spawn_time):
            raise RuntimeError("0x48 entre start e spawn não foi aceito em Status=3")

        first.sendall(request(0x4B, 6, bytes(72)))
        first_stage_enter = receive(first)
        describe("stage-enter-host", first_stage_enter)
        if not any(frame[:2] == b"\x48\x00" for frame in first_stage_enter):
            raise RuntimeError("host não recebeu o estado inicial do stage")

        second.sendall(request(0x4B, 7, bytes(72)))
        round_frames = receive(first) + receive(second)
        describe("stage-enter-member-round", round_frames)
        if sum(frame[:2] == b"\x48\x00" for frame in round_frames) != 2:
            raise RuntimeError("início do round não foi transmitido aos dois membros")

        first.sendall(request(0x4B, 7, (3).to_bytes(2, "little") + b"abc"))
        relay_sender = receive(first)
        relay_member = receive(second)
        describe("gameplay-relay-all", relay_sender + relay_member)
        if has_prefix(relay_sender, b"\x4b\x00"):
            raise RuntimeError("relay 0x4B voltou indevidamente ao remetente")
        if not has_prefix(relay_member, b"\x4b\x00\x00\x03\x00abc"):
            raise RuntimeError("relay 0x4B não chegou ao outro membro")

        second.sendall(request(0x4C, 8, b"\x00" + (3).to_bytes(2, "little") + b"xyz"))
        targeted_host = receive(first)
        targeted_sender = receive(second)
        describe("gameplay-relay-one", targeted_host + targeted_sender)
        if not has_prefix(targeted_host, b"\x4b\x00\x01\x03\x00xyz"):
            raise RuntimeError("relay direcionado 0x4C não chegou ao alvo como 0x4B")
        if has_prefix(targeted_sender, b"\x4b\x00"):
            raise RuntimeError("relay direcionado voltou indevidamente ao remetente")

        first.close()
        time.sleep(0.5)
        transferred = receive(second)
        describe("after-host-disconnect", transferred)
        if not any(frame[:3] == b"\x3a\x00\x00" for frame in transferred):
            raise RuntimeError("saída do host não foi transmitida no stage")
        if not any(frame[:3] == b"\x3c\x00\x01" for frame in transferred):
            raise RuntimeError("host não foi transferido no stage")
    finally:
        try:
            first.close()
        except OSError:
            pass
        second.close()


if __name__ == "__main__":
    main()
