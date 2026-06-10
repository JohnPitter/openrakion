"""Conecta no broker (40706), pede a lista (envia [04 00][01 01]) e mostra o
hex da resposta — pra comparar o formato do broker ORIGINAL vs o meu .NET."""
import socket, sys, time
host = "127.0.0.1"; port = int(sys.argv[1]) if len(sys.argv) > 1 else 40706
s = socket.socket(); s.settimeout(4)
try:
    s.connect((host, port))
    # request: [u16 size=4][u16 opcode=0x101]  (igual ao FUN_00481740 do client)
    s.sendall(bytes([0x04,0x00, 0x01,0x01]))
    time.sleep(0.3)
    data = b""
    try:
        while True:
            chunk = s.recv(4096)
            if not chunk: break
            data += chunk
            if len(data) >= 4 and len(data) >= int.from_bytes(data[:2],"little"):
                break
    except socket.timeout:
        pass
    print("RESP %d bytes: %s" % (len(data), data.hex()))
    if len(data) >= 4:
        sz = int.from_bytes(data[:2],"little")
        print("  size=%d  u16@2=0x%04x  payload=%s" % (sz, int.from_bytes(data[2:4],"little"), data[4:].hex()))
        # tenta interpretar como ascii (IP string?)
        print("  payload ascii: %r" % data[4:])
except Exception as e:
    print("ERRO:", e)
finally:
    s.close()
