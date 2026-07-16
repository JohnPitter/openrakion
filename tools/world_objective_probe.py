import sys
import time

from world_room_probe import connect, describe, has_prefix, receive, request


def start_two_player_match(port: int, mode: int, name: bytes):
    first = connect(port, "test", 1)
    second = connect(port, "test2", 9001)
    create = name + b"\x00probe\x00" + bytes((1, mode, 1))
    create += (432).to_bytes(2, "little") + bytes((1, 1, 99, 0))
    first.sendall(request(0x3B, 2, create))
    created = receive(first)
    create_frame = next((frame for frame in created if frame.startswith(b"\x3b\x00\x00")), None)
    if create_frame is None:
        raise RuntimeError(f"modo {mode}: criação da sala falhou")
    field_id = int.from_bytes(create_frame[3:5], "little")

    second.sendall(request(0x38, 2, field_id.to_bytes(2, "little") + b"\x00"))
    receive(first)
    receive(second)
    second.sendall(request(0x3E, 3))
    receive(first)
    receive(second)
    second.sendall(request(0x3D, 4, b"\x01"))
    receive(first)
    receive(second)
    first.sendall(request(0x43, 3))
    start_first = receive(first)
    start_second = receive(second)
    if not has_prefix(start_first + start_second, b"\x43\x00\x00"):
        describe("start-first", start_first)
        describe("start-second", start_second)
        raise RuntimeError(f"modo {mode}: partida não iniciou")
    first.sendall(request(0x4B, 4, bytes(72)))
    stage_first = receive(first)
    second.sendall(request(0x4B, 5, bytes(72)))
    round_first = receive(first)
    round_second = receive(second)
    if not has_prefix(stage_first + round_first, b"\x48\x00"):
        raise RuntimeError(f"modo {mode}: host não entrou no stage")
    if not has_prefix(round_second, b"\x48\x00"):
        raise RuntimeError(f"modo {mode}: membro não entrou no stage")
    return first, second


def probe_golem(port: int) -> None:
    first, second = start_two_player_match(port, 1, b"REGolem\x00")
    try:
        second.sendall(request(0x4D, 6, bytes.fromhex("00004b00")))
        first_frames = receive(first)
        second_frames = receive(second)
        describe("golem-objective-host", first_frames)
        describe("golem-objective-reporter", second_frames)
        expected = bytes.fromhex("4a0002000001")
        if not has_prefix(first_frames, expected) or not has_prefix(second_frames, expected):
            raise RuntimeError("0x4D não publicou o 0x4A exato aos dois membros")
        first.sendall(request(0x46, 5, bytes((2,))))
        receive(first)
        receive(second)
        second.sendall(request(0x46, 7, bytes((2,))))
        receive(first)
        receive(second)
    finally:
        first.close()
        second.close()

    time.sleep(1.5)
    first, second = start_two_player_match(port, 1, b"REGolemDeath\x00")
    try:
        first.sendall(request(0x4F, 5, bytes((0, 10))))
        first_frames = receive(first)
        second_frames = receive(second)
        describe("golem-death-host", first_frames)
        describe("golem-death-killer", second_frames)
        death = bytes.fromhex("4f0000000a0a0a")
        round_end = bytes.fromhex("4a0001000001")
        if not has_prefix(first_frames, death) or not has_prefix(second_frames, death):
            raise RuntimeError("morte Golem não publicou o 0x4F exato aos dois membros")
        if not has_prefix(first_frames, round_end) or not has_prefix(second_frames, round_end):
            raise RuntimeError("eliminação Golem não publicou o 0x4A exato aos dois membros")
    finally:
        first.close()
        second.close()


def probe_boss(port: int) -> None:
    first, second = start_two_player_match(port, 4, b"REBoss\x00")
    try:
        first.sendall(request(0x60, 5, bytes.fromhex("044101")))
        first_frames = receive(first)
        second_frames = receive(second)
        describe("boss-target-host", first_frames)
        describe("boss-target-peer", second_frames)
        if has_prefix(first_frames + second_frames, b"\x60\x00"):
            raise RuntimeError("0x60 foi transmitido, mas o World original apenas grava o alvo")

        first.sendall(request(0x4B, 6, (2).to_bytes(2, "little") + b"ok"))
        relay = receive(second)
        if not has_prefix(relay, b"\x4b\x00\x00\x02\x00ok"):
            raise RuntimeError("sessão não permaneceu funcional após o reporte 0x60")

        first.sendall(request(0x4F, 7, bytes((0, 10))))
        first_frames = receive(first)
        second_frames = receive(second)
        describe("boss-leader-death-host", first_frames)
        describe("boss-leader-death-killer", second_frames)
        death = bytes.fromhex("4f0000000a0000")
        round_end = bytes.fromhex("4a0001000001")
        if not has_prefix(first_frames, death) or not has_prefix(second_frames, death):
            raise RuntimeError("morte do líder Boss não publicou o 0x4F exato")
        if not has_prefix(first_frames, round_end) or not has_prefix(second_frames, round_end):
            raise RuntimeError("morte do líder Boss não encerrou o round para o time oposto")
    finally:
        first.close()
        second.close()


def main() -> None:
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 40708
    probe_golem(port)
    time.sleep(0.5)
    probe_boss(port)
    print("OK objetivos e mortes decisivas de Golem/Boss seguem os contratos originais")


if __name__ == "__main__":
    main()
