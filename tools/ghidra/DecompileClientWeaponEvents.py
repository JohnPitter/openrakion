# -*- coding: utf-8 -*-
# Extrai contratos e consumidores dos eventos reliable de armas do entitiesmp.dll v258.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.util.task import ConsoleTaskMonitor


ADDRESSES = (
    0x35130050,  # ESetWeapon::ESetWeapon
    0x35130070,  # ESetWeapon::CheckIDs
    0x35130090,  # EShootWeapon::CheckIDs
    0x351300B0,  # EShootShuriken::CheckIDs
    0x351300D0,  # ERequestHoldAttack::ERequestHoldAttack
    0x35130100,  # ERequestHoldAttack::CheckIDs
    0x35130120,  # EHoldAttack::EHoldAttack
    0x35130150,  # EHoldAttack::CheckIDs
    0x351347F0,  # EShootWeapon::EShootWeapon
    0x35134880,  # EShootShuriken::EShootShuriken
    0x3514F540,  # CPlayer::CheckHoldAttack
    0x3514FAF0,  # CPlayer::ReceiveHoldAttack
    0x35152C90,  # CPlayer::ExecuteHoldAttack
    0x35165DB0,  # ESetWeapon copy constructor
    0x35165E50,  # EShootWeapon copy constructor
    0x35165F10,  # EShootShuriken copy constructor
    0x35165FE0,  # ERequestHoldAttack copy constructor
    0x35166090,  # EHoldAttack copy constructor
    0x3516CC70,  # CPlayerAnimator::ChangeWeapon
    0x351738C0,  # CPlayerAnimator::SetWeapon
    0x35174400,  # CPlayerAnimator::SetWeapon
    0x35178500,  # CPlayerWeapons::ShootShuriken
    0x35179500,  # CPlayerWeapons::RequestShootWeapon(float, int, byte, CEntity*, long)
    0x35179950,  # CPlayerWeapons::RequestShootWeapon(long, Vector, Vector)
    0x35179A40,  # CPlayerWeapons::ShootWeapon
    0x35217E80,  # ESetWeapon::operator=
    0x35217ED0,  # ClearToDefault(ESetWeapon)
    0x35217F10,  # EShootWeapon::operator=
    0x35217F80,  # ClearToDefault(EShootWeapon)
    0x35217FE0,  # EShootShuriken::operator=
    0x35218050,  # ClearToDefault(EShootShuriken)
    0x352180C0,  # ERequestHoldAttack::operator=
    0x35218120,  # ClearToDefault(ERequestHoldAttack)
    0x35218170,  # EHoldAttack::operator=
    0x352181D0,  # ClearToDefault(EHoldAttack)
)

manager = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()
output_path = r"C:\temp\client_weapon_events.txt"

with open(output_path, "w") as output:
    output.write("PROGRAM=%s\n" % currentProgram.getName())
    for value in ADDRESSES:
        address = toAddr(value)
        function = manager.getFunctionContaining(address)
        if function is None:
            disassemble(address)
            createFunction(address, None)
            function = manager.getFunctionAt(address)
        if function is None:
            output.write("\nADDRESS=0x%08X sem funcao\n" % value)
            continue
        result = decompiler.decompileFunction(function, 300, monitor)
        code = result.getDecompiledFunction().getC() if result and result.getDecompiledFunction() else "(falha)"
        output.write("\n===== %s @ %s =====\n" % (function.getName(), function.getEntryPoint()))
        output.write(code.encode("ascii", "replace").decode("ascii"))

print("eventos de arma extraidos em " + output_path)
