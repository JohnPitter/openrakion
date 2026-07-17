import sys

from world_gm_operation_probe import wait_disconnected
from world_room_probe import connect, request


def assert_rejected(port: int, opcode: int, payload: bytes = b"") -> None:
    sock = connect(port, "test", 1)
    try:
        sock.sendall(request(opcode, 2, payload))
        if not wait_disconnected(sock):
            raise RuntimeError(f"0x{opcode:02X} não caiu no default DISC C9")
        print(f"opcode-{opcode:02x}=disconnect-c9")
    finally:
        sock.close()


def main() -> None:
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 40708
    assert_rejected(port, 0x1D, b"\x00\x01")
    assert_rejected(port, 0x66)
    assert_rejected(port, 0x69)


if __name__ == "__main__":
    main()
