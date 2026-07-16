# -*- coding: utf-8 -*-
# Extrai a cadeia de autoridade de combate e resultado do worldserv.exe v258.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

ADDRESSES = (
    0x00405400,  # condição especial de morte no modo 4
    0x00405950,  # calcula saldo/contadores usados na saida
    0x00405980,  # resultado individual do match (win/lose)
    0x004061C0,  # aplica pontos ao registro do jogador
    0x004063A0,  # pós-morte dos modos 0/1
    0x004066C0,  # alterna o estado 0x2CC e publica 0x54/0x55
    0x004067C0,  # pós-saída e recomposição do field
    0x00407BE0,  # encerramento do match
    0x00407E00,  # saida/morte do proprio jogador
    0x004087D0,  # GameDiePlayer
    0x0040B900,  # consumo associado a saida
    0x0040B940,  # recalcula equipamento e campos do resultado
    0x0040BB60,  # consolida placar W/L/D do jogador
    0x0040AC20,  # predicado de sessão usado por 0x54/0x55
    0x0040D300,  # progressao/level-up
    0x0041B860,  # valida estado do jogador no field
    0x0041CF80,  # validacao de EXP/gold reportados
    0x00424350,  # handler C->S 0x46
    0x00424A20,  # handler C->S 0x4F
    0x00424B60,  # handler C->S 0x50
)

manager = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

with open(r"C:\temp\world_combat_authority.txt", "w") as output:
    for address in ADDRESSES:
        function = manager.getFunctionAt(toAddr(address))
        if function is None:
            output.write("\n===== funcao ausente @ %08x =====\n" % address)
            continue
        result = decompiler.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %08x =====\n" % (function.getName(), address))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("autoridade de combate extraida")
