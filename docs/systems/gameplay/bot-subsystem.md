# Subsistema de Bots (peer sintético server-side) — reconstruído do RE

Bot **funcional** reconstruído do zero sobre o motor de partida do golden, ancorado no RE. A
implementação antiga (do master pré-golden) foi descartada. O bot é um **peer sintético
server-side**: entra no roster como um jogador, é movido pela IA e tem o movimento sintetizado no
fio. No cliente gráfico, a DLL de compatibilidade espelha apenas o movimento local ao World pelo
envelope `0xB07A`. O dano usa um contrato separado, `0xB07B`: ele só é emitido no ponto em que a
engine confirmou a colisão com o bot, contendo sequência antirreplay e o assento exato da vítima.
O retorno do bot segue a rota real de cada humano: `0x57` via TCP para clientes com tunneling e
datagrama UDP somente para peers com rota direta.
No movimento, `0x030A+17` carrega o heading absoluto. Os words `+20/+22/+24` permanecem zerados:
eles são deltas acumuláveis de câmera e repetir o heading nesse trio faz o modelo girar no próprio
eixo a cada tick.
No túnel, o World remove o cabeçalho P2P `[sequence:u32][transportSource:u8]`: movimento cru de
26 bytes vira a mensagem nativa de 21 bytes, e a reação de dano de 12 bytes vira 7 bytes. Repassar
o datagrama cru dentro do `0x57` é inválido e o engine gráfico o ignora.

## Veredito do RE (respeitado, não contornado)

O bot server-side entrega um oponente **FUNCIONAL** — aparece no roster e no stage, recebe os
ataques do humano e possui HP/morte autoritativos. Ele **não** produz o número
cosmético **HIT×N** nativo: o
gate `ReceiveDamage@0x3518ce40` exige `[vítima+0x394]!=0`, que só é setado numa entidade **simulada
em combate localmente** = um peer de sessão real sincronizado (limite type-7, confirmado e não
re-tentado). Regras invioláveis herdadas do RE:

- **Nenhum pacote do bot fala por um endpoint P2P inventado** — o World escolhe o fallback TCP ou a
  rota UDP autenticada de cada humano.
- **O servidor nunca sequestra o canal humano↔humano** — só emite o tráfego DO bot (sem relay do
  P2P humano). Evita a dupla-entrega que matava o seq/ack reliable.
- Ciclo **efêmero**: bots somem no fim do match ou quando o último humano sai (field liberado).

## Camadas

| Camada | Arquivos | Papel |
|---|---|---|
| Domínio/IA (puro) | `Domain/BotProfile.cs`, `BotVector.cs`, `BotSteering.cs`, `BotPlayer.cs` | dificuldade Easy/Normal/Hard, perseguição/frenagem-melee/antecipação (EMA), estado do bot |
| Field | `Domain/Field.Bots.cs` (+ `PlayerRec.Bot`/`Position`) | assentos de bot, `AddBot`/`RemoveAllBots`, `BotSlots` |
| Serviço | `BotManager.cs`, `BotManager.Tick.cs` | add host-only/time-oposto/pré-match, lifecycle, tick de IA→síntese |
| Rede | `Network/RoomRosterFrames.cs`, `BotMovement.cs`, `BotHitTelemetryDatagram.cs` | roster, síntese do 0x030A e confirmação autenticada de hit |
| Comando | `WorldHandlers.Field.cs` (`/addbot`) | `/addbot [facil\|normal\|dificil] [n]`, feedback ao host |

## Fluxo

1. **Add** (`BotManager.AddBotToField`): só o host, só antes da partida, só em sala competitiva
   (mode != 0). O bot entra no **time oposto** ao host, num assento livre do bloco 0..9 / 10..19,
   já READY (não trava o start). Sincroniza o roster do cliente com um **member-join 0x38** cujo
   registro é o do bot (`RoomRosterFrames.WriteBotRecord`, endpoint sintético marcado).
   No cliente v258, o botão **Add Bot** envia o chat de sala `0x47` com
   `<nome> : /addbot`; o handler aceita esse comando ainda em `FieldLobby`, sem desconectar a sessão.
   O endpoint loopback com porta-marcador `1183` identifica o peer sintético para a DLL, e o primeiro
   snapshot `0x4B` do humano é replicado com o seat do bot para o engine criar sua entidade no stage.
2. **Em jogo** (`BotManager.TickField`, chamado pelo game-clock a 150 ms quando `Field.State==2`):
   para cada bot, mira o **humano inimigo vivo mais próximo** (posição rastreada do `0x030A` dele,
   lida em `UdpGameplay.RelayToField`), avança a IA (`BotSteering`) e **sintetiza o `0x030A` do
   bot** (`BotMovement.SynthesizeMove`, origem = assento do bot), entregando-o aos peers humanos via
   `UdpGameplay.SendBotGameplay`. O servidor é a **fonte**; não há relay do bot. Em `ForceTunneling`,
   o payload nativo, sem o cabeçalho de transporte P2P, é encapsulado no `0x57`; no modo direto, o
   datagrama completo segue pelo endpoint UDP autenticado.
