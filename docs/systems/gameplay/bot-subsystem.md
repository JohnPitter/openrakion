# Subsistema de Bots (peer sintético server-side) — reconstruído do RE

Bot **funcional** reconstruído do zero sobre o motor de partida do golden, ancorado no RE. A
implementação antiga (do master pré-golden) foi descartada. O bot é um **peer sintético
server-side**: entra no roster como um jogador, é movido pela IA e tem o movimento sintetizado no
fio — sem nenhuma DLL injetada e sem falar direto com o cliente.

## Veredito do RE (respeitado, não contornado)

O bot server-side entrega um oponente **FUNCIONAL** — aparece no roster, anda na tela do humano,
e (extensão) leva/dá dano server-side. Ele **não** produz o número cosmético **HIT×N** nativo: o
gate `ReceiveDamage@0x3518ce40` exige `[vítima+0x394]!=0`, que só é setado numa entidade **simulada
em combate localmente** = um peer de sessão real sincronizado (limite type-7, confirmado e não
re-tentado). Regras invioláveis herdadas do RE:

- **Nenhum pacote do bot fala direto com o cliente** — só via o socket do `UdpGameplay`.
- **O servidor nunca sequestra o canal humano↔humano** — só emite o tráfego DO bot (sem relay do
  P2P humano). Evita a dupla-entrega que matava o seq/ack reliable.
- Ciclo **efêmero**: bots somem no fim do match ou quando o último humano sai (field liberado).

## Camadas

| Camada | Arquivos | Papel |
|---|---|---|
| Domínio/IA (puro) | `Domain/BotProfile.cs`, `BotVector.cs`, `BotSteering.cs`, `BotPlayer.cs` | dificuldade Easy/Normal/Hard, perseguição/orbita-melee/antecipação (EMA), estado do bot |
| Field | `Domain/Field.Bots.cs` (+ `PlayerRec.Bot`/`Position`) | assentos de bot, `AddBot`/`RemoveAllBots`, `BotSlots` |
| Serviço | `BotManager.cs`, `BotManager.Tick.cs` | add host-only/time-oposto/pré-match, lifecycle, tick de IA→síntese |
| Rede | `Network/RoomRosterFrames.cs` (bot record), `BotMovement.cs` | member-join 0x38 do bot, síntese do 0x030A |
| Comando | `WorldHandlers.Field.cs` (`/addbot`) | `/addbot [facil\|normal\|dificil] [n]`, feedback ao host |

## Fluxo

1. **Add** (`BotManager.AddBotToField`): só o host, só antes da partida, só em sala competitiva
   (mode != 0). O bot entra no **time oposto** ao host, num assento livre do bloco 0..9 / 10..19,
   já READY (não trava o start). Sincroniza o roster do cliente com um **member-join 0x38** cujo
   registro é o do bot (`RoomRosterFrames.WriteBotRecord`, endpoint zerado).
2. **Em jogo** (`BotManager.TickField`, chamado pelo game-clock a 150 ms quando `Field.State==2`):
   para cada bot, mira o **humano inimigo vivo mais próximo** (posição rastreada do `0x030A` dele,
   lida em `UdpGameplay.RelayToField`), avança a IA (`BotSteering`) e **sintetiza o `0x030A` do
   bot** (`BotMovement.SynthesizeMove`, origem = assento do bot), injetando-o aos peers humanos via
   `UdpGameplay.SendGameplayDatagram`. O servidor é a **fonte**; não há relay do bot.
3. **Cleanup**: fim de match / último humano sai → `RemoveAllBots`.

## Cobertura de testes

- `BotSteeringTests` (7): perseguição, orbita no melee, aceleração (sem teleporte), antecipação por
  dificuldade, tick do `BotPlayer`.
- `BotManagerTests` (6): add pelo host no time oposto, gates (não-host / em-jogo / solo), limpeza,
  time cheio.
- `BotMovementSynthTests` (3): formato do 0x030A, assento na origem, roundtrip da posição.
- `E2E/BotMovementE2ETests` (1): no fio, um humano recebe o `0x030A` sintetizado do bot com a
  partida em jogo.

## Fronteira (o teto do RE, não um bug)

Entregue: roster, movimento na tela do humano, dificuldade/IA. **Não** entregue por limite
arquitetural: o número cosmético HIT×N nativo (exige peer de sessão real). Extensões possíveis
(dano/morte server-side do bot, spawn 0x307 hittável) seguem o mesmo teto — funcionais, sem o HUD
nativo do combo. Ver o cluster HIT×N na memória do projeto.
