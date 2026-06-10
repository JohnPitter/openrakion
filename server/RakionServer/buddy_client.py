import socket, struct, time
s = socket.create_connection(("127.0.0.1",8500), timeout=5)
def frame(cd,payload):
    size=4+len(payload)
    return struct.pack("<HH",size,cd)+payload
def readf(s):
    s.settimeout(3); r=s.recv(4096)
    size,cd=struct.unpack_from("<HH",r); return size,cd,r[4:size]
s.sendall(frame(0x1000, b"\x00\x00\x00\x00"))   # SVC_PRECREDENTIAL
size,cd,p=readf(s); print(f"RET1 size={size} CD=0x{cd:04x} payload={p.hex()}")
s.sendall(frame(0x1010, b"test\x00pw\x00"))      # SVC_LOGIN
size,cd,p=readf(s); print(f"RET2 size={size} CD=0x{cd:04x} payload={p.hex()} result={struct.unpack_from('<H',p)[0] if len(p)>=2 else None}")
s.close()
