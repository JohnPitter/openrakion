import sys

from world_room_probe import connect, describe, has_prefix, receive, request


STAGE_ID = 3
RANK = 4
EXP = 40
GOLD = 83


def stage_result(stage: int, rank: int, exp: int, gold: int) -> bytes:
    return (bytes((stage, rank, 0)) + exp.to_bytes(4, "little")
            + gold.to_bytes(4, "little") + bytes(12))


def expect_no_ack(label: str, frames: list[bytes]) -> None:
    describe(label, frames)
    if has_prefix(frames, b"\x53\x00"):
        raise RuntimeError(f"{label}: resultado rejeitado recebeu ACK 0x53")


def expect_ack(label: str, frames: list[bytes]) -> None:
    describe(label, frames)
    expected = bytes((0x53, 0, 0, STAGE_ID, RANK, 0))
    if not has_prefix(frames, expected):
        raise RuntimeError(f"{label}: ACK esperado não chegou: {expected.hex()}")


def main() -> None:
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 40708
    client = connect(port, "test", 1)
    try:
        create_payload = (b"REStage\x00\x00probe\x00" + bytes((STAGE_ID, 0, 1))
                          + (288).to_bytes(2, "little") + bytes((0, 1, 10, 0)))
        client.sendall(request(0x3B, 2, create_payload))
        created = receive(client)
        describe("create-stage", created)
        if not has_prefix(created, b"\x3b\x00\x00"):
            raise RuntimeError("sala solo não foi criada")

        client.sendall(request(0x43, 3))
        started = receive(client)
        describe("start-stage", started)
        if not has_prefix(started, b"\x43\x00\x00"):
            raise RuntimeError("partida solo não iniciou")

        client.sendall(request(0x4B, 4, bytes(72)))
        spawned = receive(client)
        describe("spawn-stage", spawned)
        if not has_prefix(spawned, b"\x48\x00"):
            raise RuntimeError("stage não entrou no round")

        client.sendall(request(0x53, 5, stage_result(STAGE_ID, RANK, EXP, GOLD)))
        expect_no_ack("result-before-clear", receive(client))

        client.sendall(request(0x4A, 6, b"\x02"))
        cleared = receive(client)
        describe("stage-clear", cleared)
        if not has_prefix(cleared, b"\x4a\x00"):
            raise RuntimeError("clear não produziu tela de resultado")

        client.sendall(request(0x53, 7, stage_result(STAGE_ID + 1, RANK, EXP, GOLD)))
        expect_no_ack("result-wrong-stage", receive(client))

        valid = stage_result(STAGE_ID, RANK, EXP, GOLD)
        client.sendall(request(0x53, 8, valid))
        expect_ack("result-applied", receive(client))

        client.sendall(request(0x53, 9, valid))
        expect_ack("result-replay", receive(client))

        client.sendall(request(0x53, 10, stage_result(STAGE_ID, RANK, EXP, GOLD + 1)))
        expect_no_ack("result-divergent-replay", receive(client))

        print(f"OK Stage/PvE stage={STAGE_ID} rank={RANK} exp=+{EXP} gold=+{GOLD}")
        print("OK pré-clear, stage divergente e replay divergente foram rejeitados")
        print("OK replay idêntico recebeu ACK sem novo crédito")
    finally:
        client.close()


if __name__ == "__main__":
    main()
