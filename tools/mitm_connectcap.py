"""MITM de captura do CONNECT P2P (mini-peer do bot, slice RakionServer.Peer / blueprint M1+M3).

ALVO: gravar o CONTEUDO COMPLETO do stream reliable do handshake de SESSAO da Serious Engine
(CONNECT 7/8 / STATEDELTA 9/10 / CRC 11/12/13 / SEQ_ADDPLAYER 22) entre 2 clientes que entram
na MESMA sala/stage. Diferenca p/ o mitm_botcap: (a) NAO trunca NADA (hex inteiro de cada
datagrama, ambos UDP + TCP decifrado); (b) RECONSTROI o byte-stream reliable a partir do
OFFSET (token) dos 0x0304 e dumpa o que de fato anda no fio em cada offset; (c) destaca a 1a
aparicao de cada msgType e a transicao 0x0304 -> 1o 0x030a (abertura do gate).

TOPOLOGIA (igual mitm_cap, NAO a "transparente" do botcap): o proxy escuta nas PORTAS-PROXY
(41708/41709 + TCP 41708) e encaminha p/ o ORIGINAL nas portas padrao (40708/40709). O cliente
e' apontado p/ 127.0.0.1:41708. Assim o container original (rakion-v258) fica intocado nas
40706/40708/40709 e o MITM nao colide com ele.

Uso:
  1) Original ja' no ar (docker start rakion-v258) escutando 40708/40709.
  2) python tools/mitm_connectcap.py
  3) Lance os 2 clientes pelo launcher (test/test e test2/test2). Aponte AMBOS p/
     127.0.0.1:41708 (host do servidor no launcher). Entrem na MESMA sala e START juntos.
  4) Deixe ~15s de jogo a dois e feche. O handshake (se estiver no fio) fica em connect_cap.log.

Log: C:\\temp\\connect_cap.log  (idempotente: reescreve o header a cada start).

NOTA DE RESEARCH (achado da RE, netcode_peer_re.out.txt secao 1.2): os 0x0304 relayados pelo
worldserv tem 12-13B = SO' o header de offset; os ~103B/frame de payload reliable vivem no
reassembly LOCAL de cada engine e podem NAO aparecer no fio. Este MITM mede isso empiricamente:
se os 0x0304 continuarem 12-13B numa partida 2-peer real, M1 esta' confirmado (payload off-wire)
e o caminho e' o mini-peer que SINTETIZA as CNetworkMessages (nao replay). Se aparecer payload,
ele e' dumpado aqui por offset p/ reassemblar.
"""
import os
import socket
import struct
import threading
import time

KEY = bytes([0xE1, 0x3A, 0x7E, 0xF5, 0x37, 0x2C, 0x10, 0x4D,
             0x4E, 0xCE, 0xB3, 0x0C, 0x56, 0x26, 0xA4, 0x8E])
ORIG_IP = "127.0.0.1"

# Portas-PROXY (cliente aponta aqui) -> ORIGINAL (container) -> socket-de-volta local.
TCP_IN = int(os.environ.get("RKCAP_TCP_IN", 41708))
TCP_OUT = int(os.environ.get("RKCAP_TCP_OUT", 40708))
# (in_proxy, out_original, local_back) por canal de gameplay UDP.
UDP_TRIPLES = [(41708, 40708, 51708), (41709, 40709, 51709)]
LOG = r"C:\temp\connect_cap.log"

_lock = threading.Lock()
_t0 = time.time()
_seen_msgtypes = set()   # 1a aparicao de cada msgType UDP
_seen_session_msg = set()  # 1a aparicao de cada tipo de CNetworkMessage reliable
# Reassembly do byte-stream reliable por (direcao, sub-canal): offset -> bytes do frame.
_reliable = {}


def log(m):
    with _lock:
        with open(LOG, "a", encoding="utf-8") as f:
            f.write("[t=%08.3f] %s\n" % (time.time() - _t0, m))


