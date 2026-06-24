"""Patch do botao "Add Bot" na tela do game room (rakion.bin == rakion.exe). Gera COPIAS .botbtn
(o original NAO e' tocado); aplica com tools/swap_botbtn.ps1. RE estatica (Ghidra), 2 etapas:

ETAPA 1 — o botao APARECE: hook no build da tela da SALA FUN_00446ff0 (0x447329, "MOV ECX,[ESP+0xac]")
salta p/ um CODE CAVE (0x515207) que cria 1 csButton reusando o padrao exato do exe (ESI=tela,
command id 0x200), depois re-executa a instrucao e volta. alloc FUN_004bf8c2, criar FUN_00437680,
SetBitmap [0x4d1074], SetPos [0x4d10b8], SetSize [0x4d10bc], SetText = vtable+0x34.

ETAPA 2 — o CLIQUE funciona: hook no HandleEvent da sala FUN_00447af0 (0x447c14) -> cave (0x5152b0)
que, se o command id == 0x200, chama SendChatDataInGame("/addbot") (o servidor adiciona o bot).

Enderecos: rakion.bin, ImageBase 0x400000, sem ASLR (VA == offset via PE). Uso: python tools/patch_botbtn.py
"""
import struct, shutil, os

# O launcher roda rakion.exe; rakion.bin tem conteudo IDENTICO (mesmos offsets). Patcha os DOIS.
BIN_DIR = r"C:\Users\joaop\Desenvolvimento\Rakion\rakion-final\Bin"
TARGETS = ["rakion.exe", "rakion.bin"]
IMAGE_BASE = 0x400000
CAVE_VA = 0x515207          # code cave (3003 bytes livres)
HOOK_VA = 0x447329          # FUN_00446ff0 (tela da SALA, RE confirmada): "MOV ECX,[ESP+0xac]" (7B) -> JMP cave
HOOK_LEN = 7
RET_VA = 0x447330           # proxima instrucao apos a overwritten
NEW_ID = 0x200              # command id do botao -> Etapa 2: case 0x200 em FUN_00447af0 -> SendChatDataInGame("/addbot")
BTN_X, BTN_Y = 625, 648     # centralizado no vão entre Invite e Game setting (nudge -30 p/ não tampar Game setting)
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

