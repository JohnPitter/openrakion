"""Captura um módulo PE já carregado e desembrulhado na memória do cliente."""

from __future__ import annotations

import argparse
import ctypes
import json
import struct
import time
from pathlib import Path

import frida


CREATE_SUSPENDED = 0x00000004


class StartupInfo(ctypes.Structure):
    _fields_ = [
        ("cb", ctypes.c_uint32),
        ("reserved", ctypes.c_wchar_p),
        ("desktop", ctypes.c_wchar_p),
        ("title", ctypes.c_wchar_p),
        ("x", ctypes.c_uint32),
        ("y", ctypes.c_uint32),
        ("x_size", ctypes.c_uint32),
        ("y_size", ctypes.c_uint32),
        ("x_chars", ctypes.c_uint32),
        ("y_chars", ctypes.c_uint32),
        ("fill", ctypes.c_uint32),
        ("flags", ctypes.c_uint32),
        ("show", ctypes.c_uint16),
        ("reserved2_count", ctypes.c_uint16),
        ("reserved2", ctypes.c_void_p),
        ("stdin", ctypes.c_void_p),
        ("stdout", ctypes.c_void_p),
        ("stderr", ctypes.c_void_p),
    ]


class ProcessInformation(ctypes.Structure):
    _fields_ = [
        ("process", ctypes.c_void_p),
        ("thread", ctypes.c_void_p),
        ("process_id", ctypes.c_uint32),
        ("thread_id", ctypes.c_uint32),
    ]


def launch_suspended(
    executable: Path, arguments: str, working_directory: Path | None = None
) -> ProcessInformation:
    startup = StartupInfo(cb=ctypes.sizeof(StartupInfo))
    process = ProcessInformation()
    command_line = ctypes.create_unicode_buffer(arguments)
    created = ctypes.windll.kernel32.CreateProcessW(
        str(executable), command_line, None, None, False, CREATE_SUSPENDED,
        None, str(working_directory or executable.parent), ctypes.byref(startup), ctypes.byref(process),
    )
    if not created:
        raise ctypes.WinError()
    return process


def capture_module(process_id: int, module_name: str, timeout: float) -> tuple[dict, bytes, list[dict]]:
    session = frida.attach(process_id)
    script = session.create_script(
        """
        rpc.exports.metadata = function (name) {
          const module = Process.getModuleByName(name);
          return { name: module.name, base: module.base.toString(), size: module.size };
        };
        rpc.exports.read = function (name, offset, size) {
          const module = Process.getModuleByName(name);
          return module.base.add(offset).readByteArray(size);
        };
        rpc.exports.imports = function (name) {
          return Process.getModuleByName(name).enumerateImports().map(function (entry) {
            return {
              name: entry.name,
              module: entry.module,
              address: entry.address.toString(),
              type: entry.type
            };
          });
        };
        """
    )
    script.load()
    deadline = time.monotonic() + timeout
    while True:
        try:
            metadata = script.exports_sync.metadata(module_name)
            chunks = []
            for offset in range(0, metadata["size"], 1024 * 1024):
                size = min(1024 * 1024, metadata["size"] - offset)
                chunks.append(bytes(script.exports_sync.read(module_name, offset, size)))
            imports = script.exports_sync.imports(module_name)
            session.detach()
            return metadata, b"".join(chunks), imports
        except frida.RPCException:
            if time.monotonic() >= deadline:
                session.detach()
                raise TimeoutError(f"módulo {module_name} não carregou em {timeout:.0f}s")
            time.sleep(0.1)


def rebuild_file_layout(memory_image: bytes) -> bytes:
    pe_offset = struct.unpack_from("<I", memory_image, 0x3C)[0]
    section_count = struct.unpack_from("<H", memory_image, pe_offset + 6)[0]
    optional_size = struct.unpack_from("<H", memory_image, pe_offset + 20)[0]
    optional_offset = pe_offset + 24
    header_size = struct.unpack_from("<I", memory_image, optional_offset + 60)[0]
    sections_offset = optional_offset + optional_size
    rebuilt = bytearray(memory_image[:header_size])
    previous_end = header_size
    for index in range(section_count):
        offset = sections_offset + index * 40
        virtual_size = struct.unpack_from("<I", memory_image, offset + 8)[0]
        virtual_address, raw_size, raw_offset = struct.unpack_from("<III", memory_image, offset + 12)
        if virtual_size > raw_size or raw_offset < previous_end:
            raise ValueError("layout PE compactado não pode ser reconstruído sem realocar seções")
        required = raw_offset + raw_size
        if len(rebuilt) < required:
            rebuilt.extend(b"\0" * (required - len(rebuilt)))
        rebuilt[raw_offset:required] = memory_image[virtual_address:virtual_address + raw_size]
        previous_end = required
    return bytes(rebuilt)


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("module")
    parser.add_argument("output", type=Path)
    parser.add_argument("--pid", type=int)
    parser.add_argument("--client", type=Path)
    parser.add_argument("--arguments", default="test 74657374 1A")
    parser.add_argument("--working-directory", type=Path)
    parser.add_argument("--timeout", type=float, default=30)
    parser.add_argument("--keep-running", action="store_true")
    parser.add_argument("--file-layout", action="store_true")
    parser.add_argument("--imports-output", type=Path)
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    launched = None
    process_id = args.pid
    try:
        if process_id is None:
            if args.client is None:
                raise ValueError("informe --pid ou --client")
            launched = launch_suspended(args.client, args.arguments, args.working_directory)
            process_id = launched.process_id
            ctypes.windll.kernel32.ResumeThread(launched.thread)
        metadata, memory_image, imports = capture_module(process_id, args.module, args.timeout)
        args.output.parent.mkdir(parents=True, exist_ok=True)
        output = rebuild_file_layout(memory_image) if args.file_layout else memory_image
        args.output.write_bytes(output)
        if args.imports_output is not None:
            args.imports_output.parent.mkdir(parents=True, exist_ok=True)
            args.imports_output.write_text(
                json.dumps(imports, ensure_ascii=False, indent=2), encoding="utf-8"
            )
        print(json.dumps({**metadata, "pid": process_id, "output": str(args.output)}))
    finally:
        if launched is not None:
            if not args.keep_running:
                ctypes.windll.kernel32.TerminateProcess(launched.process, 0)
            ctypes.windll.kernel32.CloseHandle(launched.thread)
            ctypes.windll.kernel32.CloseHandle(launched.process)


if __name__ == "__main__":
    main()
