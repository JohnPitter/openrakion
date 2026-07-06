# RE do HIT×N — por que o golpe do humano não conta no bot promovido

> 2026-07-06. Depois que o bot virou **entidade física** no cliente do humano (endereço P2P no
> registro do roster → promoção; sintoma in-game: **CP conta** e o bot **é repelido** ao encostar),
> o **HIT×N nativo ainda não incrementa** e o **golpe não anima**. Esta é a RE da `engine.dll`
> (SE1, símbolos, ImageBase 0x36000000, ESTÁVEL entre builds — a `entitiesmp/gamemp` do rakion-final
> são EMPACOTADAS, offsets não transferem: [[rakion-final-binario-diferente-re-ao-vivo]]) que cravou
> a arquitetura da entrega e isolou os dois muros restantes.

## Mapa cravado (tudo em engine.dll, RVAs reais via EAT)

| símbolo | VA | papel |
|---|---|---|
| `CSessionState::HandleMessage` | **0x3610d7c0** | dispatcher das msgs reliable de entidade **0x307–0x312** (jump table @0x3610e280) |
| `CSessionState::GetActionFromMessage` | 0x3610afe0 | decode do 0x30a (movimento) — funciona (o bot anda) |
| `CSessionState::AddRemoteGeneralNpc` | 0x361097a0 | cria NPC remoto (Cell) — entidade REAL de colisão; **caso 0x307** |
| `CSessionState::AddRemotePlayer` | 0x3610e2b0 | cria PLAYER remoto real — **sem caller interno na engine** |
| `CSessionState::GetPlayer` | (0x07e8) | resolve entidade de jogador por seat |
| aplicador de estado | 0x3610d350 | roteia por TIPO: **tipo 1 → tabela PLAYER `+0x1d20`**; tipo 2 → NPC `+0x1d70` |

### O dispatcher HandleMessage (0x307–0x312)
`msgtype - 0x307`, `cmp <= 0xb`, `jmp [tabela@0x3610e280]`. Casos mapeados:
- **0x307** (create general NPC) → `AddRemoteGeneralNpc`
- **0x30c** (= nosso alive-flag `0x830c`) → 0x3610db7f → parseia `[A][len][C][D][u32][u32][blob]` →
  chama o aplicador **0x3610d350** (roteia p/ a tabela PLAYER `+0x1d20` quando tipo=1)
- 0x30d/0x30e/0x311 → default (não tratados aqui; 0x311 golpe é unreliable, tratado noutra via)

**Confirmação-chave:** nossa síntese do `0x830c` alive-flag é **byte-idêntica** ao que o joiner humano
manda na captura (l.285: `0c83…0a0a010a002a0091010400000001000000`) — o teste `AliveFlagDatagram_*`
trava isso. Ou seja: **o formato NÃO é o problema.**

## Os DOIS muros (por que ainda não conta)

### Muro 1 — a entrega reliable (validação de peer/sequência)
Se a captura humano↔humano usa **exatamente** os mesmos bytes e lá o HIT×N funciona, mas o nosso
(byte-idêntico) não, a diferença está na **camada de entrega reliable**, não no conteúdo:
- As msgs `0x83xx` são **reliable** (bit 0x8000) — o host mantém **sequência + ACK por-peer** e
  **valida o remetente** antes de entregar ao `HandleMessage`.
- Nós mandamos o `0x830c` como datagrama avulso **relayado pelo servidor** (origem = socket do
  servidor), com **seq compartilhada com os 0x30a** (unreliable). O host provavelmente **descarta**
  antes do dispatcher: sequência reliable fora de ordem / peer não-validado.
- O lockstep `0x0304/0x0305` que fechamos é o **transporte** reliable (open/push/ack); o `0x830c` é
  uma **mensagem de aplicação** reliable que precisa entrar NESSA janela com seq/ack próprios —
  subsistema que ainda não sintetizamos (só o keepalive do canal).

### Muro 2 — o create do PLAYER real é código empacotado
`AddRemotePlayer` (0x3610e2b0) **não tem caller dentro da engine.dll** — é invocado pela `gamemp/
entitiesmp` EMPACOTADAS (o mesmo muro do headless §12). Nosso `0x4b` cria um avatar que **anda +
colide** (por isso a repulsão), mas provavelmente uma entidade **local-física**, não o **peer remoto
cinemático** que `AddRemotePlayer` produz — e é o peer remoto que entra na tabela `+0x1d20` como
**combatente hittável** (ENF_ALIVE + team + HP, o gate do `AddHitCount`, cell-monster-re §5).

## Conclusão honesta
A promoção via endereço no roster foi real e mensurável (colisão física + CP). Mas o **HIT×N nativo**
exige a entidade do bot ser um **combatente de sessão completo** na tabela `+0x1d20`, e as duas vias
para isso batem em muro:
1. **Injetar o estado de combate** via a msg reliable `0x30c` → precisa reversar o **subsistema de
   mensagens reliable** (seq/ack/validação de peer) p/ o host aceitar nossos frames. Sub-projeto.
2. **Criar o peer real** via `AddRemotePlayer` → código empacotado. Mesmo muro do headless.

Nenhum é um ajuste de bytes; ambos são frentes de investigação próprias. O que ESTÁ entregue e
funcional: bot que anda (0x30a nativo), colide (entidade física), leva dano/morre/respawna
(arbitragem server-side), placar/vitória. O contador nativo e a animação de golpe ficam atrás desses
dois muros.

## O gate EXATO (cell-monster-re §5) — por que a entidade física não basta
`AddHitCount` roda DENTRO da `ReceiveDamage` da vítima, no cliente do ATACANTE, só se **todos** os
gates passarem — e são campos de ESTADO da entidade (entitiesmp, empacotado):

| gate | campo | o bot promovido tem? |
|---|---|---|
| `ENF_ALIVE` | flags +0x10 | **incerto** (setado no init de CPlayer real) |
| não-morrendo | +0x638 == 0 | ? |
| **não-template** | **+0x664 != 1** | **SUSPEITO**: avatar sintético pode nascer template/placeholder → `ReceiveDamage` sai na hora |
| ativo | +0x394 != 0 | ? |
| **team inimigo** | **+0x26c ∈ {0x0a,0x14}**, 0 FALHA | **SUSPEITO**: 0x4b não seta team explícito → 0/neutro → IsEnemy falha |
| HP > 0 | +0x624 | ? |

A **repulsão prova só a colisão de MOVIMENTO** — nenhum desses gates de COMBATE. Os campos são
inicializados no path de CPlayer real (via `AddRemotePlayer`, empacotado) — nosso `0x4b` cria o corpo
mas não roda esse init, então team/alive/template/active ficam por sorte. É por isso que o corpo
existe (repele) mas o raycast do golpe, mesmo achando o corpo, **sai no primeiro gate** (template ou
team=0) → zero HIT×N. **Não há pacote de contador a mandar; falta o ESTADO de combatente na entidade.**

## Se retomar — próximo experimento mais barato
Testar se o `0x830c` alive-flag é **aceito** pelo host: instrumentar (server-side) se o host **ecoa/
reage** ao nosso `0x830c` (como reage ao de outro humano). Se NÃO reage → confirma o Muro 1 (entrega
reliable). O sinal de sucesso seria o host mandar de volta um `0x830c`/`0x8315` referente ao seat do
bot logo após o nosso.
