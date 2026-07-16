import socket
import sys
import time
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes

KEY = bytes.fromhex("e13a7ef5372c104d4eceb30c5626a48e")
LOGIN = bytes.fromhex("22008067F00AA7327DB73801A949A8A582EEC04B975693357E0192BD376CD3893017")


def encrypt(plain: bytes) -> bytes:
    plain += bytes((-len(plain)) % 12)
    raw = b"".join((0xC47F).to_bytes(4, "little") + plain[i:i + 12] for i in range(0, len(plain), 12))
    enc = Cipher(algorithms.AES(KEY), modes.ECB()).encryptor()
    body = enc.update(raw) + enc.finalize()
    return (len(body) + 2).to_bytes(2, "little") + body


def decrypt(body: bytes) -> bytes:
    dec = Cipher(algorithms.AES(KEY), modes.ECB()).decryptor()
    raw = dec.update(body) + dec.finalize()
    return b"".join(raw[i + 4:i + 16] for i in range(0, len(raw), 16))


def receive(sock: socket.socket) -> bytes:
    data = b""
    while True:
        try:
            part = sock.recv(8192)
            if not part:
                break
            data += part
        except (socket.timeout, ConnectionAbortedError, ConnectionResetError):
            break
    return data


def show(data: bytes) -> None:
    offset = 0
    while offset + 2 <= len(data):
        size = int.from_bytes(data[offset:offset + 2], "little")
        body = data[offset + 2:offset + size]
        plain = decrypt(body) if body and len(body) % 16 == 0 else body
        print(f"size={size} plain={plain.hex()} ascii={plain!r}")
        offset += size


