import socket,struct
try:
    from Crypto.Cipher import AES; HAVE=1
except Exception:
    HAVE=0
KEY=bytes([0xE1,0x3A,0x7E,0xF5,0x37,0x2C,0x10,0x4D,0x4E,0xCE,0xB3,0x0C,0x56,0x26,0xA4,0x8E])
s=socket.create_connection(("127.0.0.1",40708),timeout=5); s.settimeout(2)
def f(op,seq,d): c=struct.pack("<HH",op,seq)+d; return struct.pack("<H",len(c)+2)+c
def rd():
    try: return s.recv(4096)
    except: return b""
s.sendall(f(0x0C,0, bytes([4])+b"test\x00"+b"JP\x00"+b"\x00"+struct.pack("<H",0))); print("login(plain)->",rd().hex())
s.sendall(f(0x01,1,b"")); r=rd(); print("enter(cipher)->",r.hex())
if HAVE and len(r)>=4:
    size=struct.unpack_from("<H",r)[0]; ct=r[2:size]
    pt=AES.new(KEY,AES.MODE_ECB).decrypt(ct)
    # cada bloco de 16 -> descarta 4 bytes de IV, mantem 12
    payload=b"".join(pt[i+4:i+16] for i in range(0,len(pt),16))
    sub=struct.unpack_from("<H",payload)[0]
    print(f"  decifrado: subtype={sub} payload[0:6]={payload[:6].hex()}")
else:
    print("  (pycryptodome ausente ? confiando no self-test do server)")
s.close()
