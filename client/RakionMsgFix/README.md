# RakionMsgFix — patch client-side da janela F9 do messenger

`msgfix.dll` (native x86) corrige o render da janela do messenger (F9) do `rakion.exe`:

- A janela é criada **oculta** no login; o F9 só faz *toggle* de visibilidade e **não** reconstrói o
  título/lista a partir do store — por isso nasciam **vazios** na abertura (só o *nick change* montava).
- O self-name chegava **truncado em 2 chars** (o campo `@41` do frame 0x0C é fixo em 2 bytes).

O patch hooka `FUN_00489120` (F9-show) e, no 1º SHOW: acha o nome **completo** em `AccountInfo` pelo
prefixo de 2 chars (auto-validante) e o grava em `host+0x44`, depois chama `FUN_00483600` — o mesmo
*refresh* do nick-change. Resultado: o messenger abre com nome completo + contador + lista na hora.

RE completa e offsets: [`docs/protocol-buddy.md`](../../docs/protocol-buddy.md) §"Render da janela F9".

## Build
Requer MSVC BuildTools x86. Rode [`build.bat`](build.bat) → gera `msgfix.dll` (bundle junto ao
RakionLauncher via `RakionLauncher.csproj`). Injeção é **pelo launcher** no launch — injetar por fora
trava o jogo (anti-tamper). Offsets são de `rakion.exe` (ImageBase 0x400000, sem ASLR), verificados
byte-a-byte entre o binário de RE e o cliente real (`rakion-final/Bin/rakion.exe`).
