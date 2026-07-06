"""Patch do botao "Add Bot" na tela do game room (rakion.bin == rakion.exe). Gera COPIAS .botbtn
(o original NAO e' tocado); aplica com tools/swap_botbtn.ps1. RE estatica (Ghidra/objdump).

Arquitetura (3 partes; ver docs/RE em rakion-work/ghidra-proj/room_click_fix6.out.txt):

ETAPA 1 — o botao APARECE: hook no build da tela da SALA (0x447329, "MOV ECX,[ESP+0xac]") salta p/
  um CODE CAVE (0x515207) que cria 1 csButton reusando o padrao exato do exe (ESI=tela, command id
  0x200), depois re-executa a instrucao e volta. alloc FUN_004bf8c2, criar FUN_00437680, SetBitmap
  [0x4d1074], SetPos [0x4d10b8], SetSize [0x4d10bc], SetText = vtable+0x34.

ETAPA 3 — o botao FICA NO LUGAR CERTO: apos o SetSize (0x51528d) desvia p/ um bloco (0x5152ec) que
  refaz SetSize e RE-CHAMA SetPos RELATIVO a' origem da tela bx=[esi+0x270]/by=[esi+0x274] com
  (bx+0x15c, by+0x233) — centrado no GAP entre Invite (fim bx+340) e Game setting (ini bx+458),
  mesma linha Y dos nativos. SetZorder mantido (inofensivo).

ETAPA 2 (CLIQUE) — REESCRITA. As 2 causas-raiz cravadas (in-game + disasm):
  BUG A: o mouse-DOWN nao chega ao csButton custom (entrega/hit-test; layout muda fora de 800x600).
    FIX robusto: NAO depender do csButton. A TELA (FUN_00447af0) SEMPRE recebe o mouse no inicio do
    seu HandleEvent (antes de rotear aos filhos). Hook em 0x447bf5 (logo apos EBP=evento, antes do
    demux que so trata type 1/0xd): em type==4 (ButtonDown) + botao esquerdo ([ev+0x14]==1) + dentro
    do rect do botao, dispara a acao. Imune a captura/resolucao (quem testa e' a propria tela).
  BUG B: SendChatDataInGame (modo 0x1d) CONGELA quando chamado da SALA (modo 0x1c). A rota CERTA da
    sala (a que o /addbot digitado usa) e' FUN_0041a760 sobre o widget de chat [tela+0x9ec], passando
    "/addbot" como CTString por valor. FUN_0041a760 ja' revalida modo==0x1c internamente.
  => Hook do clique vira o MOUSE-HOOK em 0x447bf5 -> cave 0x515380. O hook de COMANDO antigo
     (0x447c14 -> SendChatDataInGame) e' REMOVIDO: 0x447c14 volta ao original, o cave 0x5152b0 some.

Enderecos: rakion.bin, ImageBase 0x400000, sem ASLR (VA == offset via PE). Uso: python tools/patch_botbtn.py
"""
import struct, os

BIN_DIR = r"C:\Users\joaop\Desenvolvimento\Rakion\rakion-final\Bin"
TARGETS = ["rakion.exe", "rakion.bin"]
IMAGE_BASE = 0x400000
CAVE_VA = 0x515207          # code cave da Etapa 1 (cria o botao)
HOOK_VA = 0x447329          # FUN_00446ff0 (tela da SALA): "MOV ECX,[ESP+0xac]" (7B) -> JMP cave
HOOK_LEN = 7
RET_VA = 0x447330           # proxima instrucao apos a overwritten
NEW_ID = 0x200              # command id do botao (so visual; o clique e' tratado pela tela, nao por 0x200)
BTN_X, BTN_Y = 625, 648     # SetPos absoluto da Etapa-1 (morto; sobrescrito pelo SetPos relativo da Etapa-3)
ALLOC = 0x4bf8c2
CREATE = 0x437680


