"""MITM de captura p/ o sistema de BOTS: passthrough cliente <-> world ORIGINAL (Docker
openrakion-server:latest), LOGANDO TCP (frames de sala/roster, decifrados) E UDP (gameplay:
move/ação 0x30a, input 0040, tick 1583). Não altera nada (só o byte de porta do handshake UDP,
como o mitm_cap original). Objetivo: capturar uma sessão PvP com movimento p/ golden-confirmar o
datagrama do bot (docs/bot-movement-capture.md) e os frames de roster.

Uso:
  1) Suba o world ORIGINAL:   docker run --rm -p 40708:40708/tcp -p 40708:40708/udp -p 40709:40709/udp openrakion-server:latest
  2) python tools/mitm_botcap.py
  3) Aponte o cliente p/ 127.0.0.1:41708 (o proxy), entre numa sala PvP e ANDE / GIRE / ATAQUE
     em movimentos isolados e repetidos. (1 cliente já emite o 0x30a; 2 clientes capturam o roster.)
  4) python tools/decode_bot_action.py   -> decodifica o 0x30a e confere contra BotMovement.
Log: C:\\temp\\botcap.log
"""
import socket, struct, threading, time

KEY = bytes([0xE1,0x3A,0x7E,0xF5,0x37,0x2C,0x10,0x4D,0x4E,0xCE,0xB3,0x0C,0x56,0x26,0xA4,0x8E])
ORIG_IP = "127.0.0.1"
TCP_IN, TCP_OUT = 41708, 40708
UDP_TRIPLES = [(41708, 40708, 51708), (41709, 40709, 51709)]
LOG = r"C:\temp\botcap.log"
_lock = threading.Lock(); _t0 = time.time()

def log(m):
    with _lock:
        with open(LOG, "a") as f:
            f.write("[t=%07.3f] %s\n" % (time.time() - _t0, m))

def dec(blob):
    """Decifra o canal lobby (AES-128-ECB, 16->12 por bloco). C->S é texto; W->C é cifrado."""
    try:
        from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
    except Exception:
        return blob
    n = (len(blob) // 16) * 16
    if n == 0:
        return blob
    d = Cipher(algorithms.AES(KEY), modes.ECB()).decryptor()
    raw = d.update(blob[:n]) + d.finalize()
    return b"".join(raw[i + 4:i + 16] for i in range(0, n, 16))

def tcp_pump(src, dst, tag):
    buf = b""
    try:
        while True:
            d = src.recv(8192)
            if not d:
                break
            dst.sendall(d); buf += d
            while len(buf) >= 2:
                size = struct.unpack_from("<H", buf, 0)[0]
                if size < 2 or len(buf) < size:
                    break
                fr = buf[:size]; buf = buf[size:]
                content = fr[2:]
                p = dec(content) if (len(content) >= 16 and len(content) % 16 == 0) else content
                op = struct.unpack_from("<H", p, 0)[0] if len(p) >= 2 else -1
                b = struct.unpack_from("<H", p, 2)[0] if len(p) >= 4 else -1
                log("%s size=%d u16a=%#06x u16b=%#06x len=%d data=%s" % (tag, size, op, b, len(p), p.hex()))
    except Exception as e:
        log("%s fim: %s" % (tag, e))
    try: dst.shutdown(socket.SHUT_WR)
    except Exception: pass

def tcp_handle(client):
    try:
        world = socket.socket(); world.connect((ORIG_IP, TCP_OUT))
    except Exception as e:
        log("erro world: %s" % e); client.close(); return
    log("=== nova conexao TCP ===")
    threading.Thread(target=tcp_pump, args=(client, world, "C->W"), daemon=True).start()
    threading.Thread(target=tcp_pump, args=(world, client, "W->C"), daemon=True).start()

def tcp_srv():
    s = socket.socket(); s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    s.bind(("0.0.0.0", TCP_IN)); s.listen(8); log("TCP %d->%d" % (TCP_IN, TCP_OUT))
    while True:
        c, _ = s.accept(); threading.Thread(target=tcp_handle, args=(c,), daemon=True).start()

def udp_px(inp, outp, wp):
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM); s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1); s.bind(("0.0.0.0", inp))
    w = socket.socket(socket.AF_INET, socket.SOCK_DGRAM); w.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1); w.bind(("0.0.0.0", wp))
    ca = [None]
    def fw():  # world -> cliente
        while True:
            try: d, _ = w.recvfrom(65535)
            except Exception: break
            log("W->C UDP:%d %dB %s" % (inp, len(d), d.hex()))
            if ca[0]: s.sendto(d, ca[0])
    threading.Thread(target=fw, daemon=True).start()
    while True:  # cliente -> world
        try: d, a = s.recvfrom(65535)
        except Exception: break
        ca[0] = a
        log("C->W UDP:%d %dB %s" % (inp, len(d), d.hex()))
        d2 = bytearray(d)
        if len(d2) >= 19:  # patch do byte de porta do handshake (igual mitm_cap)
            d2[17] = (wp >> 8) & 0xff; d2[18] = wp & 0xff
        w.sendto(bytes(d2), (ORIG_IP, outp))

if __name__ == "__main__":
    open(LOG, "w").write("botcap start %s\n" % time.strftime("%H:%M:%S"))
    threading.Thread(target=tcp_srv, daemon=True).start()
    for ip, op, wp in UDP_TRIPLES:
        threading.Thread(target=udp_px, args=(ip, op, wp), daemon=True).start()
    print("MITM botcap up -> %s (cliente: 127.0.0.1:%d)" % (LOG, TCP_IN))
    while True:
        time.sleep(60)
