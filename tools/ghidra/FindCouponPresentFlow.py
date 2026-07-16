# Decompila o fluxo compartilhado de validacao de cupom e sorteio/grant de presentes.
# Saida: C:\temp\coupon_present_flow.txt
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor

TARGETS = [
    0x0040BD80,  # valida paymentType/cupom do inventario
    0x0041D570,  # decide a faixa de presente conforme gasto
    0x0041D650,  # publica os presentes na sessao
    0x004208E0,  # request CharacterStateClear
    0x00420A40,  # request CharacterChangeCharName
    0x00427760,  # callback CharacterStateClear
    0x004278D0,  # callback CharacterChangeCharName
]

fm = currentProgram.getFunctionManager()
decomp = DecompInterface()
decomp.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()

with open(r"C:\temp\coupon_present_flow.txt", "w") as output:
    for offset in TARGETS:
        address = currentProgram.getAddressFactory().getDefaultAddressSpace().getAddress(offset)
        function = fm.getFunctionAt(address)
        if function is None:
            output.write("\n===== ausente @ %08x =====\n" % offset)
            continue
        result = decomp.decompileFunction(function, 180, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %08x =====\n" % (function.getName(), offset))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("coupon/present: %d alvos processados" % len(TARGETS))
