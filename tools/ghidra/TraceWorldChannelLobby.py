# -*- coding: utf-8 -*-
# Extrai o agregado de canal/lobby, serializers, chat, saída e as duas famílias
# de mensagens chamadas "ping" no worldserv.exe v258.
# Saída: C:\temp\world_channel_lobby.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

ADDRESSES = (
    0x00404DA0,  # notificação/lista de membros do canal
    0x00404EF0,  # broadcast de entrada no canal
    0x00404FC0,  # entrada e snapshot completo do canal
    0x004051F0,  # transfere o boss/owner para outro slot local
    0x00405240,  # saída/remoção do canal
    0x00406240,  # ping/consulta de field da lista de salas
    0x004062C0,  # ping de membro durante a partida
    0x0040AF60,  # grava canal/slot na sessão
    0x0040AFB0,  # serializa registro do personagem
    0x0041B8B0,  # seleciona canal e entra
    0x0041BC10,  # handler C->S 0x20
    0x0041BCA0,  # handler C->S 0x22
    0x00420C20,  # handler C->S 0x29
    0x00420CB0,  # handler C->S 0x2A
    0x004257B0,  # handler C->S 0x59
    0x00425860,  # handler C->S 0x5A
    0x00429230,  # handler C->S 0x1E
)

manager = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

with open(r"C:\temp\world_channel_lobby.txt", "w") as output:
    for address in ADDRESSES:
        function = manager.getFunctionAt(toAddr(address))
        if function is None:
            output.write("\n===== funcao ausente @ %08x =====\n" % address)
            continue
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %08x =====\n" % (function.getName(), address))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("canal/lobby do World extraído")
