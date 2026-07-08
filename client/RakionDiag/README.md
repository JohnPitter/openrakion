# RakionDiag — diagnóstico do muro do HIT×N (task #21)

Diagnóstico **passivo** (só leitura + um getter estável; **não patcheia** o jogo) para cravar o campo da
entidade do bot que não passa no gate do HIT×N nativo.

## Pergunta

O HIT×N conta ao acertar o **humano-peer** mas **não** ao acertar o **bot**, sendo os dois criados pelo
mesmo `0x4b`. A diferença tem de estar num campo da entidade do bot (team/alive/HP/template/flag). O
`entitydiff.dll` resolve a `CEntity*` de cada slot via `GetPlayerEntity(slot) @engine.dll 0x36121530`
(offset estável, sem ASLR) e dumpa o struct cru; o `diff_entities.py` compara humano vs bot offline.

## Passos

Tudo pela launcher via um script só. Do diretório `client\RakionDiag`:

1. **Subir os servidores** normalmente (`start-stack.ps1`), Docker/MariaDB no ar.

2. **Rodar o diagnóstico** (compila a DLL se faltar, limpa dumps antigos, abre a launcher já com a env
   `RAKION_DIAG_DLL` setada — a injeção é precoce, no launch suspenso, antes do anti-tamper armar):
   ```powershell
   .\run-diag.ps1
   ```
   Lance o cliente **HOST** pela launcher que abriu (a injeção é automática; o status mostra
   `diag: injeção precoce agendada`). O script espera os snapshots terminarem.

3. **No jogo**: host cria a sala, o 2º humano entra, host dá `/addbot`, entrem no **stage** e **fiquem ~1
   minuto** (parados, andando e trocando golpes — o dump roda 24 snapshots a cada 2.5s).

4. **Anote os seats** do `worldserver.log`: o **humano-peer** (ex.: seat 10) e o **bot** (ex.: seat 11).
   O host é seat 0.

5. **Diffar** — ou passe os seats direto ao script (ele roda o diff no fim):
   ```powershell
   .\run-diag.ps1 -HumanSeat 10 -BotSeat 11
   ```
   Ou, com os dumps já coletados, só o diff:
   ```powershell
   python .\diff_entities.py C:\temp\entdiff 10 11
   ```

> Só compilar a DLL, sem rodar: `.\build.ps1` (gera `C:\temp\entitydiff.dll`). Outro VS: `.\build.ps1 -Vcvars "<caminho\vcvarsall.bat>"`.

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
