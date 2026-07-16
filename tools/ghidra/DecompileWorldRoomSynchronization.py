# -*- coding: utf-8 -*-
# Extrai as rotinas que serializam e aplicam sincronizacao da sala no worldserv.exe v258.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

ADDRESSES = (
    0x00405740,  # fechamento da sala
    0x004061F0,  # broadcast da sala
    0x004075A0,  # troca de time/assento
    0x00407910,  # estado de slot
    0x004091E0,  # remocao de jogador e troca de host
    0x004097C0,  # expulsao pelo host
    0x00404FC0,  # localiza/remove usuario da room de chat
    0x0040AF60,  # transicao de sessao apos remocao do field
    0x0041B8B0,  # cleanup/notificacao da sessao removida
    0x00423AD0,  # handler C->S 0x3D por estado
    0x00423B70,  # handler C->S 0x3E por estado
    0x00423C00,  # handler C->S 0x3F
    0x00423CC0,  # handler C->S 0x40
    0x00423DD0,  # handler C->S 0x41
    0x00424100,  # handler C->S 0x42
    0x00424210,  # handler C->S 0x43
)
manager = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

with open(r"C:\temp\world_room_synchronization.txt", "w") as output:
    for address in ADDRESSES:
        function = manager.getFunctionAt(toAddr(address))
        if function is None:
            output.write("\n===== funcao ausente @ %08x =====\n" % address)
            continue
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %08x =====\n" % (function.getName(), address))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("sincronizacao de sala extraida")
