"""Captura completa de tráfego: TCP (decifrado) + UDP (gameplay).
Cliente --TCP 41708--> [proxy] --TCP--> world ORIGINAL (40708).
Cliente --UDP 41708/41709--> [proxy] --UDP--> world ORIGINAL (40708/40709).
Log: C:\temp\botcap.log"""
import socket, struct, threading, time
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes

KEY = bytes([0xE1,0x3A,0x7E,0xF5,0x37,0x2C,0x10,0x4D,0x4E,0xCE,0xB3,0x0C,0x56,0x26,0xA4,0x8E])
ORIG_IP = "127.0.0.1"
TCP_IN, TCP_OUT = 40708, 40808
UDP_IN1, UDP_OUT1, UDP_W1 = 40708, 40808, 50708
UDP_IN2, UDP_OUT2, UDP_W2 = 40709, 40809, 50709
LOG = r"C:\temp\botcap.log"
_lock = threading.Lock()
_t0 = time.time()

def log(m):
    with _lock:
        with open(LOG, "a") as f:
            f.write("[t=%07.3f] %s\n" % (time.time() - _t0, m))

def dec(blob):
    n = (len(blob) // 16) * 16
    if n == 0: return blob
    d = Cipher(algorithms.AES(KEY), modes.ECB()).decryptor()
    raw = d.update(blob[:n]) + d.finalize()
    return b"".join(raw[i+4:i+16] for i in range(0, n, 16))

def op_of(frame):
    c = frame[2:]
    p = dec(c) if (len(c) >= 16 and len(c) % 16 == 0) else c
    op = struct.unpack_from("<H", p, 0)[0] if len(p) >= 2 else -1
    return op, p

def pump(src, dst, tag):
    buf = b""
    try:
        while True:
            d = src.recv(8192)
            if not d: break
            dst.sendall(d)
            buf += d
            while len(buf) >= 2:
                size = struct.unpack_from("<H", buf, 0)[0]
                if size < 2 or len(buf) < size: break
                fr = buf[:size]; buf = buf[size:]
                op, p = op_of(fr)
                b = struct.unpack_from("<H", p, 2)[0] if len(p) >= 4 else -1
                log("%s TCP RAW %dB data=%s | size=%d" op=%#06x aux=%#06x len=%d data=%s" % (tag, size, op, b, len(p), p.hex()))
    except Exception as e:
        log("%s TCP fim: %s" % (tag, e))
    try: dst.shutdown(socket.SHUT_WR)
    except: pass

def handle(client):
    try:
        world = socket.socket()
        world.connect((ORIG_IP, TCP_OUT))
    except Exception as e:
        log("erro world: %s" % e); client.close(); return
    log("=== nova conexao TCP ===")
    threading.Thread(target=pump, args=(client, world, "C->W"), daemon=True).start()
    threading.Thread(target=pump, args=(world, client, "W->C"), daemon=True).start()

def tcp_srv():
    s = socket.socket()
    s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    s.bind(("0.0.0.0", TCP_IN)); s.listen(8)
    log("TCP %d->%d" % (TCP_IN, TCP_OUT))
    while True:
        c, a = s.accept()
        threading.Thread(target=handle, args=(c,), daemon=True).start()

def udp_px(inp, outp, wp, label):
    """Proxy UDP que LOGA cada pacote (hex dump dos primeiros 40 bytes)."""
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    s.bind(("0.0.0.0", inp))

    w = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    w.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    w.bind(("0.0.0.0", wp))

    ca = [None]  # client address

    def fwd():
        """World -> Client"""
        while True:
            try:
                d, _ = w.recvfrom(65535)
                if ca[0]:
                    s.sendto(d, ca[0])
                    # Log W->C UDP
                    msg_type = struct.unpack_from("<H", d, 0)[0] if len(d) >= 2 else 0
                    log("W->C UDP [%s] %dB type=0x%04X data=%s" % (label, len(d), msg_type, d[:40].hex()))
            except:
                break

    threading.Thread(target=fwd, daemon=True).start()

    while True:
        try:
            d, a = s.recvfrom(65535)
            ca[0] = a
            d2 = bytearray(d)
            if len(d2) >= 19:
                d2[17] = (wp >> 8) & 0xff
                d2[18] = wp & 0xff
            w.sendto(bytes(d2), (ORIG_IP, outp))
            # Log C->W UDP
            msg_type = struct.unpack_from("<H", d, 0)[0] if len(d) >= 2 else 0
            log("C->W UDP [%s] %dB type=0x%04X from=%s data=%s" % (label, len(d), msg_type, a, d[:40].hex()))
        except:
            break

# Start
open(LOG, "w").write("botcap start %s\n" % time.strftime("%H:%M:%S"))
threading.Thread(target=tcp_srv, daemon=True).start()
threading.Thread(target=udp_px, args=(UDP_IN1, UDP_OUT1, UDP_W1, "port1"), daemon=True).start()
threading.Thread(target=udp_px, args=(UDP_IN2, UDP_OUT2, UDP_W2, "port2"), daemon=True).start()
print("botcap up: TCP+UDP %d->%d (log: %s)" % (TCP_IN, TCP_OUT, LOG))
while True:
    time.sleep(60)

