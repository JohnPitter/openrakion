# RakionClientCompat

Proxy x86 completo de `version.dll`, carregado pelo fluxo normal antes do entry point. Encaminha as
17 exportações para a DLL oficial de 32 bits (`verorig.dll`) e centraliza os patches do cliente:
diff golden do `rakion-final`, no-GameGuard, janela/multi-instância/Alt+Tab, IP do servidor,
HIT/SHOT, lifecycle, ground-snap e telemetria de ataque humano contra bot.

O servidor continua sendo a autoridade sobre dano, HP e morte. Build, instalação, ativação e rollback:
[`docs/guides/client-compatibility-dll.md`](../../docs/guides/client-compatibility-dll.md).
