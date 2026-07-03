"""Analisa a captura loopback do canal P2P-direto (tools/cap_p2p_loopback.ps1) SEM dependencia
externa: le pcap classico E pcapng, anda os link-layers (Ethernet/Null/Loopback/Raw), filtra
UDP e separa o canal P2P-direto (portas 2300-2399) do relay do worldserv (40708/40709).

Para cada datagrama P2P imprime o msgType u16-LE do transporte da Serious Engine
(0x0201/0x0202 connect, 0x0304 push-reliable, 0x0305 ack, 0x0319 addr-update, 0x030a acao,
0x4000 input) e os primeiros bytes — base p/ cravar TAGV/STATEDELTA/CRC/ADDPLAYER byte-a-byte.

Uso:
  py tools/analyze_p2p_pcap.py C:\\temp\\p2p_loopback.pcapng [--max 0] [--hexlen 48]
"""
import struct
import sys

# ---- link-layer (DLT) handling -------------------------------------------------
DLT_NULL = 0          # BSD loopback: 4-byte family header
DLT_EN10MB = 1        # Ethernet
DLT_RAW = 101         # raw IP
DLT_LOOP = 108        # OpenBSD loopback: 4-byte family (big-endian)


def _strip_link(dlt, data):
    """Retorna o payload IP a partir do quadro de link, ou None se nao reconhecido."""
    if dlt == DLT_EN10MB:
        if len(data) < 14:
            return None
        etype = struct.unpack_from(">H", data, 12)[0]
        if etype == 0x0800:        # IPv4
            return data[14:]
        return None                # IPv6/ARP/etc — fora do escopo
    if dlt in (DLT_NULL, DLT_LOOP):
        if len(data) < 4:
            return None
        # family pode estar em qualquer endianness; 2 = AF_INET
        fam_le = struct.unpack_from("<I", data, 0)[0]
        fam_be = struct.unpack_from(">I", data, 0)[0]
        if 2 in (fam_le, fam_be):
            return data[4:]
        return None
    if dlt == DLT_RAW:
        return data
    return None


def _parse_ipv4_udp(ip):
    """ip = payload IPv4. Retorna (src, sport, dst, dport, udp_payload) ou None."""
    if len(ip) < 20:
        return None
    vihl = ip[0]
    if (vihl >> 4) != 4:
        return None
    ihl = (vihl & 0x0F) * 4
    if ihl < 20 or len(ip) < ihl + 8:
        return None
    if ip[9] != 17:               # protocolo != UDP
        return None
    src = ".".join(str(b) for b in ip[12:16])
    dst = ".".join(str(b) for b in ip[16:20])
    sport, dport, ulen = struct.unpack_from(">HHH", ip, ihl)
    payload = ip[ihl + 8: ihl + ulen] if ulen >= 8 else ip[ihl + 8:]
    return src, sport, dst, dport, payload


# ---- pcap classico -------------------------------------------------------------
def _read_pcap(fh, magic):
    if magic == 0xA1B2C3D4:
        endian = "<"
    elif magic == 0xD4C3B2A1:
        endian = ">"
    else:
        raise ValueError("magic pcap inesperado")
    hdr = fh.read(20)
    # global header (apos magic): ver_major,ver_minor,thiszone,sigfigs,snaplen,network
    _vmaj, _vmin, _tz, _sig, _snap, network = struct.unpack(endian + "HHIIII", hdr)
    while True:
        rh = fh.read(16)
        if len(rh) < 16:
            break
        _ts, _tus, caplen, _origlen = struct.unpack(endian + "IIII", rh)
        data = fh.read(caplen)
        if len(data) < caplen:
            break
        yield network, data


# ---- pcapng --------------------------------------------------------------------
def _read_pcapng(fh):
    fh.seek(0)
    idb_dlt = {}     # interface_id -> dlt
    if_count = 0
    while True:
        bh = fh.read(8)
        if len(bh) < 8:
            break
        btype, blen = struct.unpack("<II", bh)
        # endianness: SHB carrega o byte-order magic
        if btype == 0x0A0D0D0A:                       # Section Header Block
            rest = fh.read(blen - 8)
            bom = struct.unpack_from("<I", rest, 0)[0]
            if bom != 0x1A2B3C4D:                     # big-endian section: re-parse
                btype_be, blen_be = struct.unpack(">II", bh)
                # raro em x86; aborta com aviso
                raise ValueError("pcapng big-endian nao suportado")
            if_count = 0
            idb_dlt = {}
            continue
        body = fh.read(blen - 12)
        fh.read(4)                                    # trailing block length
        if btype == 0x00000001:                       # Interface Description Block
            dlt = struct.unpack_from("<H", body, 0)[0]
            idb_dlt[if_count] = dlt
            if_count += 1
        elif btype == 0x00000006:                     # Enhanced Packet Block
            ifid = struct.unpack_from("<I", body, 0)[0]
            caplen = struct.unpack_from("<I", body, 12)[0]
            data = body[20:20 + caplen]
            yield idb_dlt.get(ifid, DLT_EN10MB), data
        elif btype == 0x00000003:                     # Simple Packet Block
            data = body[4:]
            yield idb_dlt.get(0, DLT_EN10MB), data
        # outros blocos ignorados


