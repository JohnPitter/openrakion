"""UDP+TCP proxy logger: 40708 -> 40808 (original Docker server).
Logs all packets to C:\temp\cap.log with hex dump."""
import socket, threading, time, struct, os

ORIG = "127.0.0.1"
TCP_IN, TCP_OUT = 40708, 40808
UDP_IN, UDP_OUT = 40708, 40808
BROKER_IN, BROKER_OUT = 40706, 40806
LOG = r"C:\temp\cap.log"
_lock = threading.Lock()

def log(msg):
    ts = time.strftime("%H:%M:%S")
    line = f"[{ts}] {msg}"
    with _lock:
        print(line)
        with open(LOG, "a", encoding="utf-8") as f:
            f.write(line + "\n")

def hexdump(data, max_bytes=32):
    return data[:max_bytes].hex()

def msg_type(data):
    if len(data) >= 2:
        return struct.unpack_from("<H", data, 0)[0]
    return -1

# --- TCP proxy ---
def tcp_pump(src, dst, label):
    buf = b""
    try:
        while True:
            data = src.recv(8192)
            if not data:
                break
            dst.sendall(data)
            buf += data
            while len(buf) >= 2:
                size = struct.unpack_from("<H", buf, 0)[0]
                if size < 2 or len(buf) < size:
                    break
                frame = buf[:size]
                buf = buf[size:]
                mt = msg_type(frame[2:] if len(frame) > 2 else b"")
                log(f"TCP {label} {len(frame)}B type=0x{mt:04X} data={hexdump(frame)}")
    except Exception as e:
        log(f"TCP {label} erro: {e}")

def tcp_handle(client):
    try:
        remote = socket.socket()
        remote.connect((ORIG, TCP_OUT))
    except Exception as e:
        log(f"TCP connect erro: {e}")
        client.close()
        return
    log("TCP nova conexao")
    threading.Thread(target=tcp_pump, args=(client, remote, "C->W"), daemon=True).start()
    threading.Thread(target=tcp_pump, args=(remote, client, "W->C"), daemon=True).start()

def tcp_server(port, label):
    s = socket.socket()
    s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    s.bind(("0.0.0.0", port))
    s.listen(8)
    log(f"TCP {label} ouvindo na {port}")
    while True:
        c, a = s.accept()
        threading.Thread(target=tcp_handle, args=(c,), daemon=True).start()

# --- UDP proxy ---
def udp_proxy(port_in, port_out, label):
    """Proxy UDP que loga cada pacote."""
    sock_in = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock_in.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    sock_in.bind(("0.0.0.0", port_in))

    sock_out = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    sock_out.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    # sock_out binds to a random port to send from

    client_addr = [None]
    remote_addr = (ORIG, port_out)

    def fwd_remote():
        """Forward from remote to client."""
        while True:
            try:
                data, _ = sock_out.recvfrom(65535)
                if client_addr[0]:
                    sock_in.sendto(data, client_addr[0])
                    mt = msg_type(data)
                    log(f"UDP W->C [{label}] {len(data)}B type=0x{mt:04X} data={hexdump(data)}")
            except:
                break

    threading.Thread(target=fwd_remote, daemon=True).start()

    log(f"UDP {label} ouvindo na {port_in} -> {port_out}")
    while True:
        try:
            data, addr = sock_in.recvfrom(65535)
            client_addr[0] = addr
            sock_out.sendto(data, remote_addr)
            mt = msg_type(data)
            log(f"UDP C->W [{label}] {len(data)}B type=0x{mt:04X} from={addr} data={hexdump(data)}")
        except:
            break

# --- Main ---
os.makedirs(os.path.dirname(LOG), exist_ok=True)
open(LOG, "w").write(f"cap start {time.strftime('%H:%M:%S')}\n")

threading.Thread(target=tcp_server, args=(TCP_IN, "world"), daemon=True).start()
threading.Thread(target=tcp_server, args=(BROKER_IN, "broker"), daemon=True).start()
threading.Thread(target=udp_proxy, args=(UDP_IN, UDP_OUT, "gameplay"), daemon=True).start()

print(f"Proxy ativo: TCP {TCP_IN}->{TCP_OUT}, TCP {BROKER_IN}->{BROKER_OUT}, UDP {UDP_IN}->{UDP_OUT}")
print(f"Log: {LOG}")
while True:
    time.sleep(60)
