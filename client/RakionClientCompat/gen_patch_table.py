#!/usr/bin/env python3
"""Gera a tabela de patches do RakionClientCompat a partir do diff binario.

Compara o rakion.exe do rakion-final (fonte autoritativa de todos os patches) contra
o rakion.exe.orig PRISTINO, converte cada offset de
arquivo para RVA usando o cabecalho PE, e emite um header C com {rva, byte} para a DLL
aplicar em runtime (base 0x400000, sem ASLR). Assim o rakion.exe fica pristino e a DLL
reproduz exatamente o binario patcheado.

Uso: python gen_patch_table.py <rakion.exe patcheado> <rakion.exe.orig> <saida.h>
"""
import struct
import sys
import hashlib


EXPECTED_PATCHED_SHA256 = "435F50E3FF9F3F140D4C335336B4BA4A758DF823C146210CC8DA90460960FFFF"
EXPECTED_ORIGINAL_SHA256 = "88E177F243FA4C43769CD323FB4D73E106AE833070F9BCE7B2DC05B8DDFD6AF8"
EXPECTED_PATCH_COUNT = 317


def parse_sections(data):
    """Devolve [(virtual_addr, raw_ptr, raw_size)] das secoes do PE."""
    e_lfanew = struct.unpack_from("<I", data, 0x3C)[0]
    assert data[e_lfanew:e_lfanew + 4] == b"PE\0\0", "PE header invalido"
    num_sections = struct.unpack_from("<H", data, e_lfanew + 6)[0]
    opt_size = struct.unpack_from("<H", data, e_lfanew + 20)[0]
    sect_off = e_lfanew + 24 + opt_size
    sections = []
    for i in range(num_sections):
        base = sect_off + i * 40
        va = struct.unpack_from("<I", data, base + 12)[0]
        raw_ptr = struct.unpack_from("<I", data, base + 20)[0]
        raw_size = struct.unpack_from("<I", data, base + 16)[0]
        sections.append((va, raw_ptr, raw_size))
    return sections


def file_off_to_rva(off, sections):
    for va, raw_ptr, raw_size in sections:
        if raw_ptr <= off < raw_ptr + raw_size:
            return off - raw_ptr + va
    return None


def main():
    patched_path, orig_path, out_path = sys.argv[1], sys.argv[2], sys.argv[3]
    with open(patched_path, "rb") as f:
        patched = f.read()
    with open(orig_path, "rb") as f:
        orig = f.read()

    patched_hash = hashlib.sha256(patched).hexdigest().upper()
    original_hash = hashlib.sha256(orig).hexdigest().upper()
    if patched_hash != EXPECTED_PATCHED_SHA256:
        raise SystemExit(f"rakion-final patcheado incompatível: SHA-256 {patched_hash}")
    if original_hash != EXPECTED_ORIGINAL_SHA256:
        raise SystemExit(f"rakion.exe.orig incompatível: SHA-256 {original_hash}")
    if len(patched) != len(orig):
        raise SystemExit("executáveis golden têm tamanhos diferentes")

    sections = parse_sections(orig)
    entries = []       # (rva, patched_byte, orig_byte)
    n = min(len(patched), len(orig))
    for off in range(n):
        if patched[off] != orig[off]:
            rva = file_off_to_rva(off, sections)
            if rva is None:
                raise SystemExit(f"offset 0x{off:X} fora de qualquer secao PE")
            entries.append((rva, patched[off], orig[off]))

    entries.sort()
    if len(entries) != EXPECTED_PATCH_COUNT:
        raise SystemExit(f"diff golden incompatível: {len(entries)} bytes")
    with open(out_path, "w", newline="\n") as out:
        out.write("// GERADO por gen_patch_table.py -- NAO EDITAR A MAO.\n")
        out.write(f"// golden patched SHA-256: {patched_hash}\n")
        out.write(f"// golden original SHA-256: {original_hash}\n")
        out.write("// Diff exato do rakion.exe patcheado vs pristino, como {RVA, byte-novo, byte-orig}.\n")
        out.write("// A DLL valida o byte-orig antes de escrever o byte-novo (base 0x400000, sem ASLR).\n")
        out.write("#pragma once\n#include <cstdint>\n\n")
        out.write("struct BakedPatch { uint32_t rva; uint8_t to; uint8_t from; };\n\n")
        out.write(f"static const BakedPatch kBakedPatches[] = {{\n")
        for rva, to, frm in entries:
            out.write(f"    {{ 0x{rva:06X}, 0x{to:02X}, 0x{frm:02X} }},\n")
        out.write("};\n")
        out.write(f"static const int kBakedPatchCount = {len(entries)};\n")

    print(f"{len(entries)} bytes de patch escritos em {out_path}")


if __name__ == "__main__":
    main()
