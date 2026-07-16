"""Teste diferencial: drива um World Server (nativo OU .NET) pela mesma sequencia
de pacotes e despeja os bytes crus de cada resposta. Rodar contra os dois e diff:
respostas identicas = reconstrucao fiel (a cifra AES e deterministica, IV fixo)."""
import socket, struct, sys

PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 40708

def frame(op, seq, data):
    c = struct.pack("<HH", op, seq) + data
    return struct.pack("<H", len(c) + 2) + c

def recv_frame(s):
    try:
        hdr = b""
        while len(hdr) < 2:
            ch = s.recv(2 - len(hdr))
            if not ch: return None
            hdr += ch
        size = struct.unpack("<H", hdr)[0]
        body = b""
        while len(body) < size - 2:
            ch = s.recv(size - 2 - len(body))
            if not ch: break
            body += ch
        return hdr + body
    except socket.timeout:
        return None

def step(s, label, op, seq, data):
    s.sendall(frame(op, seq, data))
    r = recv_frame(s)
    print(f"{label}: {'(sem resposta)' if r is None else r.hex()}")

s = socket.create_connection(("127.0.0.1", PORT), timeout=4)
s.settimeout(2.0)
# login verifyMode=4 (pula MD5), conta/senha locais 'test'
step(s, "login    ", 0x0C, 0, bytes([4]) + b"\x00test\x00test\x00" + struct.pack("<H", 0))
step(s, "enterchan", 0x01, 1, b"")
step(s, "worldinfo", 0x02, 2, b"")
s.close()
