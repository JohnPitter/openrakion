import sys, zlib, struct

# XFS reader based on AppXFSDev (CarlosX/RakionSource): name field is FIXED 112 bytes
class PR:
    def __init__(self, d): self.d=d; self.p=0
    def i32(self):
        v=struct.unpack_from("<i", self.d, self.p)[0]; self.p+=4; return v
    def byte(self):
        v=self.d[self.p]; self.p+=1; return v
    def bytes(self, n):
        v=self.d[self.p:self.p+n]; self.p+=n; return v
    def strn(self, n):
        v=self.d[self.p:self.p+n].decode('latin-1'); self.p+=n; return v
    def name112(self):
        raw=self.d[self.p:self.p+112]; self.p+=112
        return raw.split(b'\x00')[0].decode('latin-1', 'replace')
    def seek(self, o): self.p=o

def parse(path, verbose=True):
    d=open(path,'rb').read()
    r=PR(d)
    offset=r.i32(); r.seek(offset)
    zsize=r.byte()
    head=PR(zlib.decompress(r.bytes(zsize)))
    str_head=head.strn(4); version=head.i32(); files_count=head.i32(); validation=head.i32(); offset2=head.i32()
    if verbose:
        print(f"head={str_head!r} version={version} files={files_count} validation=0x{validation&0xffffffff:08x} offset2=0x{offset2:x}")
    sz=r.bytes(3); info_size_data=sz[0]+(sz[1]<<8)+(sz[2]<<16)
    info=PR(zlib.decompress(r.bytes(info_size_data)))
    files=[]
    for _ in range(files_count):
        name=info.name112(); foff=info.i32(); comp=info.i32(); uc=info.i32(); cs=info.i32()
        files.append((name,foff,comp,uc,cs))
    for name,foff,comp,uc,cs in files:
        safe=name.encode('ascii','replace').decode('ascii')
        if verbose:
            print(f"  {safe:34} off=0x{foff:x} comp={comp} UCSize={uc} CSize={cs}")
    return d, files

def decode_block(raw, compressed, uncompressed_size, name="<entry>"):
    if not compressed:
        return raw[-uncompressed_size:]
    data=bytearray(); position=0
    while position < len(raw) and len(data) < uncompressed_size:
        standard=position+8 <= len(raw) and raw[position+2] == 0x80
        if standard:
            compressed_size=int.from_bytes(raw[position+3:position+6], "little")
            payload_start=position+8
        else:
            compressed_size=int.from_bytes(raw[position:position+3], "little")
            payload_start=position+5
        payload=raw[payload_start:payload_start+compressed_size]
        try:
            chunk=zlib.decompress(payload)
        except zlib.error:
            chunk=zlib.decompressobj().decompress(payload)
        if not chunk or compressed_size <= 0:
            break
        data.extend(chunk)
        position=payload_start+compressed_size
    if len(data) != uncompressed_size:
        raise ValueError(
            f"invalid uncompressed size for {name}: {len(data)} != {uncompressed_size}")
    return bytes(data)

def extract(path, want, dump=True):
    d, files = parse(path)
    for name,foff,comp,uc,cs in files:
        if want.lower() in name.lower():
            raw=d[foff:foff+cs]
            data=decode_block(raw, comp, uc, name)
            safe=name.encode('ascii','replace').decode('ascii')
            print(f"\n===== {safe} ({len(data)} bytes) =====")
            sys.stdout.flush()
            sys.stdout.buffer.write(data[:4000]); sys.stdout.buffer.flush(); print()
            return data

if __name__=="__main__":
    p=sys.argv[1]
    if len(sys.argv)>2: extract(p, sys.argv[2])
    else: parse(p)
