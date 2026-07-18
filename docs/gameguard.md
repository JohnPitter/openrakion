# GameGuard — análise e veredito

## TL;DR

O GameGuard original do Rakion (**nProtect / INCA, build de 2007**) **não inicializa mais**. Ele falha com **"Game guard error : 0"** porque o `GameMon` tenta contatar o servidor de update/auth do nProtect, que está **fora do ar** há anos. **Não é** problema de Windows 11, de driver de kernel, nem de assinatura — e por isso **uma VM Windows 7/10 não resolve**.

## Evidência

Capturando a rede do processo durante a inicialização do GameMon:

```
TCP  10.0.2.15:49708  ->  61.78.35.29:6060   SYN_SENT   (GameMon.des)
```

- `61.78.35.29:6060` é o servidor de atualização/autenticação do nProtect (faixa coreana da SoftNyx/INCA). Fica em **`SYN_SENT`** (sem resposta) → o GG aborta com erro 0.
- O log de evento de **Code Integrity** do Windows **não** mostra bloqueio de driver. O serviço `npggNT` **nem chega a ser instalado** — a falha é **antes** da fase de driver.
- O comportamento é **idêntico** em Windows 11 e numa VM Windows 10 com `testsigning on` → confirma que o sistema operacional não é o fator.

## Conclusão

Para "fazer o GameGuard funcionar" seria necessário **emular o protocolo do servidor nProtect** (proprietário, não documentado) — inviável na prática.

## Alternativas para jogar

1. **Fluxo do launcher** — em alguns builds o `rakion.bin` lançado pelo `NyxLauncher` conecta ao broker/world **mesmo com o GameGuard falhando de forma não-fatal**. Foi o caminho que funcionou nos nossos testes.
2. **Patch no-GG no client** — neutralizar o lançamento do GameMon no `rakion.bin` (engenharia reversa). O "gate" fica na função de init do GG, na chamada que lança o GameMon: substituir a `call` por `xor eax,eax; add esp,4` (resultado = 0/sucesso, pilha balanceada) faz o jogo rodar sem GameGuard. Lado servidor, pode ser necessário neutralizar a checagem de GG (auth) do world.

> Nota: patches no client mudam o MD5/sha1; se o servidor (`file.php`) impuser a checagem, atualize os hashes esperados. No build v258 testado, a checagem não era imposta.

---

# OpenGuard — anti-cheat server-side do OpenRakion

Como o nProtect GameGuard está morto e o cliente roda sem ele, o OpenRakion traz seu
**próprio anti-cheat, 100% server-side** (nenhum agente/DLL no cliente — fiel ao princípio
"sempre há caminho server-side"). Ele reúne num pipeline único as verificações que o
servidor **consegue impor sozinho** e que antes estavam dispersas e passivas pelo World.

## O que o servidor observa

| Detecção | Origem | Gravidade |
|---|---|---|
| **Integridade do binário** | hash reportado no `Op_VerifyClientHash` (0x65) vs `[Client] MD5_1/MD5_2` | High / Medium (ausente) |
| **Sequência de protocolo** | seq TCP fora de ordem (`ClientSession`) | Medium |
| **Opcode desconhecido** | fora da tabela de dispatch | Medium |
| **Frame forjado** | `[u16 size]` inválido ou conteúdo curto | High |
| **Flood de opcodes (TCP)** | rate-limit no ponto único de entrada (`ClientSession.DispatchAsync`) — cobre login 0x0C, keepalive e a cadeia de lobby | Low (dropa) |
| **Flood de gameplay (UDP)** | rate-limit no relay do `UdpGameplay` (vetor de amplificação) | Low (dropa) |
| **Chave de sessão UDP** | `user+0x1464` divergente | Medium |

## Arquitetura

Slice de domínio em [`RakionServer.World/Security/`](../server/RakionServer/src/RakionServer.World/Security/),
**isolado de I/O**: o `AntiCheatService` recebe primitivos/DTOs (nunca o `ClientSession`),
pontua a sessão, consulta a política e devolve uma `GuardDecision` **semântica** (dropar /
kickar). Quem chama (borda de rede) aplica o `Disconnect` com o código de DISC concreto
(fonte única em `Protocol.DiscReason`). A auditoria sai por `IViolationSink` — composto de
dois sinks: log `"guard"` + **`DbViolationSink`** (tabela `anticheat_log`, provisionada no
`EnsureSchemaAsync`). O sink de DB é fire-and-forget (falha de DB nunca trava o dispatch) e
**coalescido** por (slot, tipo) numa janela de 5s (`hits` acumula) — senão um flood de
pacotes viraria um flood idêntico de INSERTs. O painel admin lê em **`/openguard`**
(filtro por conta, limpar log).

- **Pontuação/política**: cada violação soma pontos por gravidade; ao cruzar `KickScore`,
  o serviço sinaliza kick (quando `EnforceKick`). O score **decai** `ScoreDecayPerMin`
  pontos/min — violações esparsas numa sessão longa "esfriam" em vez de acumular até um
  falso positivo. A integridade de binário kicka na hora (`EnforceClientHash`, DISC `0xbc`,
  fiel ao exe).
- **Estado por sessão**: contadores de janela fixa + score, esquecidos no disconnect.

## Modo de operação (config `[AntiCheat]`)

Padrão = **MODO OBSERVAÇÃO**: detecta e audita, mas **não** desconecta — não atrapalha o
cliente offline de uso pessoal. Ligue a imposição quando quiser:

| Chave | Padrão | Efeito |
|---|---|---|
| `Enabled` | `1` | liga o pipeline (0 = passa tudo direto) |
| `EnforceKick` | `0` | desconecta ao cruzar `KickScore` |
| `EnforceClientHash` | `0` | desconecta no mismatch/ausência de hash (no-op sem `MD5_1/2`) |
| `MaxOpcodesPerWindow` / `OpcodeWindowMs` | `120` / `1000` | teto de opcodes TCP por janela |
| `MaxGameplayPerWindow` / `GameplayWindowMs` | `400` / `1000` | teto de pacotes UDP por janela |
| `KickScore` | `100` | score acumulado que dispara o kick |
| `ScoreDecayPerMin` | `10` | decaimento do score por minuto (0 = nunca decai) |

Contrato coberto por testes de domínio puros
([`AntiCheatServiceTests`](../server/RakionServer/tests/RakionServer.World.Tests/AntiCheatServiceTests.cs)):
rate-limit + reset de janela, acúmulo de score/kick, atestação de hash (match/mismatch/
ausente/sem-referência) e isolamento por sessão.