# ---- monta o code cave (mini-assembler: emite bytes, resolve rel32 no fim) ----
code = bytearray()
fixups = []   # (pos_no_code, tipo, alvo_va)  tipo: 'call'/'jmp' (rel32 a partir do fim do operando)

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
emit(0x83,0xc4,0x04)                         # add esp,4
emit(0x85,0xc0)                              # test eax,eax
emit(0x0f,0x84); jz1=len(code); imm32(0)     # jz done (near, patch depois)
emit(0x6a,0x00)                              # push 0
emit(0x68); imm32(0x1400)                    # push 0x1400 (bit 0x1000: release chama Press DIRETO, sem depender da captura do engine)
emit(0x6a,0x00)                              # push 0
emit(0x6a,0xff)                              # push -1
emit(0x6a,0xff)                              # push -1
emit(0x68); imm32(NEW_ID)                    # push NEW_ID
emit(0x56)                                   # push esi (tela)
emit(0x8b,0xc8)                              # mov ecx,eax (this=buf)
call(CREATE)                                 # call FUN_00437680 -> eax=btn
emit(0x85,0xc0)                              # test eax,eax
emit(0x0f,0x84); jz2=len(code); imm32(0)     # jz done
emit(0x8b,0xf8)                              # mov edi,eax (btn)
# SetBitmap(btn, esi+0x184, esi+0x1b4, 0)
emit(0x6a,0x00)                              # push 0
emit(0x8d,0x9e,0xb4,0x01,0x00,0x00)          # lea ebx,[esi+0x1b4]
emit(0x53)                                   # push ebx
emit(0x8d,0xae,0x84,0x01,0x00,0x00)          # lea ebp,[esi+0x184]
emit(0x55)                                   # push ebp
emit(0x8b,0xcf)                              # mov ecx,edi
emit(0xff,0x15,0x74,0x10,0x4d,0x00)          # call [0x4d1074] SetBitmap
# alvo do clique: btn[0x18c]=esi+0x19c; btn[0x190]=0
emit(0x8d,0x86,0x9c,0x01,0x00,0x00)          # lea eax,[esi+0x19c]
emit(0x89,0x87,0x8c,0x01,0x00,0x00)          # mov [edi+0x18c],eax
emit(0xc7,0x87,0x90,0x01,0x00,0x00,0x00,0x00,0x00,0x00)  # mov [edi+0x190],0
# SetText(btn, "Add Bot")
emit(0x68); strfix=len(code); imm32(0)       # push <STR_VA> (patch depois)
emit(0x8b,0xcf)                              # mov ecx,edi
emit(0x8b,0x07)                              # mov eax,[edi] (vtable)
emit(0xff,0x50,0x34)                         # call [eax+0x34] SetText
# SetPos(btn, X, Y)
emit(0x68); imm32(BTN_Y)                     # push Y
emit(0x68); imm32(BTN_X)                     # push X
emit(0x8b,0xcf)                              # mov ecx,edi
emit(0xff,0x15,0xb8,0x10,0x4d,0x00)          # call [0x4d10b8] SetPos
# SetSize(btn, 0x66, 0x1d)
emit(0x6a,0x1d)                              # push 0x1d
emit(0x6a,0x66)                              # push 0x66
emit(0x8b,0xcf)                              # mov ecx,edi
emit(0xff,0x15,0xbc,0x10,0x4d,0x00)          # call [0x4d10bc] SetSize
# done:
done_pos = len(code)
emit(0x61)                                   # popad
emit(0x8b,0x8c,0x24,0xac,0x00,0x00,0x00)     # MOV ECX,[ESP+0xac] (overwritten de FUN_00446ff0)
jmp(RET_VA)                                  # jmp 0x44fbb4
# string "Add Bot\0"
str_pos = len(code)
code.extend(b"Add Bot\x00")

# resolve os jz near (done) e o push da string
def patch_rel32_at(pos, target_pos):
    rel = target_pos - (pos + 4)
    struct.pack_into("<i", code, pos, rel)
patch_rel32_at(jz1, done_pos)
patch_rel32_at(jz2, done_pos)
struct.pack_into("<I", code, strfix, CAVE_VA + str_pos)   # push absoluto da string
# resolve call/jmp (rel a partir do VA do fim do operando)
for pos, target_va in fixups:
    instr_end_va = CAVE_VA + pos + 4
    struct.pack_into("<i", code, pos, target_va - instr_end_va)

# hook: JMP cave (5) + NOP*(HOOK_LEN-5)
jrel = CAVE_VA - (HOOK_VA + 5)
hook = b"\xe9" + struct.pack("<i", jrel) + b"\x90" * (HOOK_LEN - 5)

# ===== ETAPA 2: handler do CLIQUE (cmd 0x200 -> SendChatDataInGame("/addbot")) =====
# Receita RE verificada byte-a-byte contra o rakion.bin (agente). Hook em FUN_00447af0 @0x447c14
# (HandleEvent da sala; le cmd_id em [EBP+0xc]); 0x200 nao colide com case existente. Cave @0x5152b0
# (logo apos a da Etapa 1): cmp cmd==0x200 -> CNet=*(*0x4d0650)+0x119c; push 0; push "/addbot";
# call [0x4d0434] (thunk IAT de SendChatDataInGame); volta ao codigo deslocado e jmp 0x447c1c.
CLICK_CAVE_VA = 0x5152b0
CLICK_HOOK_VA = 0x447c14
CLICK_HOOK_LEN = 8                  # MOV EAX,[EBP+0xc] (3B) + ADD EAX,0xffffff00 (5B) deslocados
click_cave = bytes.fromhex(
    "8b450c3d00020000751d8b0d50064d00"
    "8b0981c19c1100006a0068e4525100ff"
    "1534044d00eb008b450c0500ffffffe9"
    "3829f3ff2f616464626f7400")
cjrel = CLICK_CAVE_VA - (CLICK_HOOK_VA + 5)
click_hook = b"\xe9" + struct.pack("<i", cjrel) + b"\x90" * (CLICK_HOOK_LEN - 5)

