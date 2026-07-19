# RakionClientCompat

Bootstrap x86 `version.dll`, carregado pelo fluxo normal antes do entry point. Ele encaminha as
17 exportações para a DLL oficial de 32 bits carregada diretamente do diretório do sistema e chama
`RakionClientPatch.dll`, que centraliza os patches do cliente: diff golden do `rakion-final`,
no-GameGuard, janela/multi-instância/Alt+Tab, IP do servidor, HIT/SHOT, lifecycle, ground-snap e
telemetria de ataque humano contra bot. A DLL também fecha a janela de criação depois do ACK de
personagem, inclusive quando o tutorial é recusado, e corrige a sincronização do Messenger após a
seleção de um personagem novo, disparando o `SetNick` já existente do `Buddy2.dll` depois que o
World confirma a troca de `buddyname` em `0x15`.

O código está separado em bootstrap/forwarding (`version_proxy.cpp`), lifecycle e HIT/SHOT
(`rakion_client_patch.cpp`), patches do cliente (`client_patches.cpp`), IP e telemetria
(`bot_telemetry.cpp`) e log (`compat_log.cpp`). O antigo
binário em `client/RakionClientPatch/build` é somente evidência auxiliar: suas 317 entradas
foram comparadas pelo `verify_legacy_client_patch.py`; ele não é distribuído nem carregado. A
`RakionClientPatch.dll` final é sempre recompilada a partir do código deste diretório.

O servidor continua sendo a autoridade sobre dano, HP e morte. Build, instalação, ativação e rollback:
[`docs/guides/client-compatibility-dll.md`](../../docs/guides/client-compatibility-dll.md).
