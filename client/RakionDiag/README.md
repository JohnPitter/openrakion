# RakionDiag — diagnóstico do muro do HIT×N (task #21)

Diagnóstico **passivo** (só leitura + um getter estável; **não patcheia** o jogo) para cravar o campo da
entidade do bot que não passa no gate do HIT×N nativo.

## Pergunta

O HIT×N conta ao acertar o **humano-peer** mas **não** ao acertar o **bot**, sendo os dois criados pelo
mesmo `0x4b`. A diferença tem de estar num campo da entidade do bot (team/alive/HP/template/flag). O
`entitydiff.dll` resolve a `CEntity*` de cada slot via `GetPlayerEntity(slot) @engine.dll 0x36121530`
(offset estável, sem ASLR) e dumpa o struct cru; o `diff_entities.py` compara humano vs bot offline.

## Um comando faz tudo

Do diretório `client\RakionDiag`:

```powershell
.\diag.ps1
```

Isso: **(1)** compila a `entitydiff.dll`, **(2)** garante Docker + MariaDB (`rakion-db`), **(3)** sobe a
stack de servidores, **(4)** limpa dumps antigos, **(5)** abre a launcher com `RAKION_DIAG_DLL` setada,
**(6)** espera os 24 snapshots e **(7)** mostra/roda o diff.

Enquanto o script espera, **no jogo**:
1. Lance o cliente **HOST** pela launcher que abriu (injeção automática — o status mostra
   `diag: injeção precoce agendada`).
2. Host cria a sala, o 2º humano entra, host dá `/addbot`, entrem no **stage** e **fiquem ~1 minuto**
   (parados, andando e trocando golpes — 24 snapshots a cada 2.5s).

Depois pegue os seats no `worldserver.log` (host = 0; o **humano-peer** e o **bot**) e:

```powershell
.\diag.ps1 -SkipServers -HumanSeat 10 -BotSeat 11
```

(ou, com os dumps já na mão, só o diff: `python .\diff_entities.py C:\temp\entdiff 10 11`).

### Flags do diag.ps1
- `-HumanSeat <n> -BotSeat <n>` — roda o diff desses seats no fim.
- `-SkipServers` — pula Docker/MariaDB/stack (quando já estão no ar).
- `-Dll <caminho>` — DLL alternativa. `-DbContainer <nome>` — container do MariaDB (default `rakion-db`).

> Só compilar a DLL: `.\build.ps1` (gera `C:\temp\entitydiff.dll`). Outro VS: `.\build.ps1 -Vcvars "<...\vcvarsall.bat>"`.

## Ler o resultado

A saída lista os offsets **estáveis e divergentes** entre a entidade do humano e a do bot. Os marcados
`<<< humano TEM, bot NÃO` são os suspeitos diretos: um flag/ponteiro que o peer real tem e o bot não —
provavelmente o gate (alive/team/template/HP-ptr). Esse offset vira o alvo do fix server-side (mandar o
`0x830c`/`0x8312`/campo que seta aquele valor na entidade do bot).

## Saídas

- `C:\temp\entdiff\slotNN_snapSS.bin` — dumps por slot/snapshot
- `C:\temp\entdiff\entitydiff.log` — log de ocupação por snapshot (confirma quais slots resolveram)
- `C:\temp\entdiff\done.txt` — marcador de fim

## Segurança / escopo

DLL **passiva**: lê memória e chama um getter (`GetPlayerEntity`), **não escreve** byte nenhum no jogo.
Injeção é opt-in (`RAKION_DIAG_DLL`); sem a env, a launcher não injeta nada. Uso exclusivo de RE/preservação
do binário do próprio autor — **não** é injeção para funcionalidade (regra `sem-ddl-injetada`).