# ===== ETAPA 3: Z-ORDER — traz o botao pra FRENTE pra ele RECEBER o mouse-DOWN =====
# Receita RE verificada (agente): o botao custom nao esta em "slot conhecido" que a tela re-ordena,
# entao fica no FIM da lista de filhos e ATRAS do grid nativo na varredura de hit-test -> nunca recebe
# o down -> Press nunca roda -> /addbot nunca sai (por isso a flag 0x1400 sozinha nao bastou). Fix:
# desvia o caminho de SUCESSO da Etapa-1 (apos o SetSize @0x51528d) pra um bloco que refaz o SetSize +
# csComponent::SetZorder(tela; botao, ref=head[esi+0x13c]) -> botao vira 1o na varredura. So o sucesso
# e' afetado (os JE de falha seguem caindo no epilogo original 0x515299).
ZFIX_EDIT_VA = 0x51528d
ZFIX_EDIT = bytes.fromhex("e95a00000090909090909090")   # CALL SetSize (12B) -> JMP 0x5152ec + 7 NOP
ZFIX_BLOCK_VA = 0x5152ec                                # zeros livres apos a string da Etapa 2
ZFIX_BLOCK = bytes.fromhex(
    "6a1d6a668bcfff15bc104d00"                      # SetSize refeito (push 0x1d; push 0x66; mov ecx,edi; call [0x4d10bc])
    "8b863c010000" "50" "57" "8bce" "ff1588104d00" # MOV EAX,[ESI+0x13c]; PUSH EAX(ref); PUSH EDI(btn); MOV ECX,ESI; CALL [0x4d1088] SetZorder
    "61" "8b8c24ac000000" "e91b20f3ff")             # popad; MOV ECX,[ESP+0xac]; JMP 0x447330

# ---- aplica nos arquivos (rakion.exe e rakion.bin) ----
for name in TARGETS:
    path = os.path.join(BIN_DIR, name)
    # le SEMPRE do binario LIMPO: o .orig (backup do swap) se existir, senao o proprio arquivo
    src = path + ".orig" if os.path.exists(path + ".orig") else path
    if not os.path.exists(src):
        print("[skip] %s nao existe" % name); continue
    data = bytearray(open(src, "rb").read())
    cave_off = va_to_off(data, CAVE_VA)
    hook_off = va_to_off(data, HOOK_VA)
    click_cave_off = va_to_off(data, CLICK_CAVE_VA)
    click_hook_off = va_to_off(data, CLICK_HOOK_VA)
    zfix_edit_off = va_to_off(data, ZFIX_EDIT_VA)
    zfix_block_off = va_to_off(data, ZFIX_BLOCK_VA)
    if any(data[cave_off+i] for i in range(len(code))) or any(data[click_cave_off+i] for i in range(len(click_cave))) \
       or any(data[zfix_block_off+i] for i in range(len(ZFIX_BLOCK))):
        print("[!] %s: code-cave NAO esta livre — pulando p/ nao corromper" % name); continue
    data[cave_off:cave_off+len(code)] = code                          # Etapa 1: cria o botao
    data[hook_off:hook_off+HOOK_LEN] = hook
    data[click_cave_off:click_cave_off+len(click_cave)] = click_cave  # Etapa 2: clique -> /addbot
    data[click_hook_off:click_hook_off+CLICK_HOOK_LEN] = click_hook
    data[zfix_edit_off:zfix_edit_off+len(ZFIX_EDIT)] = ZFIX_EDIT      # Etapa 3: z-order (desvio apos a Etapa 1)
    data[zfix_block_off:zfix_block_off+len(ZFIX_BLOCK)] = ZFIX_BLOCK
    open(path + ".botbtn", "wb").write(data)
    print("OK %-11s -> %s.botbtn  (botao %dB @%#x + clique %dB @%#x + zorder %dB @%#x)" % (name, name, len(code), CAVE_VA, len(click_cave), CLICK_CAVE_VA, len(ZFIX_BLOCK), ZFIX_BLOCK_VA))
print("Original intacto. Aplique com: tools\\swap_botbtn.ps1 apply")
