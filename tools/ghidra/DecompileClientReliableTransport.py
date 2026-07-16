# -*- coding: utf-8 -*-
# Extrai transporte UDP reliable/P2P do engine.dll v258.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


ADDRESSES = (
    0x36007110,  # agregado SetHaveTunnelingClient
    0x36007120,  # agregado IsHave_TunnelingClient
    0x360FF750,  # CNet::InitNetwork
    0x360FFB10,  # CNet::SendTo
    0x361001F0,  # CNet::PacketBufferRecvUpdate
    0x36100BF0,  # CNet::SendPacket_Reliable
    0x36100EA0,  # registro/fila de pacote recebido
    0x36100EF0,  # builder direto
    0x36100F90,  # builder com relay header
    0x361010D0,  # avança tentativa/deadline
    0x361010F0,  # decide retransmissão
    0x36101050,  # leitura do serial da fila
    0x36101090,  # cópia de datagrama para registro
    0x36101130,  # insere registro na fila
    0x36101180,  # busca registro por origem
    0x361011B0,  # remove registro confirmado
    0x36100780,  # CNet::SendToOtherClient
    0x36100980,  # CNet::SendToOtherClientReliable
    0x361927C0,  # WorldNet virtual: tunneling all
    0x36192830,  # WorldNet virtual: tunneling one
    0x36194C90,  # wrapper tunneling all
    0x36194D20,  # wrapper tunneling one
    0x36194DB0,  # decisão IsTunneling_Client
)

NAME_PARTS = (
    "SendData",
    "PacketBuffer",
    "SendPacket_Reliable",
    "SendPacket_Unreliable",
    "GetRelayHeader",
    "RecvFrom",
    "SendTo",
)

manager = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()


def decompile(function):
    result = decompiler.decompileFunction(function, 240, monitor)
    if result and result.getDecompiledFunction():
        return result.getDecompiledFunction().getC()
    return "(falha)"


functions = {}
for address in ADDRESSES:
    function = manager.getFunctionContaining(toAddr(address))
    if function:
        functions[str(function.getEntryPoint())] = function

for function in manager.getFunctions(True):
    if any(part in function.getName() for part in NAME_PARTS):
        functions[str(function.getEntryPoint())] = function

with open(r"C:\temp\client_reliable_transport.txt", "w") as output:
    for key in sorted(functions):
        function = functions[key]
        output.write("\n===== %s @ %s =====\n" % (function.getName(), function.getEntryPoint()))
        output.write(decompile(function).encode("ascii", "replace").decode("ascii"))

print("transporte reliable/P2P do cliente extraido")