def dec_tcp(blob):
    """Decifra o canal lobby/TCP (AES-128-ECB, 16->12 por bloco). C->S texto; W->C cifrado."""
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


# ---------------------------------------------------------------------------
# TCP do world (lobby/sala/char-select). NAO trunca; loga frame inteiro decifrado.
# ---------------------------------------------------------------------------
def tcp_pump(src, dst, tag):
    buf = b""
    try:
        while True:
            d = src.recv(8192)
            if not d:
                break
            dst.sendall(d)
            buf += d
            while len(buf) >= 2:
                size = struct.unpack_from("<H", buf, 0)[0]
                if size < 2 or len(buf) < size:
                    break
                fr = buf[:size]
                buf = buf[size:]
                content = fr[2:]
                p = dec_tcp(content) if (len(content) >= 16 and len(content) % 16 == 0) else content
                op = struct.unpack_from("<H", p, 0)[0] if len(p) >= 2 else -1
                aux = struct.unpack_from("<H", p, 2)[0] if len(p) >= 4 else -1
                log("TCP %s size=%d op=%#06x aux=%#06x len=%d data=%s"
                    % (tag, size, op, aux, len(p), p.hex()))
    except Exception as e:
        log("TCP %s fim: %s" % (tag, e))
    try:
        dst.shutdown(socket.SHUT_WR)
    except Exception:
        pass


def tcp_handle(client):
    try:
        world = socket.socket()
        world.connect((ORIG_IP, TCP_OUT))
    except Exception as e:
        log("TCP erro world: %s" % e)
        client.close()
        return
    log("=== nova conexao TCP (cliente) ===")
    threading.Thread(target=tcp_pump, args=(client, world, "C->W"), daemon=True).start()
    threading.Thread(target=tcp_pump, args=(world, client, "W->C"), daemon=True).start()


def tcp_srv():
    s = socket.socket()
    s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    s.bind(("0.0.0.0", TCP_IN))
    s.listen(8)
    log("TCP proxy %d -> original %d" % (TCP_IN, TCP_OUT))
    while True:
        c, _ = s.accept()
        threading.Thread(target=tcp_handle, args=(c,), daemon=True).start()


# ---------------------------------------------------------------------------
# Analise dos frames de transporte UDP da engine (netcode_peer_re secao 1.2).
#   [u16 msgType LE][u32 seq][u8 role][u8 sub][u32 token=offset][tail]
# Destaca 1a aparicao de cada msgType e reconstroi o byte-stream reliable por offset.
# ---------------------------------------------------------------------------
def analyze_udp(direction, port, pkt):
    if len(pkt) < 2:
        return ""
    mt = struct.unpack_from("<H", pkt, 0)[0]   # ex.: 0x030a, 0x0304...
    notes = []

    key = (port, mt)
    if key not in _seen_msgtypes:
        _seen_msgtypes.add(key)
        notes.append("PRIMEIRA-VEZ msgType=%#06x" % mt)

    # PUSH/ACK reliable: extrai offset e dumpa o CORPO (se houver) por offset.
    if mt in (0x0304, 0x0305) and len(pkt) >= 6:
        seq = struct.unpack_from("<I", pkt, 2)[0]
        role = pkt[6] if len(pkt) > 6 else -1
        sub = pkt[7] if len(pkt) > 7 else -1
        token = struct.unpack_from("<I", pkt, 8)[0] if len(pkt) >= 12 else -1
        body = pkt[12:] if len(pkt) > 12 else b""
        notes.append("RELIABLE seq=%#x role=%#04x sub=%#04x offset=%#010x bodylen=%d"
                     % (seq, role & 0xff, sub & 0xff, token & 0xffffffff, len(body)))
        if body:  # caso (i) do blueprint: payload NO fio -> guarda por offset p/ reassemblar
            chan = (direction, sub & 0xff)
            _reliable.setdefault(chan, {})[token & 0xffffffff] = body
            notes.append("*** PAYLOAD-NO-FIO chan=%s off=%#x %s" % (chan, token & 0xffffffff, body.hex()))
            # Se o 1o byte do payload for um tipo de CNetworkMessage de sessao, sinaliza.
            t = body[0] & 0x3f
            if t in (5, 6, 7, 8, 9, 10, 11, 12, 13, 21, 22, 23, 24, 25, 26) and t not in _seen_session_msg:
                _seen_session_msg.add(t)
                notes.append("### CNetworkMessage TIPO=%d (handshake de sessao!)" % t)

    # 1o 0x030a = gate de gameplay abriu (transicao nitida do load p/ o jogo).
    if mt == 0x030a and ("gate", port) not in _seen_msgtypes:
        _seen_msgtypes.add(("gate", port))
        notes.append(">>> GATE ABERTO: 1o 0x030a (move) nesta porta — fim do load do stream <<<")

    return ("  | " + " ; ".join(notes)) if notes else ""


