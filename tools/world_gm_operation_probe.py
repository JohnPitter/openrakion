import socket
import sys
import time

from world_room_probe import connect, receive, request


def wait_disconnected(sock: socket.socket) -> bool:
    deadline = time.time() + 2
    while time.time() < deadline:
        try:
            if sock.recv(4096) == b"":
                return True
        except socket.timeout:
            continue
        except (ConnectionResetError, ConnectionAbortedError, OSError):
            return True
        time.sleep(0.05)
    return False


def wrong_substatus(port: int) -> None:
    player = connect(port, "test", 1)
    player.sendall(request(0x64, 2))
    if not wait_disconnected(player):
        player.close()
        raise RuntimeError("0x64 não recusou substatus diferente de 0x34")
    player.close()
    print("wrong-substatus=disconnect")


def member_ip_gate(port: int, expect_allowed: bool) -> None:
    master = connect(port, "test", 1)
    member = connect(port, "test2", 9001)
    try:
        create = (b"REGmGate\x00\x00probe\x00" + bytes((1, 0, 1))
                  + (432).to_bytes(2, "little") + bytes((1, 1, 99, 0)))
        master.sendall(request(0x3B, 2, create))
        receive(master)

        member.sendall(request(0x36, 2, bytes((10,)) + bytes(9)))
        listed = receive(member)
        frame = next((item for item in listed if item[:3] == b"\x36\x00\x01"), b"")
        if len(frame) < 5:
            raise RuntimeError("sala do probe 0x64 não apareceu")
        field_id = int.from_bytes(frame[3:5], "little")
        member.sendall(request(0x38, 3, field_id.to_bytes(2, "little") + b"\x00"))
        receive(member)
        receive(master)

        member.sendall(request(0x64, 4))
        disconnected = wait_disconnected(member)
        if disconnected == expect_allowed:
            expected = "conexão aberta" if expect_allowed else "desconexão"
            raise RuntimeError(f"0x64 não manteve {expected} conforme GM.AllowedIPs")
        print("allowed-ip=no-response" if expect_allowed else "denied-ip=disconnect")
    finally:
        master.close()
        member.close()


def main() -> None:
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 40708
    expect_allowed = len(sys.argv) > 2 and sys.argv[2] == "allowed"
    wrong_substatus(port)
    member_ip_gate(port, expect_allowed)


if __name__ == "__main__":
    main()