def iter_packets(path):
    with open(path, "rb") as fh:
        magic = struct.unpack("<I", fh.read(4))[0]
        if magic == 0x0A0D0D0A:
            yield from _read_pcapng(fh)
        else:
            fh.seek(0)
            m = struct.unpack("<I", fh.read(4))[0]
            yield from _read_pcap(fh, m)


MSGTYPE = {
    0x0201: "CONNECT(0x0201)", 0x0202: "CONNECT(0x0202)", 0x0203: "CONNECT(0x0203)",
    0x0304: "PUSH-RELIABLE(0x0304)", 0x0305: "ACK(0x0305)", 0x0319: "ADDR-UPDATE(0x0319)",
    0x030a: "ACTION(0x030a)", 0x030d: "KEEPALIVE(0x030d)", 0x030f: "ACTION(0x030f)",
    0x0311: "ACTION(0x0311)", 0x4000: "INPUT(0x4000)",
}
# 'TAGV' aparece no fio como ASCII (54 41 47 56) OU como o u32 0x56544147 em LE
# (47 41 54 56) — depende de como a engine serializa. Casamos as duas ordens.
TAGV_MARKERS = (b"TAGV", (0x56544147).to_bytes(4, "little"))


def main():
    if len(sys.argv) < 2:
        print(__doc__)
        sys.exit(1)
    path = sys.argv[1]
    hexlen = 48
    maxn = 0
    for i, a in enumerate(sys.argv):
        if a == "--hexlen" and i + 1 < len(sys.argv):
            hexlen = int(sys.argv[i + 1])
        if a == "--max" and i + 1 < len(sys.argv):
            maxn = int(sys.argv[i + 1])

    p2p = 0
    relay = 0
    tagv_hits = 0
    by_type = {}
    ports_seen = {}
    printed = 0

    for dlt, data in iter_packets(path):
        ip = _strip_link(dlt, data)
        if ip is None:
            continue
        r = _parse_ipv4_udp(ip)
        if r is None:
            continue
        src, sport, dst, dport, pl = r
        is_p2p = (2300 <= sport <= 2399) or (2300 <= dport <= 2399)
        is_relay = sport in (40708, 40709) or dport in (40708, 40709)
        if is_p2p:
            p2p += 1
            ports_seen[sport] = ports_seen.get(sport, 0) + 1
            mt = struct.unpack_from("<H", pl, 0)[0] if len(pl) >= 2 else -1
            name = MSGTYPE.get(mt, "0x%04x" % mt if mt >= 0 else "<short>")
            by_type[name] = by_type.get(name, 0) + 1
            if any(m in pl for m in TAGV_MARKERS):
                tagv_hits += 1
                name += " [TAGV]"
            if maxn == 0 or printed < maxn:
                hx = pl[:hexlen].hex()
                print("P2P %5d->%-5d %3dB %-22s %s%s" % (
                    sport, dport, len(pl), name, hx,
                    " ..." if len(pl) > hexlen else ""))
                printed += 1
        elif is_relay:
            relay += 1

    print("\n=== RESUMO ===")
    print("datagramas P2P-direto (2300-2399): %d" % p2p)
    print("datagramas relay (40708/40709):    %d" % relay)
    print("frames com 'TAGV' (handshake):     %d" % tagv_hits)
    if ports_seen:
        print("portas P2P vistas: " + ", ".join(
            "%d(%d)" % (p, n) for p, n in sorted(ports_seen.items())))
    if by_type:
        print("por msgType:")
        for k, v in sorted(by_type.items(), key=lambda kv: -kv[1]):
            print("  %-26s %d" % (k, v))
    if p2p == 0:
        print("\n!! ZERO frames 2300-2399. Provavel: a captura nao pegou o loopback")
        print("   (Npcap sem 'Support loopback traffic') OU os clientes nao chegaram")
        print("   ao stage juntos. Confira a interface de loopback e refaca.")


if __name__ == "__main__":
    main()
