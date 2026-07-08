# RakionDiag — diagnóstico do muro do HIT×N (task #21)

Diagnóstico por **leitura externa pura** (`ReadProcessMemory`) — **não injeta nada** no jogo. Injeção via
`LoadLibrary` (mesmo precoce) crasha o anti-tamper; leitura de memória (como os patches read/write da
launcher) é permitida. Cria o campo da entidade do bot que não passa no gate do HIT×N.

## Pergunta

O HIT×N conta ao acertar o **humano-peer** mas **não** ao acertar o **bot**, sendo os dois criados pelo
mesmo `0x4b`. A diferença tem de estar num campo da entidade do bot (team/alive/HP/template/flag).

## Como funciona

`diag.ps1` resolve a `CEntity*` de cada slot replicando o `GetPlayerEntity @engine.dll 0x36121530` — puro
read, cravado por disassembly (engine.dll base fixa 0x36000000, sem ASLR):

```
pNet  = [0x362ba778]        ; CNetworkLibrary
A     = [pNet + 0x18]
B     = [A + 0x10]          ; base da tabela de slots (entrada = 0x100 bytes)
entry = B + slot*0x100
ent   = [entry + 4]         ; CEntity*  (0 no [entry] = slot vazio)
```

Dumpa o struct de cada slot em 24 snapshots; `diff_entities.py` compara o slot do humano-peer (recebe HIT×N)
contra o do bot (não) e destaca os campos que diferem de forma estrutural.

## Um comando

Com **o(s) cliente(s) já no STAGE, com o bot** (não precisa de env nem de build):

```powershell
.\diag.ps1
```

Dumpa de todos os `rakion.exe` rodando (cada cliente é um processo) → `C:\temp\entdiff\pid<PID>\`.
Deixe o stage ativo ~1 min (parado, andando, batendo) enquanto ele roda os 24 snapshots.

Depois pegue os seats no `worldserver.log` (host = 0; o **humano-peer** e o **bot**) e:

```powershell
.\diag.ps1 -HumanSeat 10 -BotSeat 11
```

Ele re-dumpa e roda o diff por PID (o cliente certo é o que tem **ambos** os seats como entidade). Ou o diff
direto sobre dumps já coletados:

```powershell
python .\diff_entities.py C:\temp\entdiff\pid<PID> 10 11
```

## Ler o resultado

Os offsets marcados `<<< humano TEM, bot NÃO` são os suspeitos: um flag/ponteiro que o peer real tem e o
bot não — provável gate (alive/team/template/HP-ptr). Esse offset vira o alvo do fix server-side (mandar a
mensagem que seta aquele valor na entidade do bot).

### Flags
- `-HumanSeat <n> -BotSeat <n>` — roda o diff no fim.
- `-Snapshots <n>` (default 24), `-IntervalMs <ms>` (default 2500), `-EntBytes <n>` (default 0x2800).

## Segurança / escopo

**Só leitura** — `OpenProcess(PROCESS_VM_READ)` + `ReadProcessMemory`. Não escreve byte nenhum no jogo, não
injeta DLL, não chama função no processo. Uso exclusivo de RE/preservação do binário do próprio autor.