def va_to_off(data, va):
    """Mapeia VA->offset de arquivo via cabecalhos PE."""
    e_lfanew = struct.unpack_from("<I", data, 0x3c)[0]
    assert data[e_lfanew:e_lfanew+4] == b"PE\0\0"
    num_sec = struct.unpack_from("<H", data, e_lfanew+6)[0]
    opt_sz = struct.unpack_from("<H", data, e_lfanew+20)[0]
    sec_off = e_lfanew + 24 + opt_sz
    rva = va - IMAGE_BASE
    for i in range(num_sec):
        o = sec_off + i*40
        vsize = struct.unpack_from("<I", data, o+8)[0]
        vaddr = struct.unpack_from("<I", data, o+12)[0]
        rawsz = struct.unpack_from("<I", data, o+16)[0]
        rawptr = struct.unpack_from("<I", data, o+20)[0]
        if vaddr <= rva < vaddr + max(vsize, rawsz):
            return rawptr + (rva - vaddr)
    raise ValueError("VA %#x fora das secoes" % va)


# ============================ ETAPA 1: cria o botao ============================
code = bytearray()
fixups = []   # (pos_no_code, alvo_va) para rel32 (call/jmp) a partir do fim do operando


def emit(*bs):
    for b in bs: code.append(b & 0xff)
def imm32(v): code.extend(struct.pack("<i", v))
def call(target_va):
    emit(0xe8); fixups.append((len(code), target_va)); imm32(0)
def jmp(target_va):
    emit(0xe9); fixups.append((len(code), target_va)); imm32(0)


emit(0x60)                                   # pushad
emit(0x68); imm32(0x1b4)                     # push 0x1b4
call(ALLOC)                                  # call FUN_004bf8c2 -> eax=buf
emit(0x83, 0xc4, 0x04)                       # add esp,4
emit(0x85, 0xc0)                             # test eax,eax
emit(0x0f, 0x84); jz1 = len(code); imm32(0)  # jz done
emit(0x6a, 0x00)                             # push 0
emit(0x68); imm32(0x1400)                    # push 0x1400 (bit 0x1000: release chama Press direto)
emit(0x6a, 0x00)                             # push 0
emit(0x6a, 0xff)                             # push -1
emit(0x6a, 0xff)                             # push -1
emit(0x68); imm32(NEW_ID)                    # push NEW_ID
emit(0x56)                                   # push esi (tela)
emit(0x8b, 0xc8)                             # mov ecx,eax (this=buf)
call(CREATE)                                 # call FUN_00437680 -> eax=btn
emit(0x85, 0xc0)                             # test eax,eax
emit(0x0f, 0x84); jz2 = len(code); imm32(0)  # jz done
emit(0x8b, 0xf8)                             # mov edi,eax (btn)
# SetBitmap(btn, esi+0x184, esi+0x1b4, 0)
emit(0x6a, 0x00)                             # push 0
emit(0x8d, 0x9e, 0xb4, 0x01, 0x00, 0x00)     # lea ebx,[esi+0x1b4]
emit(0x53)                                   # push ebx
emit(0x8d, 0xae, 0x84, 0x01, 0x00, 0x00)     # lea ebp,[esi+0x184]
emit(0x55)                                   # push ebp
emit(0x8b, 0xcf)                             # mov ecx,edi
emit(0xff, 0x15, 0x74, 0x10, 0x4d, 0x00)     # call [0x4d1074] SetBitmap
# alvo do clique: btn[0x18c]=esi+0x19c; btn[0x190]=0
emit(0x8d, 0x86, 0x9c, 0x01, 0x00, 0x00)     # lea eax,[esi+0x19c]
emit(0x89, 0x87, 0x8c, 0x01, 0x00, 0x00)     # mov [edi+0x18c],eax
emit(0xc7, 0x87, 0x90, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00)  # mov [edi+0x190],0
# SetText(btn, "Add Bot")
emit(0x68); strfix = len(code); imm32(0)     # push <STR_VA> (patch depois)
emit(0x8b, 0xcf)                             # mov ecx,edi
emit(0x8b, 0x07)                             # mov eax,[edi] (vtable)
emit(0xff, 0x50, 0x34)                       # call [eax+0x34] SetText
# SetPos(btn, X, Y) — absoluto (morto; refeito relativo na Etapa 3)
emit(0x68); imm32(BTN_Y)                     # push Y
emit(0x68); imm32(BTN_X)                     # push X
emit(0x8b, 0xcf)                             # mov ecx,edi
emit(0xff, 0x15, 0xb8, 0x10, 0x4d, 0x00)     # call [0x4d10b8] SetPos
# SetSize(btn, 0x66, 0x1d)
emit(0x6a, 0x1d)                             # push 0x1d
emit(0x6a, 0x66)                             # push 0x66
emit(0x8b, 0xcf)                             # mov ecx,edi
emit(0xff, 0x15, 0xbc, 0x10, 0x4d, 0x00)     # call [0x4d10bc] SetSize
# done:
done_pos = len(code)
emit(0x61)                                   # popad
emit(0x8b, 0x8c, 0x24, 0xac, 0x00, 0x00, 0x00)  # MOV ECX,[ESP+0xac] (overwritten de FUN_00446ff0)
jmp(RET_VA)                                  # jmp RET_VA
# string "Add Bot\0"
str_pos = len(code)
code.extend(b"Add Bot\x00")