port = int(sys.argv[1]) if len(sys.argv) > 1 else 40708
action = sys.argv[2] if len(sys.argv) > 2 else "create"
with socket.create_connection(("127.0.0.1", port), timeout=5) as sock:
    sock.settimeout(0.7)
    sock.sendall(LOGIN)
    time.sleep(0.4)
    bootstrap = receive(sock)
    if action == "login":
        show(bootstrap)
        sys.exit(0)
    request_seq = 1
    selected_actions = (
        "reset", "rename", "present-peek", "present-accept", "present-dispose",
        "storage-buy", "storage-sell", "storage-move", "buy-bag", "buy-char-slot",
        "buy-potion-slot", "buy-stage-rank-clear", "buy-stage-level-free", "hold"
    )
    if action in selected_actions:
        sock.sendall(encrypt(b"\x14\x00\x01\x00\x01\x00\x00\x00\x00\x00\x00\x00"))
        time.sleep(0.2)
        receive(sock)
        request_seq = 2
    inventory_actions = (
        "buy-bag", "buy-char-slot", "buy-potion-slot",
        "buy-stage-rank-clear", "buy-stage-level-free"
    )
    if action in inventory_actions:
        sock.sendall(encrypt(
            b"\x2c\x00" + request_seq.to_bytes(2, "little")
            + b"\xff\xff\xff\xff\x00\x00\x00\x00"
        ))
        time.sleep(0.2)
        receive(sock)
        request_seq += 1
    if action == "hold":
        duration = int(sys.argv[3]) if len(sys.argv) > 3 else 20
        time.sleep(duration)
        show(receive(sock))
        sys.exit(0)
    if action == "create":
        name = sys.argv[3] if len(sys.argv) > 3 else "ProbeRE"
        char_class = int(sys.argv[4]) if len(sys.argv) > 4 else 0
        variant = int(sys.argv[5]) if len(sys.argv) > 5 else 0
        request = b"\x12\x00\x01\x00" + name.encode("ascii") + b"\x00" + bytes((char_class, variant))
    elif action == "delete":
        request = b"\x13\x00\x01\x00\x01\x00\x00\x00sirmaster\x00"
    elif action == "buddy":
        request = b"\x15\x00\x01\x00ProbeBuddy\x00"
    elif action == "resolve":
        request = b"\x19\x00\x01\x00Arrocha\x00"
    elif action == "tutorial":
        request = b"\x1a\x00\x01\x00"
    elif action == "reset":
        payment_type = int(sys.argv[3]) if len(sys.argv) > 3 else 0
        payment_value = int(sys.argv[4]) if len(sys.argv) > 4 else 0
        request = b"\x1b\x00" + request_seq.to_bytes(2, "little") + bytes((payment_type,))
        if payment_type:
            request += payment_value.to_bytes(2, "little")
    elif action == "rename":
        name = sys.argv[3] if len(sys.argv) > 3 else "ProbeRename"
        payment_type = int(sys.argv[4]) if len(sys.argv) > 4 else 0
        payment_value = int(sys.argv[5]) if len(sys.argv) > 5 else 0
        request = b"\x1c\x00" + request_seq.to_bytes(2, "little") + name.encode("ascii") + b"\x00" + bytes((payment_type,))
        if payment_type:
            request += payment_value.to_bytes(2, "little")
    elif action == "present-peek":
        request = b"\x6b\x00" + request_seq.to_bytes(2, "little")
    elif action == "present-accept":
        pending_id = int(sys.argv[3])
        slot = int(sys.argv[4])
        request = (b"\x6c\x00" + request_seq.to_bytes(2, "little")
                   + pending_id.to_bytes(4, "little") + slot.to_bytes(2, "little"))
    elif action == "present-dispose":
        pending_id = int(sys.argv[3])
        request = (b"\x6d\x00" + request_seq.to_bytes(2, "little")
                   + pending_id.to_bytes(4, "little"))
    elif action == "storage-buy":
        item_id = int(sys.argv[3])
        currency = int(sys.argv[4]) if len(sys.argv) > 4 else 1
        coupon_slot = int(sys.argv[5]) if len(sys.argv) > 5 else None
        request = (b"\x2e\x00" + request_seq.to_bytes(2, "little")
                   + item_id.to_bytes(2, "little")
                   + bytes((currency, 1 if coupon_slot is not None else 0)))
        if coupon_slot is not None:
            request += coupon_slot.to_bytes(2, "little")
    elif action == "storage-sell":
        slot = int(sys.argv[3])
        request = b"\x2f\x00" + request_seq.to_bytes(2, "little") + bytes((slot,))
    elif action == "storage-move":
        src_type, src_slot, dest_type, dest_slot = map(int, sys.argv[3:7])
        request = (b"\x31\x00" + request_seq.to_bytes(2, "little")
                   + bytes((src_type, src_slot, dest_type, dest_slot)))
    elif action in ("buy-bag", "buy-char-slot"):
        opcode = 0x32 if action == "buy-bag" else 0x35
        mode = int(sys.argv[3]) if len(sys.argv) > 3 else 0
        product = int(sys.argv[4]) if len(sys.argv) > 4 else 0
        request = (opcode.to_bytes(2, "little") + request_seq.to_bytes(2, "little")
                   + bytes((mode,)) + (product.to_bytes(2, "little") if mode else b""))
    elif action in ("buy-potion-slot", "buy-stage-rank-clear", "buy-stage-level-free"):
        opcode = {
            "buy-potion-slot": 0x6f,
            "buy-stage-rank-clear": 0x70,
            "buy-stage-level-free": 0x71,
        }[action]
        request = opcode.to_bytes(2, "little") + request_seq.to_bytes(2, "little")
    else:
        raise ValueError(
            "action deve ser create, delete, buddy, resolve, tutorial, reset, rename, "
            "present-peek, present-accept, present-dispose, storage-buy, storage-sell "
            "storage-move, buy-bag, buy-char-slot, buy-potion-slot, "
            "buy-stage-rank-clear, buy-stage-level-free ou hold"
        )
    sock.sendall(encrypt(request))
    time.sleep(0.2)
    show(receive(sock))
