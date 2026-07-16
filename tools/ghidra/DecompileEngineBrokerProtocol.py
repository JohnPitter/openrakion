# -*- coding: utf-8 -*-
# Extrai a superfície Broker do engine.dll v258.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


ADDRESSES = (
    0x3618CB30,  # thread de recepção TCP Broker
    0x3618CD50,  # InitBrokerNetLib
    0x3618CE70,  # IScavengerBrokerNet::Disconnect
    0x3618D1D0,  # IScavengerBrokerNet::Connect
    0x3618D3A0,  # IScavengerBrokerNet::SendWorldList
    0x3618D3E0,  # IScavengerBrokerNet::SendDisconnect
)

manager = currentProgram.getFunctionManager()
references = currentProgram.getReferenceManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
functions = {}

for address in ADDRESSES:
    function = manager.getFunctionContaining(toAddr(address))
    if not function:
        continue
    functions[function.getEntryPoint().getOffset()] = function
    for reference in references.getReferencesTo(function.getEntryPoint()):
        caller = manager.getFunctionContaining(reference.getFromAddress())
        if caller:
            functions[caller.getEntryPoint().getOffset()] = caller

with open(r"C:\temp\engine_broker_protocol.txt", "w") as output:
    for entry in sorted(functions):
        function = functions[entry]
        result = decompiler.decompileFunction(function, 240, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %s =====\n" %
                     (function.getName(True), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("protocolo Broker do engine: %d funcoes" % len(functions))
