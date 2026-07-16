# -*- coding: utf-8 -*-
# Decompila as rotinas centrais de dano, morte e respawn de entitiesmp.dll.
# @category Rakion
from ghidra.app.decompiler import DecompInterface
from ghidra.program.model.symbol import SourceType
from ghidra.util.task import ConsoleTaskMonitor


TARGETS = (
    0x35130160, 0x35130170, 0x35130180, 0x35130190, 0x351301C0,
    0x351303A0, 0x351303B0, 0x351303E0, 0x351303F0, 0x35130400, 0x35130420,
    0x35130920, 0x35130950, 0x351309B0,
    0x35130AD0, 0x35130B00, 0x35130B70, 0x35130B90, 0x35130BA0,
    0x35131B70,
    0x35134930, 0x35134950, 0x351349C0, 0x35134C90, 0x35134CB0,
    0x351357F0, 0x35135810,
    0x35138810, 0x35138850,
    0x3513C880, 0x3513C9B0,
    0x35140D30, 0x35140E70, 0x35140EA0,
    0x35147140, 0x35147A80,
    0x3514AE30, 0x3514B140,
    0x3514F8D0, 0x3514FEA0,
    0x35150770, 0x35152DA0, 0x351535E0,
    0x3515E830, 0x35162370, 0x35164E10, 0x35164EC0,
    0x3516DC80,
    0x35177A30, 0x351781D0, 0x3517B020, 0x3517B110,
    0x3517B1E0, 0x3517D3D0,
    0x35218230, 0x352182B0, 0x352182E0, 0x35218330,
    0x35218900, 0x35218950, 0x35218990, 0x352189D0,
)
NAMES = {
    0x35130160: "EPlayerDamage_GetSizeOf",
    0x35130170: "EPlayerDamage_CheckIDs",
    0x35130180: "EPlayerRemainHP_GetSizeOf",
    0x35130190: "EPlayerRemainHP_Ctor",
    0x351301C0: "EPlayerRemainHP_CheckIDs",
    0x351303A0: "EPlayerDeath_GetSizeOf",
    0x351303B0: "EPlayerDeath_Ctor",
    0x351303E0: "EPlayerDeath_CheckIDs",
    0x351303F0: "ERespawn_GetSizeOf",
    0x35130400: "ERespawn_Ctor",
    0x35130420: "ERespawn_CheckIDs",
    0x35130920: "CPlayer_AddHP",
    0x35130950: "CPlayer_ReduceHP",
    0x351309B0: "CPlayer_ReduceAP",
    0x35130AD0: "CPlayer_GetMaxHP",
    0x35130B00: "CPlayer_SetMaxHP",
    0x35130B70: "CPlayer_SetAlive",
    0x35130B90: "CPlayer_IsAlive",
    0x35130BA0: "CPlayer_IsDead",
    0x35131B70: "CPlayer_AutoRecoverHP",
    0x35134930: "EPlayerDamage_MakeCopy",
    0x35134950: "EPlayerDamage_Ctor",
    0x351349C0: "EPlayerRemainHP_MakeCopy",
    0x35134C90: "EPlayerDeath_MakeCopy",
    0x35134CB0: "ERespawn_MakeCopy",
    0x351357F0: "CPlayer_ReduceCP",
    0x35135810: "CPlayer_SetDead",
    0x35138810: "CPlayer_ConfirmDamage",
    0x35138850: "CPlayer_ApplyPacket_HP_AP",
    0x3513C880: "CPlayer_ReceiveDamage_Legacy",
    0x3513C9B0: "CPlayer_DamageImpact",
    0x35140D30: "CPlayer_Send_HP_AP",
    0x35140E70: "CPlayer_GetHP",
    0x35140EA0: "CPlayer_SetHP",
    0x35147140: "CPlayer_WorkReduce_HP_AP",
    0x35147A80: "CPlayer_DeathActions",
    0x3514AE30: "CPlayer_GetRespawnTime",
    0x3514B140: "CPlayer_DrawRespawnGuage",
    0x3514F8D0: "CPlayer_Freeze",
    0x3514FEA0: "CPlayer_ReceiveDamage_OnGuard",
    0x35150770: "CPlayer_ApplyReceiveDamage",
    0x35152DA0: "CPlayer_ReceiveDamage",
    0x351535E0: "CPlayer_AliveActions",
    0x3515E830: "CPlayer_Death",
    0x35162370: "CPlayer_Respawn",
    0x35164E10: "CPlayer_RespawnWork",
    0x35164EC0: "CPlayer_RespawnPlayers",
    0x3516DC80: "CPlayerAnimator_CheckAbleInvinAttack",
    0x35177A30: "CPlayerWeapons_GetHitType",
    0x351781D0: "CPlayerWeapons_Attack",
    0x3517B020: "CPlayerWeapons_GetDamageMotionType",
    0x3517B110: "CPlayerWeapons_GetDamageType",
    0x3517B1E0: "CPlayerWeapons_SetupDamageInfo",
    0x3517D3D0: "CPlayerWeapons_UpdateWeaponHit",
    0x35218230: "EPlayerDamage_Assign",
    0x352182B0: "EPlayerDamage_ClearToDefault",
    0x352182E0: "EPlayerRemainHP_Assign",
    0x35218330: "EPlayerRemainHP_ClearToDefault",
    0x35218900: "EPlayerDeath_Assign",
    0x35218950: "EPlayerDeath_ClearToDefault",
    0x35218990: "ERespawn_Assign",
    0x352189D0: "ERespawn_ClearToDefault",
}
OUTPUT = r"C:\temp\client_player_combat.txt"

manager = currentProgram.getFunctionManager()
decompiler = DecompInterface()
decompiler.openProgram(currentProgram)
monitor = ConsoleTaskMonitor()


def decompile(function):
    result = decompiler.decompileFunction(function, 240, monitor)
    if result and result.getDecompiledFunction():
        return result.getDecompiledFunction().getC()
    return "(falha na decompilacao)"


with open(OUTPUT, "w") as output:
    for target in TARGETS:
        address = toAddr(target)
        function = manager.getFunctionAt(address)
        if function is None:
            disassemble(address)
            function = createFunction(address, NAMES.get(target))
        elif target in NAMES:
            function.setName(NAMES[target], SourceType.USER_DEFINED)
        output.write("\n===== %s @ %s =====\n" % (
            function.getName() if function else "(sem funcao)", address))
        if function:
            output.write(decompile(function).encode("ascii", "replace").decode("ascii"))
        else:
            output.write("(endereco sem funcao)")
        output.write("\n")

print("rotinas de combate do cliente extraidas em " + OUTPUT)
