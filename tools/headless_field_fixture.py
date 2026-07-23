import argparse
import time

from world_room_probe import connect, receive, request


def create_field(sock) -> int:
    payload = (b"HeadlessHost\x00\x00fixture\x00" + bytes((1, 2, 1))
               + (432).to_bytes(2, "little") + bytes((13, 1, 99, 0)))
    sock.sendall(request(0x3B, 2, payload))
    frames = receive(sock)
    ack = next((frame for frame in frames
                if len(frame) >= 5 and frame[:3] == b"\x3b\x00\x00"), None)
    if ack is None:
        raise RuntimeError("sala de validação headless não foi criada")
    return int.from_bytes(ack[3:5], "little")


def wait_for_ready(sock, timeout: float) -> None:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        frames = receive(sock, 0.1)
        for frame in frames:
            print(f"ROOM_FRAME {frame.hex()}", flush=True)
        if any(frame[:2] == b"\x3d\x00" for frame in frames):
            return
    raise TimeoutError("peer headless não publicou ready")


def start_field(sock) -> None:
    sock.sendall(request(0x43, 3, bytes(8)))
    started = receive(sock)
    if not any(frame[:3] == b"\x43\x00\x00" for frame in started):
        raise RuntimeError("World não confirmou o start do field")
    print("START_OK", flush=True)


def observe_native_spawn(sock, timeout: float) -> None:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        frames = receive(sock, 0.1)
        for frame in frames:
            if frame[:2] == b"\x4b\x00":
                print(f"NATIVE_4B {frame.hex()}", flush=True)
                return
    raise TimeoutError("peer headless não publicou 0x4B nativo")


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=40708)
    parser.add_argument("--account", default="test")
    parser.add_argument("--character", type=int, default=1)
    parser.add_argument("--timeout", type=float, default=60)
    parser.add_argument("--expect-native-peer", action="store_true")
    args = parser.parse_args()

    sock = connect(args.port, args.account, args.character)
    try:
        field_id = create_field(sock)
        print(f"FIELD_ID {field_id}", flush=True)
        wait_for_ready(sock, args.timeout)
        print("READY_OK", flush=True)
        start_field(sock)
        if args.expect_native_peer:
            observe_native_spawn(sock, args.timeout)
    finally:
        sock.close()


if __name__ == "__main__":
    main()
