import socket
import sys
import time

from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes

KEY = bytes.fromhex("e13a7ef5372c104d4eceb30c5626a48e")


def encrypt(plain: bytes) -> bytes:
    plain += bytes((-len(plain)) % 12)
    raw = b"".join(
        (0xC47F).to_bytes(4, "little") + plain[offset:offset + 12]
        for offset in range(0, len(plain), 12)
    )
    body = Cipher(algorithms.AES(KEY), modes.ECB()).encryptor().update(raw)
    return (len(body) + 2).to_bytes(2, "little") + body


def receive(sock: socket.socket, delay: float = 0.25) -> list[bytes]:
    time.sleep(delay)
    data = b""
    while True:
        try:
            data += sock.recv(16384)
        except socket.timeout:
            break
    frames = []
    offset = 0
    while offset + 2 <= len(data):
        size = int.from_bytes(data[offset:offset + 2], "little")
        raw = Cipher(algorithms.AES(KEY), modes.ECB()).decryptor().update(
            data[offset + 2:offset + size]
        )
        frames.append(b"".join(raw[pos + 4:pos + 16] for pos in range(0, len(raw), 16)))
        offset += size
    return frames


def request(opcode: int, sequence: int, payload: bytes = b"") -> bytes:
    return encrypt(opcode.to_bytes(2, "little") + sequence.to_bytes(2, "little") + payload)


def connect_and_select(port: int, account: str, character_id: int) -> tuple[socket.socket, list[bytes]]:
    sock = socket.create_connection(("127.0.0.1", port), timeout=5)
    sock.settimeout(0.3)
    login = (b"\x0c\x00\x00\x00\x00D\x00" + account.encode("ascii") + b"\x00"
             + account.encode("ascii") + b"\x00\x01\x00\x00\xdfish")
    sock.sendall(encrypt(login))
    receive(sock, 0.4)
    sock.sendall(request(0x14, 1, character_id.to_bytes(4, "little") + bytes(4)))
    return sock, receive(sock)


def frame(frames: list[bytes], opcode: int) -> bytes:
    return next((item for item in frames if item[:2] == opcode.to_bytes(2, "little")), b"")


def main() -> None:
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 40708
    first, first_entry = connect_and_select(port, "test", 1)
    second = None
    extras: list[socket.socket] = []
    try:
        initial_snapshot = frame(first_entry, 0x1E)
        if initial_snapshot[3] != 1:
            raise RuntimeError("snapshot inicial não contém exatamente um membro")
        if initial_snapshot[4] != 100 or not initial_snapshot[5:].startswith(b"channel01\x00"):
            raise RuntimeError("snapshot inicial não preserva owner sentinel 100 e channel01")

        second, second_entry = connect_and_select(port, "test", 1)
        first_join = receive(first)
        if not frame(first_join, 0x1F):
            raise RuntimeError("membro existente não recebeu o 0x1F incremental")
        if frame(second_entry, 0x1E)[3] != 2:
            raise RuntimeError("novo membro não recebeu snapshot 0x1E com dois membros")

        second.sendall(request(0x1E, 2))
        if frame(receive(second), 0x1E)[3] != 2:
            raise RuntimeError("refresh 0x1E não devolveu os dois membros")

        second.sendall(request(0x22, 3, b"hello\x00"))
        if not frame(receive(first), 0x22) or not frame(receive(second), 0x22):
            raise RuntimeError("chat 0x22 não foi entregue aos dois membros")

        last_entry = second_entry
        for expected_count in range(3, 10):
            extra, last_entry = connect_and_select(port, "test", 1)
            extras.append(extra)
            if frame(last_entry, 0x1E)[3] != expected_count:
                raise RuntimeError(f"snapshot de entrada não contém {expected_count} membros")

        extras[-1].sendall(request(0x1E, 2))
        if frame(receive(extras[-1]), 0x1E)[3] != 8:
            raise RuntimeError("refresh 0x1E não limitou a amostra a oito membros")

        first.sendall(request(0x20, 4))
        owner_exit = receive(second)
        if not frame(owner_exit, 0x20):
            raise RuntimeError("saída 0x20 não foi publicada ao membro restante")
        if frame(owner_exit, 0x28):
            raise RuntimeError("canal padrão ownerless publicou transferência 0x28 indevida")

        print("channel probe OK: 9 joins, owner sentinel, snapshot, refresh limitado a 8, chat e exit")
    finally:
        first.close()
        if second is not None:
            second.close()
        for extra in extras:
            extra.close()


if __name__ == "__main__":
    main()
