"""DIAGNOSTICO: em vez de criar um botao, o hook em FUN_0044f080 (0x44fbad) RENOMEIA o primeiro botao
da tela (tela+0xa4c) p/ "HOOK!!". Se um botao da sala virar "HOOK!!", o hook roda e FUN_0044f080 e' a
tela certa -> o bug esta na CRIACAO do botao. Se nada mudar -> tela/hook errados.
Gera rakion.exe.botbtn / rakion.bin.botbtn (le do .orig limpo). Aplique com swap_botbtn.ps1 apply.
"""
import struct, os
BIN_DIR=r"C:\Users\joaop\Desenvolvimento\Rakion\rakion-final\Bin"
TARGETS=["rakion.exe","rakion.bin"]
IMAGE_BASE=0x400000; CAVE_VA=0x515207; HOOK_VA=0x44fbad; HOOK_LEN=7; RET_VA=0x44fbb4

def va_to_off(data,va):
    e=struct.unpack_from("<I",data,0x3c)[0]; ns=struct.unpack_from("<H",data,e+6)[0]; os_=struct.unpack_from("<H",data,e+20)[0]
    so=e+24+os_; rva=va-IMAGE_BASE
    for i in range(ns):
        o=so+i*40; vs=struct.unpack_from("<I",data,o+8)[0]; va_=struct.unpack_from("<I",data,o+12)[0]
        rs=struct.unpack_from("<I",data,o+16)[0]; rp=struct.unpack_from("<I",data,o+20)[0]
        if va_<=rva<va_+max(vs,rs): return rp+(rva-va_)
    raise ValueError("VA %#x"%va)

code=bytearray()
def emit(*bs):
    for b in bs: code.append(b&0xff)
emit(0x60)                                   # pushad
emit(0x8b,0x8e,0x4c,0x0a,0x00,0x00)          # mov ecx,[esi+0xa4c]  (1o botao)
emit(0x85,0xc9)                              # test ecx,ecx
emit(0x74,0x0a)                              # jz done (+10)
emit(0x8b,0x01)                              # mov eax,[ecx] (vtable)
emit(0x68); strfix=len(code); emit(0,0,0,0)  # push STR
emit(0xff,0x50,0x34)                         # call [eax+0x34] SetText
# done:
emit(0x61)                                   # popad
emit(0x8b,0x8c,0x24,0x30,0x02,0x00,0x00)     # mov ecx,[esp+0x230]
emit(0xe9); jmpfix=len(code); emit(0,0,0,0)  # jmp RET_VA
str_pos=len(code); code.extend(b"HOOK!!\x00")
struct.pack_into("<I",code,strfix,CAVE_VA+str_pos)
struct.pack_into("<i",code,jmpfix,RET_VA-(CAVE_VA+jmpfix+4))

jrel=CAVE_VA-(HOOK_VA+5)
hook=b"\xe9"+struct.pack("<i",jrel)+b"\x90"*(HOOK_LEN-5)
for name in TARGETS:
    path=os.path.join(BIN_DIR,name); src=path+".orig" if os.path.exists(path+".orig") else path
    if not os.path.exists(src): print("[skip]",name); continue
    data=bytearray(open(src,"rb").read())
    co=va_to_off(data,CAVE_VA); ho=va_to_off(data,HOOK_VA)
    data[co:co+len(code)]=code; data[ho:ho+HOOK_LEN]=hook
    open(path+".botbtn","wb").write(data)
    print("OK %-11s -> %s.botbtn (DIAG rename, %dB)"%(name,name,len(code)))
print("Aplique: swap_botbtn.ps1 apply ; entre numa sala ; veja se ALGUM botao virou 'HOOK!!'")
