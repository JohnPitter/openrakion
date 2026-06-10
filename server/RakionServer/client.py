import socket, struct, time
s = socket.create_connection(("127.0.0.1",40708), timeout=5)
def frame(op,seq,data):
    c = struct.pack("<HH",op,seq)+data
    return struct.pack("<H",len(c)+2)+c
payload = bytes([4]) + b"test\x00" + b"pw\x00" + b"\x00" + struct.pack("<H",0)
s.sendall(frame(0x0C,0,payload))
s.settimeout(3)
try:
    r=s.recv(4096); print("RESP_HEX", r.hex())
    if len(r)>=6:
        size,a,b=struct.unpack_from("<HHH",r); print(f"size={size} serverSeq={a} msgType={b} data={r[6:size].hex()}")
except Exception as e: print("no-resp", e)
s.close()