def patch_rel32_at(pos, target_pos):
    struct.pack_into("<i", code, pos, target_pos - (pos + 4))
patch_rel32_at(jz1, done_pos)
patch_rel32_at(jz2, done_pos)
struct.pack_into("<I", code, strfix, CAVE_VA + str_pos)   # push absoluto da string
for pos, target_va in fixups:
    struct.pack_into("<i", code, pos, target_va - (CAVE_VA + pos + 4))

# hook Etapa 1: JMP cave (5) + NOP*(HOOK_LEN-5)
hook = b"\xe9" + struct.pack("<i", CAVE_VA - (HOOK_VA + 5)) + b"\x90" * (HOOK_LEN - 5)

# ===================== ETAPA 3: posiciona RELATIVO (gap) =====================
ZFIX_EDIT_VA = 0x51528d
ZFIX_EDIT = bytes.fromhex("e95a00000090909090909090")   # CALL SetSize (12B) -> JMP 0x5152ec + 7 NOP
ZFIX_BLOCK_VA = 0x5152ec
_zfix_body = bytes.fromhex(
    "6a1d6a668bcfff15bc104d00"                       # SetSize refeito
    "8b8674020000" "0533020000" "50"                 # MOV EAX,[ESI+0x274](by); ADD EAX,0x233; PUSH (Y=by+563)
    "8b8670020000" "055c010000" "50"                 # MOV EAX,[ESI+0x270](bx); ADD EAX,0x15c; PUSH (X=bx+348, gap)
    "8bcf" "ff15b8104d00"                            # MOV ECX,EDI; CALL [0x4d10b8] SetPos (relativo)
    "8b863c010000" "50" "57" "8bce" "ff1588104d00"  # SetZorder(ref=[ESI+0x13c], btn)
    "61" "8b8c24ac000000")                            # popad; MOV ECX,[ESP+0xac]
ZFIX_BLOCK = _zfix_body + b"\xe9" + struct.pack("<i", 0x447330 - (ZFIX_BLOCK_VA + len(_zfix_body) + 5))

# ============== ETAPA 2 (clique) — MOUSE-HOOK na tela (BUG A + B) ==============
# Hook em FUN_00447af0 @0x447bf5 (MOV ECX,[0x4feed0], 6B, sem rel interno; EBP=evento ja' setado em
# 0x447b88; ESI=tela vivo desde 0x447b30; ponto ANTES do demux que so trata type 1/0xd). Cave @0x515380.
# Em type==4 (ButtonDown) + esquerdo ([ev+0x14]==1) + dentro do rect do botao (X[bx+0x15c,bx+0x1c2),
# Y[by+0x233,by+0x250)), envia "/addbot" pela rota da SALA: CTString("/addbot") por valor +
# FUN_0041a760(this=[tela+0x9ec]=widget de chat). FUN_0041a760 revalida modo==0x1c sozinha.
MOUSE_HOOK_VA = 0x447bf5
MOUSE_HOOK_RET = 0x447bfb
MOUSE_HOOK_LEN = 6
MOUSE_CAVE_VA = 0x515380          # livre (cave da Etapa 1/3 termina em 0x515334)

