import socket
import sys

from world_gm_operation_probe import wait_disconnected
from world_room_probe import receive, request


HASH1 = "0123456789abcdef0123456789abcdef"


def connect_hash(port: int, select_character: bool = True) -> socket.socket:
    sock = socket.create_connection(("127.0.0.1", port), timeout=5)
    sock.settimeout(0.3)
    payload = bytes((0,)) + HASH1.encode("ascii") + b"\x00test\x00test\x00\x01\x00"
    sock.sendall(request(0x0C, 0, payload))
    receive(sock, 0.4)
    if select_character:
        sock.sendall(request(0x14, 1, (1).to_bytes(4, "little") + bytes(4)))
        receive(sock)
    return sock


def field_required(port: int) -> None:
    sock = connect_hash(port, select_character=False)
    try:
        sock.sendall(request(0x65, 1, HASH1.encode("ascii") + b"\x00"))
        if not wait_disconnected(sock):
            raise RuntimeError("0x65 fora do field não desconectou")
        print("field-required=disconnect-bb")
    finally:
        sock.close()


def hash_match_and_mismatch(port: int) -> None:
    sock = connect_hash(port)
    try:
        create = (b"REChCode\x00\x00probe\x00" + bytes((1, 0, 1))
                  + (432).to_bytes(2, "little") + bytes((1, 1, 99, 0)))
        sock.sendall(request(0x3B, 2, create))
        receive(sock)

        sock.sendall(request(0x65, 3, HASH1.encode("ascii") + b"\x00"))
        if wait_disconnected(sock):
            raise RuntimeError("0x65 recusou o MD5 esperado")
        print("matching-hash=no-response")

        wrong = "f" * 32
        sock.sendall(request(0x65, 4, wrong.encode("ascii") + b"\x00"))
        if not wait_disconnected(sock):
            raise RuntimeError("0x65 aceitou MD5 divergente")
        print("mismatch=disconnect-bc")
    finally:
        sock.close()


def main() -> None:
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 40708
    field_required(port)
    hash_match_and_mismatch(port)


if __name__ == "__main__":
    main()
