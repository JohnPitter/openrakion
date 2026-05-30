import zlib, struct, sys

def parse(path):
    d=open(path,'rb').read()
    start=struct.unpack_from("<i",d,0)[0]; p=start
    zsize=d[p]; p+=1
    head=zlib.decompress(d[p:p+zsize]); p+=zsize
    infosz=d[p]+(d[p+1]<<8)+(d[p+2]<<16); p+=3
    info=zlib.decompress(d[p:p+infosz])
    sh=head[0:4]; version,count,validation,offset2=struct.unpack_from("<iiii",head,4)
    tail=head[20:]  # "The Xenesis2..." string
    files=[]; ip=0
    for _ in range(count):
        name=info[ip:ip+112]; ip+=112
        foff,comp,uc,cs=struct.unpack_from("<iiii",info,ip); ip+=16
        files.append([name,foff,comp,uc,cs])
    return d, version, count, validation, tail, files

def build(d, version, validation, tail, files):
    # files: list of [name(112 bytes), foff, comp, uc, cs, (optional new_block bytes)]
    out=bytearray(); out+=b'\x00\x00\x00\x00'  # placeholder start offset
    newfiles=[]
    for f in files:
        name,foff,comp,uc,cs = f[0],f[1],f[2],f[3],f[4]
        block = f[5] if len(f)>5 and f[5] is not None else d[foff:foff+cs]
        newfoff=len(out)
        out+=block
        newfiles.append((name,newfoff,comp,uc,len(block)))
    start=len(out)
    # head
    head=bytearray(b'XFS2'); head+=struct.pack('<iiii',version,len(files),validation,start); head+=tail
    head_z=zlib.compress(bytes(head),9)
    assert len(head_z)<256, "head zlib too big"
    # filetable
    ft=bytearray()
    for name,foff,comp,uc,cs in newfiles:
        ft+=name; ft+=struct.pack('<iiii',foff,comp,uc,cs)
    ft_z=zlib.compress(bytes(ft),9)
    struct.pack_into('<i',out,0,start)
    out+=bytes([len(head_z)]); out+=head_z
    out+=bytes([len(ft_z)&0xff,(len(ft_z)>>8)&0xff,(len(ft_z)>>16)&0xff]); out+=ft_z
    return bytes(out)

def verify(orig_path, new_bytes):
    tmp=r"C:\Users\joaop\Desenvolvimento\rakion-work\spike\_verify.xfs"
    open(tmp,"wb").write(new_bytes)
    d0,_,_,_,_,f0=parse(orig_path)
    d1,_,_,_,_,f1=parse(tmp)
    assert len(f0)==len(f1), f"count diff {len(f0)} {len(f1)}"
    bad=0
    for a,b in zip(f0,f1):
        na=a[0].split(b'\x00')[0]; nb=b[0].split(b'\x00')[0]
        if na!=nb: print("NAME DIFF",na,nb); bad+=1; continue
        # decompress both blocks (skip 8-byte hdr) for single-block files
        ra=d0[a[1]:a[1]+a[4]]; rb=d1[b[1]:b[1]+b[4]]
        try: ca=zlib.decompress(ra[8:])
        except: ca=ra
        try: cb=zlib.decompress(rb[8:])
        except: cb=rb
        if ca!=cb: print("DATA DIFF",na.decode('latin-1','replace')); bad+=1
    print(f"verify: {len(f0)} files, {bad} mismatches")
    return bad==0

if __name__=="__main__":
    src=r"C:\Users\joaop\Desenvolvimento\rakion-work\client\DataSetup.xfs"
    mode=sys.argv[1] if len(sys.argv)>1 else "roundtrip"
    d,ver,cnt,val,tail,files=parse(src)
    if mode=="roundtrip":
        nb=build(d,ver,val,tail,files)
        print("rebuilt size",len(nb),"orig",len(d))
        verify(src, nb)
    elif mode=="rename":
        newname=sys.argv[2]  # e.g. "LuxView Brasil"
        cks=int(sys.argv[3],16) if len(sys.argv)>3 else 0  # checksum bytes[6:8]
        outpath=sys.argv[4]
        # find locale.ini, decompress, edit
        for f in files:
            nm=f[0].split(b'\x00')[0].decode('latin-1')
            if nm.lower().endswith('locale.ini'):
                plain=zlib.decompress(d[f[1]+8:f[1]+f[4]])
                txt=plain.decode('latin-1')
                old="WorldServerName = Africa / Arab"
                assert old in txt, "old name not found!"
                txt=txt.replace(old, "WorldServerName = "+newname, 1)
                np=txt.encode('latin-1')
                zc=zlib.compress(np,6); zlen=len(zc)
                block=struct.pack('<H',len(np))+b'\x80'+zlen.to_bytes(3,'little')+struct.pack('<H',cks)+zc
                f[3]=len(np); f[4]=len(block); f.append(block)
                print(f"locale.ini: oldUC={f[2]} newUC={len(np)} newZlen={zlen} newCS={len(block)} cks={cks:04x}")
                break
        nb=build(d,ver,val,tail,files)
        open(outpath,"wb").write(nb)
        print("wrote",outpath,len(nb),"bytes")
        verify(src, nb)  # will show locale.ini as DATA DIFF (expected - name changed)
