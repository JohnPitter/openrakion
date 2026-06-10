import socket,struct,time
s=socket.create_connection(("127.0.0.1",40708),timeout=5); s.settimeout(2)
def f(op,seq,d): c=struct.pack("<HH",op,seq)+d; return struct.pack("<H",len(c)+2)+c
def rd(t):
    try:
        s.settimeout(t); r=s.recv(4096); return r.hex()
    except: return "(nada)"
# login (0x0C, seq 0): connType=4, user test, field2 "JP", field3 "", tail 0
s.sendall(f(0x0C,0, bytes([4])+b"test\x00"+b"JP\x00"+b"\x00"+struct.pack("<H",0)))
print("login->",rd(2))
# EnterChannel (0x01, seq 1) ? sem payload
s.sendall(f(0x01,1,b"")); print("enter->",rd(2))
# RequestWorldInfo (0x02, seq 2)
s.sendall(f(0x02,2,b"")); print("worldinfo->",rd(2))
s.close()