3. **Morte e respawn**: o lifecycle muda para morto e é aplicado pela DLL no game thread. Em
   Deathmatch, Team Death e Boss, o World agenda o respawn autoritativo de **7 segundos**, restaura
   HP/estado/IA e publica um novo lifecycle vivo; Golem continua seguindo eliminação por round.
4. **Cleanup**: fim de match / último humano sai → `RemoveAllBots`.

## Combate (server-side, dentro do teto RE)

O servidor é a **autoridade do HP do bot** (`BotPlayer.Health`, curva `100 + level*10`). O que é
entregue e o que é teto:

- **Bot como VÍTIMA (funcional no canal World)**: `0x0311 kind=Attack` é somente animação e **não
  prova hit**. O hook nativo intercepta a entrada de `CPlayer::ReceiveDamage`, antes dos gates que
  rejeitam uma entidade remota sintética, e aceita apenas uma vítima cujo seat tem lifecycle de bot
  e cujo atacante é o jogador local. A colisão confirmada envia `0xB07B(sequence,targetSeat)` ao
  endpoint autenticado do World. O servidor rejeita replay e valida partida, atacante vivo, assento
  exato, time e alcance em `ResolveConfirmedBotHit`/`BotCombat.TryApplyConfirmedHit`. A cada acerto,
  o servidor devolve
  um `0x0311 kind=Damage` estendido com o assento do bot e segura a IA por 1,1 s, permitindo que a
  animação de queda/recuperação termine antes do próximo movimento. O ataque do bot alterna as três
  animações observadas na captura humana (`0x1B`, `0x1A`, `0x12`) e usa cooldown de 2,2/1,7/1,3 s
  para Easy/Normal/Hard, respectivamente. Isso evita o loop acelerado do ataque básico.
  publicada ao cliente. Ao zerar o HP, a morte é liquidada
  pelo **`Field.ApplyReportedDeath`** do modo e transmitida com **`0x4f`** aos humanos: o atacante
  recebe o kill/pontos. A detecção autoritativa combina colisão da engine com as regras do servidor;
  o cliente apresenta a reação, mas não computa o número cosmético HIT×N (gate `[+0x394]`, limite
  type-7).
- **Bot como ATACANTE (cosmético)**: no melee, o tick sintetiza a animação de ataque (`0x0311`
  kind=Attack) para os humanos verem o bot golpear. O dano bot→humano é **client-authoritative** —
  o cliente do humano não processa dano de um peer sintético, então é apresentação, não dano real.
  Esse é o teto arquitetural do RE, não um bug.

## Cobertura de testes

- `BotSteeringTests`: perseguição, frenagem no melee, aceleração, antecipação, tick e convergência
  em até dez segundos na escala wire real (100 unidades wire = 1 unidade de mapa).
- `BotManagerTests` (6): add pelo host no time oposto, gates (não-host / em-jogo / solo), limpeza,
  time cheio.
- `BotHitTelemetryDatagramTests` e `BotCombatTests`: contrato `0xB07B`, alvo exato, time, alcance,
  dano e morte; a telemetria de animação `0x0311` isolada não altera HP.
- `BotRespawnTests`: política de 7 segundos e restauração de HP/lifecycle.
- `BotMovementSynthTests` (5): formato do 0x030A, assento/`sourceEcho`, roundtrip da posição,
  separação entre heading absoluto e deltas de câmera, e reação estendida `0x0311 kind=Damage`.
- `E2E/AddBotButtonCommandE2ETests` (1): envia exatamente `GoHeroi : /addbot` pelo `0x47` capturado
  do botão e confirma bot no time oposto sem derrubar o host da sala.
- `E2E/BotMovementE2ETests` (1): no fio, um humano recebe o `0x030A` sintetizado do bot com a
  partida em jogo.
- `E2E/BotStageValidationTests`: prova que animação isolada não reduz HP; o hit `0xB07B` reduz HP e
  retorna `0x0311 kind=Damage` ao humano;
  golpes seguintes matam o bot e publicam `0x4F` com o assento correto; também cobre movimento e
  reação de dano encapsulados em TCP `0x57` quando `ForceTunneling` está ativo.

## Fronteira (o teto do RE, não um bug)

Entregue e provado no cliente gráfico em 18/07/2026: botão Add Bot, roster, criação da entidade
`Rok` no stage Mammoth, captura do primeiro ataque pela DLL, HP `500→0` no World e aplicação nativa
dos lifecycles `alive seq=1` e `dead seq=2`. O fluxo novo de colisão real `0xB07B` e o lifecycle de
respawn passaram no build/test automatizado, mas ainda precisam da aprovação visual no cliente.
**Não** entregue por limite arquitetural: o número cosmético HIT×N nativo (exige peer de sessão real). Extensões possíveis
(hitbox mais precisa, projéteis e dano bot→humano) seguem o mesmo teto sem um peer simulado pelo
engine. Ver o cluster HIT×N na memória do projeto.
