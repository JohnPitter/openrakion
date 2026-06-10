import socket, struct
def frame(cd,pl): return struct.pack("<HH",4+len(pl),cd)+pl
def conn():
    s=socket.create_connection(("127.0.0.1",8500),timeout=4); s.settimeout(2)
    s.sendall(frame(0x1000,b"\0\0\0\0")); s.recv(256)   # precredential
    s.sendall(frame(0x1010,b"u\0p\0")); s.recv(256)     # login
    return s
A=conn()
import time; time.sleep(0.3)
B=conn()           # login de B deve disparar NTF_VIP_IPPORT em A e B
time.sleep(0.5)
def drain(s,name):
    s.settimeout(1)
    try:
        while True:
            r=s.recv(256)
            if not r: break
            off=0
            while off+4<=len(r):
                size,cd=struct.unpack_from("<HH",r,off)
                if size<4 or off+size>len(r): break
                print(f"{name} <- CD=0x{cd:04x} ({'NTF_VIP_IPPORT' if cd==0x101f else 'NTF_USER_STATE' if cd==0x3fff else hex(cd)})")
                off+=size
    except: pass
drain(A,"A"); drain(B,"B")
A.close(); B.close()