mcode = bytearray(); mfix = []


def m_emit(*bs):
    for b in bs: mcode.append(b & 0xff)
def m_imm32(v): mcode.extend(struct.pack("<i", v))
def m_jmp(t): m_emit(0xe9); mfix.append((len(mcode), t)); m_imm32(0)
def m_call(t): m_emit(0xe8); mfix.append((len(mcode), t)); m_imm32(0)


m_emit(0x60)                                  # pushad
m_emit(0x80, 0x7d, 0x04, 0x04)                # cmp byte [ebp+0x4],4    (ButtonDown)
m_emit(0x0f, 0x85); mj1 = len(mcode); m_imm32(0)   # jne done
m_emit(0x83, 0x7d, 0x14, 0x01)                # cmp dword [ebp+0x14],1  (botao esquerdo)
m_emit(0x0f, 0x85); mj2 = len(mcode); m_imm32(0)   # jne done
# X em [bx+0x15c .. bx+0x1c2) : ebx=X=[ebp+0xc]; ecx=bx=[esi+0x270]
m_emit(0x8b, 0x5d, 0x0c)                      # mov ebx,[ebp+0xc]
m_emit(0x8b, 0x8e, 0x70, 0x02, 0x00, 0x00)    # mov ecx,[esi+0x270]
m_emit(0x8d, 0x81, 0x5c, 0x01, 0x00, 0x00)    # lea eax,[ecx+0x15c]
m_emit(0x3b, 0xd8)                            # cmp ebx,eax
m_emit(0x0f, 0x8c); mj3 = len(mcode); m_imm32(0)   # jl done
m_emit(0x8d, 0x81, 0xc2, 0x01, 0x00, 0x00)    # lea eax,[ecx+0x1c2]  (= +0x15c +0x66)
m_emit(0x3b, 0xd8)                            # cmp ebx,eax
m_emit(0x0f, 0x8d); mj4 = len(mcode); m_imm32(0)   # jge done
# Y em [by+0x233 .. by+0x250) : edx=Y=[ebp+0x10]; ecx=by=[esi+0x274]
m_emit(0x8b, 0x55, 0x10)                      # mov edx,[ebp+0x10]
m_emit(0x8b, 0x8e, 0x74, 0x02, 0x00, 0x00)    # mov ecx,[esi+0x274]
m_emit(0x8d, 0x81, 0x33, 0x02, 0x00, 0x00)    # lea eax,[ecx+0x233]
m_emit(0x3b, 0xd0)                            # cmp edx,eax
m_emit(0x0f, 0x8c); mj5 = len(mcode); m_imm32(0)   # jl done
m_emit(0x8d, 0x81, 0x50, 0x02, 0x00, 0x00)    # lea eax,[ecx+0x250]  (= +0x233 +0x1d)
m_emit(0x3b, 0xd0)                            # cmp edx,eax
m_emit(0x0f, 0x8d); mj6 = len(mcode); m_imm32(0)   # jge done
# --- dentro do rect: envia "/addbot" pela rota da sala ---
m_emit(0x83, 0xec, 0x04)                      # sub esp,4              (slot do CTString = arg1 por valor)
m_emit(0x68); mstr = len(mcode); m_imm32(0)   # push <"/addbot" VA>    (char*)
m_emit(0x8d, 0x4c, 0x24, 0x04)                # lea ecx,[esp+4]        (this=&slot)
m_emit(0xff, 0x15, 0x08, 0x05, 0x4d, 0x00)    # call [0x4d0508]        CTString::CTString(char*); ret 4
m_emit(0x8b, 0x8e, 0xec, 0x09, 0x00, 0x00)    # mov ecx,[esi+0x9ec]    (this = widget de chat da sala)
m_call(0x41a760)                              # call FUN_0041a760      ; ret 4 (limpa arg + ~CTString)
# done:
mdone = len(mcode)
m_emit(0x61)                                  # popad
m_emit(0x8b, 0x0d, 0xd0, 0xee, 0x4f, 0x00)    # mov ecx,[0x4feed0]     (instrucao deslocada)
m_jmp(MOUSE_HOOK_RET)                          # jmp 0x447bfb
mstr_pos = len(mcode)
mcode.extend(b"/addbot\x00")

