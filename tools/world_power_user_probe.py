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


def find(frames: list[bytes], prefix: bytes) -> bytes:
    return next((frame for frame in frames if frame.startswith(prefix)), b"")


def main() -> None:
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 40708
    hold = int(sys.argv[2]) if len(sys.argv) > 2 else 0
    with socket.create_connection(("127.0.0.1", port), timeout=5) as sock:
        sock.settimeout(0.3)
        login = b"\x0c\x00\x00\x00\x00D\x00test\x00test\x00\x01\x00\x00\xdfish"
        sock.sendall(encrypt(login))
        receive(sock, 0.4)
        sock.sendall(request(0x14, 1, (1).to_bytes(4, "little") + bytes(4)))
        receive(sock)

        create = (b"REPowerUser\x00\x00probe\x00" + bytes((1, 0, 1))
                  + (432).to_bytes(2, "little") + bytes((1, 1, 99, 0)))
        sock.sendall(request(0x3B, 2, create))
        receive(sock)

        sock.sendall(request(0x34, 3, bytes((0, 0))))
        purchase_frames = receive(sock, 0.7)
        purchase = find(purchase_frames, b"\x34\x00")
        if len(purchase) < 18 or purchase[2] != 0:
            raise RuntimeError("compra de Power User não retornou o callback exato")
        gold = int.from_bytes(purchase[3:7], "little")
        cash = int.from_bytes(purchase[7:11], "little")
        power_time = int.from_bytes(purchase[11:15], "little")
        points = int.from_bytes(purchase[15:17], "little")
        if power_time == 0 or purchase[17] != 0:
            raise RuntimeError("callback Power User tem validade/presentes inválidos")

        sock.sendall(request(0x33, 4, bytes((0,))))
        allocation = find(receive(sock, 0.7), b"\x33\x00\x00")
        if len(allocation) < 10:
            raise RuntimeError("alocação transacional 0x33 não retornou sucesso")
        print(f"purchase={purchase[:18].hex()}")
        print(f"gold={gold} cash={cash} powertime={power_time} points={points}")
        print(f"allocation={allocation[:10].hex()}")
        if hold > 0:
            print(f"holding={hold}s", flush=True)
            time.sleep(hold)


if __name__ == "__main__":
    main()
