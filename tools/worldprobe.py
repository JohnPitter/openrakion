"""Oraculo do world: conecta no world (40708), envia o LOGIN capturado do client
real (cifrado) e DECIFRA as respostas. Rodar contra o RakionWorldServ.exe ORIGINAL
e contra o nosso .NET pra comparar byte-a-byte, sem precisar do client real.

Uso: python worldprobe.py [porta]
"""
import socket, sys, time
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes

KEY = bytes([0xE1,0x3A,0x7E,0xF5,0x37,0x2C,0x10,0x4D,0x4E,0xCE,0xB3,0x0C,0x56,0x26,0xA4,0x8E])
PORT = int(sys.argv[1]) if len(sys.argv) > 1 else 40708

# login capturado do client real (test/test), 34 bytes: [22 00 size][32 cifrado]
LOGIN = bytes.fromhex("22008067F00AA7327DB73801A949A8A582EEC04B975693357E0192BD376CD3893017")

def decrypt(blob):
    """cada bloco 16 -> AES dec -> descarta 4 (IV) -> mantem 12."""
    dec = Cipher(algorithms.AES(KEY), modes.ECB()).decryptor()
    raw = dec.update(blob) + dec.finalize()
    out = b""
    for i in range(0, len(blob), 16):
        out += raw[i+4:i+16]
    return out

def show_frames(data, label):
    print("=== %s: %d bytes brutos: %s" % (label, len(data), data.hex()))
    off = 0
    while off + 2 <= len(data):
        size = int.from_bytes(data[off:off+2], "little")
        if size < 2 or off + size > len(data):
            print("  [frame parcial/invalido @ %d size=%d]" % (off, size)); break
        content = data[off+2:off+size]
        plain = decrypt(content) if (len(content) % 16 == 0 and len(content) >= 16) else content
        op = int.from_bytes(plain[0:2],"little") if len(plain)>=2 else -1
        sq = int.from_bytes(plain[2:4],"little") if len(plain)>=4 else -1
        body = plain[4:] if len(plain)>=4 else b""
        print("  FRAME size=%d  serverSeq/op=%#06x msgType/seq=%#06x" % (size, op, sq))
        print("    plaintext: %s" % plain.hex())
        print("    body ascii: %r" % body)
        off += size

s = socket.socket(); s.settimeout(6)
try:
    s.connect(("127.0.0.1", PORT))
    print(">> conectado :%d, enviando login (%d bytes)" % (PORT, len(LOGIN)))
    s.sendall(LOGIN)
    time.sleep(0.5)
    buf = b""
    try:
        while True:
            chunk = s.recv(8192)
            if not chunk: print(">> peer fechou"); break
            buf += chunk
            time.sleep(0.3)
    except socket.timeout:
        print(">> (timeout de recv — fim das respostas)")
    if buf:
        show_frames(buf, "RESPOSTA do world")
    else:
        print(">> NENHUMA resposta recebida")
except Exception as e:
    print("ERRO:", e)
finally:
    s.close()