for p in (mj1, mj2, mj3, mj4, mj5, mj6):
    struct.pack_into("<i", mcode, p, mdone - (p + 4))
struct.pack_into("<I", mcode, mstr, MOUSE_CAVE_VA + mstr_pos)   # push absoluto da string
for pos, target_va in mfix:
    struct.pack_into("<i", mcode, pos, target_va - (MOUSE_CAVE_VA + pos + 4))

mouse_hook = b"\xe9" + struct.pack("<i", MOUSE_CAVE_VA - (MOUSE_HOOK_VA + 5)) + b"\x90" * (MOUSE_HOOK_LEN - 5)

# Restaurar o hook de COMANDO antigo (0x447c14): bytes originais MOV EAX,[EBP+0xc] + ADD EAX,0xffffff00
CLICK_HOOK_VA = 0x447c14
CLICK_HOOK_ORIG = bytes.fromhex("8b450c0500ffffff")   # 8 bytes (era JMP 0x5152b0 + 3 NOP)

# ============================ aplica nos arquivos ============================
for name in TARGETS:
    path = os.path.join(BIN_DIR, name)
    src = path + ".orig" if os.path.exists(path + ".orig") else path
    if not os.path.exists(src):
        print("[skip] %s nao existe" % name); continue
    data = bytearray(open(src, "rb").read())
    cave_off = va_to_off(data, CAVE_VA)
    hook_off = va_to_off(data, HOOK_VA)
    zfix_edit_off = va_to_off(data, ZFIX_EDIT_VA)
    zfix_block_off = va_to_off(data, ZFIX_BLOCK_VA)
    mouse_cave_off = va_to_off(data, MOUSE_CAVE_VA)
    mouse_hook_off = va_to_off(data, MOUSE_HOOK_VA)
    click_hook_off = va_to_off(data, CLICK_HOOK_VA)
    if any(data[cave_off+i] for i in range(len(code))) \
       or any(data[zfix_block_off+i] for i in range(len(ZFIX_BLOCK))) \
       or any(data[mouse_cave_off+i] for i in range(len(mcode))):
        print("[!] %s: code-cave NAO esta livre — pulando p/ nao corromper" % name); continue
    data[cave_off:cave_off+len(code)] = code                          # Etapa 1: cria o botao
    data[hook_off:hook_off+HOOK_LEN] = hook
    data[zfix_edit_off:zfix_edit_off+len(ZFIX_EDIT)] = ZFIX_EDIT      # Etapa 3: posiciona no gap
    data[zfix_block_off:zfix_block_off+len(ZFIX_BLOCK)] = ZFIX_BLOCK
    data[mouse_cave_off:mouse_cave_off+len(mcode)] = mcode            # Etapa 2: clique pela tela -> /addbot
    data[mouse_hook_off:mouse_hook_off+MOUSE_HOOK_LEN] = mouse_hook
    data[click_hook_off:click_hook_off+len(CLICK_HOOK_ORIG)] = CLICK_HOOK_ORIG  # remove o hook de comando antigo
    open(path + ".botbtn", "wb").write(data)
    print("OK %-11s -> %s.botbtn  (botao %dB @%#x + zorder %dB @%#x + clique %dB @%#x)"
          % (name, name, len(code), CAVE_VA, len(ZFIX_BLOCK), ZFIX_BLOCK_VA, len(mcode), MOUSE_CAVE_VA))
print("Original intacto. Aplique com: tools\\swap_botbtn.ps1 apply")
