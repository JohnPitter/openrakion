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


def request(opcode: int, sequence: int, payload: bytes = b"") -> bytes:
    return encrypt(opcode.to_bytes(2, "little") + sequence.to_bytes(2, "little") + payload)


def receive(sock: socket.socket, delay: float = 0.3) -> list[bytes]:
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
        body = data[offset + 2:offset + size]
        raw = Cipher(algorithms.AES(KEY), modes.ECB()).decryptor().update(body)
        frames.append(b"".join(raw[i + 4:i + 16] for i in range(0, len(raw), 16)))
        offset += size
    return frames


def login(sock: socket.socket) -> None:
    plain = b"\x0c\x00\x00\x00\x00D\x00test\x00test\x00\x01\x00\x00\xdfish"
    sock.sendall(encrypt(plain))
    receive(sock, 0.4)
    sock.sendall(request(0x14, 1, (1).to_bytes(4, "little") + bytes(4)))
    receive(sock)


def frame_with_prefix(frames: list[bytes], prefix: bytes) -> bytes:
    return next((frame for frame in frames if frame.startswith(prefix)), b"")


def main() -> None:
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 40708
    with socket.create_connection(("127.0.0.1", port), timeout=5) as sock:
        sock.settimeout(0.3)
        login(sock)
        create = (b"REEnchant\x00\x00probe\x00" + bytes((1, 0, 1))
                  + (432).to_bytes(2, "little") + bytes((1, 1, 99, 0)))
        sock.sendall(request(0x3B, 2, create))
        receive(sock)

        sock.sendall(request(0x74, 3, bytes((0, 1, 1, 2))))
        preview = frame_with_prefix(receive(sock), b"\x00\x00\x28\x00")
        if not preview:
            raise RuntimeError("preview 0x28 não recebido")
        if int.from_bytes(preview[9:13], "little") != 9912001:
            raise RuntimeError("preview não publicou o serial real da arma")

        commit = bytes((0, 0, 1, 1, 2, 0, 0, 5))
        sock.sendall(request(0x28, 4, commit))
        result = frame_with_prefix(receive(sock), b"\x74\x00")
        if len(result) < 7 or result[2] not in (0, 1, 2, 3, 4, 6):
            raise RuntimeError("resultado autoritativo 0x74 inválido")

        sock.sendall(request(0x28, 5, commit))
        replay = frame_with_prefix(receive(sock), b"\x74\x00")
        if replay[:7] != result[:7]:
            raise RuntimeError("replay não devolveu o mesmo resultado")
        print(f"preview={preview[:40].hex()}")
        print(f"result={result[:7].hex()} server_result={result[2]}")
        print(f"replay={replay[:7].hex()}")


if __name__ == "__main__":
    main()