def udp_px(inp, outp, wp):
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    s.bind(("0.0.0.0", inp))
    w = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    w.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    w.bind(("0.0.0.0", wp))
    ca = [None]

    def fw():  # original -> cliente
        while True:
            try:
                d, _ = w.recvfrom(65535)
            except Exception:
                break
            extra = analyze_udp("W->C", inp, d)
            log("W->C UDP:%d %dB %s%s" % (inp, len(d), d.hex(), extra))
            if ca[0]:
                s.sendto(d, ca[0])

    threading.Thread(target=fw, daemon=True).start()
    while True:  # cliente -> original
        try:
            d, a = s.recvfrom(65535)
        except Exception:
            break
        ca[0] = a
        extra = analyze_udp("C->W", inp, d)
        log("C->W UDP:%d %dB %s%s" % (inp, len(d), d.hex(), extra))
        d2 = bytearray(d)
        if len(d2) >= 19:   # patch do byte de porta do handshake UDP (igual mitm_cap/botcap)
            d2[17] = (wp >> 8) & 0xff
            d2[18] = wp & 0xff
        w.sendto(bytes(d2), (ORIG_IP, outp))


def dump_reliable_summary():
    """Imprime o byte-stream reliable reassemblado por canal (so se houve payload no fio)."""
    if not _reliable:
        log("RESUMO RELIABLE: nenhum 0x0304 trouxe payload no fio (so headers de offset). "
            "=> M1 confirma: payload do reliable NAO trafega relayado; mini-peer SINTETIZA.")
        return
    for chan, byoff in sorted(_reliable.items()):
        stream = b"".join(byoff[o] for o in sorted(byoff))
        log("RESUMO RELIABLE chan=%s offsets=%d totbytes=%d stream=%s"
            % (chan, len(byoff), len(stream), stream.hex()))


if __name__ == "__main__":
    os.makedirs(r"C:\temp", exist_ok=True)
    with open(LOG, "w", encoding="utf-8") as f:
        f.write("connect_cap start %s | proxy TCP %d->%d, UDP %s\n"
                % (time.strftime("%H:%M:%S"), TCP_IN, TCP_OUT,
                   ",".join("%d->%d" % (a, b) for a, b, _ in UDP_TRIPLES)))
    threading.Thread(target=tcp_srv, daemon=True).start()
    for ip, op, wp in UDP_TRIPLES:
        threading.Thread(target=udp_px, args=(ip, op, wp), daemon=True).start()
    print("MITM connect-cap up -> %s" % LOG)
    print("Aponte os 2 clientes p/ 127.0.0.1:%d, entrem na MESMA sala e START juntos." % TCP_IN)
    print("Ctrl+C encerra e imprime o resumo do reliable reassemblado.")
    try:
        while True:
            time.sleep(60)
    except KeyboardInterrupt:
        dump_reliable_summary()
        print("encerrado; resumo em %s" % LOG)
