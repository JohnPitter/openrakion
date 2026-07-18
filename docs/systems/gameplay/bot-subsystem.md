# Subsistema de Bots (peer sintético server-side) — reconstruído do RE

Bot **funcional** reconstruído do zero sobre o motor de partida do golden, ancorado no RE. A
implementação antiga (do master pré-golden) foi descartada. O bot é um **peer sintético
server-side**: entra no roster como um jogador, é movido pela IA e tem o movimento sintetizado no
fio. Nos testes headless, o ataque chega pela extensão UDP do World; no cliente gráfico, a DLL de
compatibilidade captura movimento e ataque na entrada de `CNet::SendToOtherClient` e os espelha ao
World pelo envelope `0xB07A`, sem duplicar o pacote entregue aos peers e sem exigir um peer humano.

## Veredito do RE (respeitado, não contornado)

O bot server-side entrega um oponente **FUNCIONAL** — aparece no roster, anda na tela do humano,
leva dano, reage visualmente ao golpe e morre com placar server-side. Ele **não** produz o número
cosmético **HIT×N** nativo: o
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

## Combate (server-side, dentro do teto RE)

O servidor é a **autoridade do HP do bot** (`BotPlayer.Health`, curva `100 + level*10`). O que é
entregue e o que é teto:

- **Bot como VÍTIMA (funcional no canal World)**: quando o World recebe um ataque de melee humano
  (`0x0311` kind=Attack),
  o `UdpGameplay` chama `WorldServer.ResolveBotMeleeAttack` → `BotCombat.ResolveMeleeAttack` aplica
  dano aos bots inimigos vivos no alcance (posições rastreadas). A cada acerto, o servidor devolve
  um `0x0311 kind=Damage` estendido com o assento do bot e segura a IA por 300 ms, tornando a reação
  visível no cliente. Ao zerar o HP, a morte é liquidada
  pelo **`Field.ApplyReportedDeath`** do modo e transmitida com **`0x4f`** aos humanos: o atacante
  recebe o kill/pontos. A detecção autoritativa é por ataque, time, estado e proximidade no servidor;
  o cliente apresenta a reação, mas não computa o número cosmético HIT×N (gate `[+0x394]`, limite
  type-7).
- **Bot como ATACANTE (cosmético)**: no melee, o tick sintetiza a animação de ataque (`0x0311`
  kind=Attack) para os humanos verem o bot golpear. O dano bot→humano é **client-authoritative** —
  o cliente do humano não processa dano de um peer sintético, então é apresentação, não dano real.
  Esse é o teto arquitetural do RE, não um bug.

## Cobertura de testes

- `BotSteeringTests` (7): perseguição, orbita no melee, aceleração (sem teleporte), antecipação por
  dificuldade, tick do `BotPlayer`.
- `BotManagerTests` (6): add pelo host no time oposto, gates (não-host / em-jogo / solo), limpeza,
  time cheio.
- `BotMovementSynthTests` (4): formato do 0x030A, assento na origem, roundtrip da posição e reação
  estendida `0x0311 kind=Damage`.
- `E2E/BotMovementE2ETests` (1): no fio, um humano recebe o `0x030A` sintetizado do bot com a
  partida em jogo.
- `E2E/BotStageValidationTests`: o primeiro golpe reduz HP e retorna `0x0311 kind=Damage` ao humano;
  golpes seguintes matam o bot e publicam `0x4F` com o assento correto.

## Fronteira (o teto do RE, não um bug)

Entregue e provado headless: roster, movimento, reação de dano, HP/morte, dificuldade/IA e ponte
P2P→World da DLL. A apresentação no cliente gráfico ainda exige smoke visual. **Não** entregue por limite
arquitetural: o número cosmético HIT×N nativo (exige peer de sessão real). Extensões possíveis
(hitbox mais precisa, projéteis e dano bot→humano) seguem o mesmo teto sem um peer simulado pelo
engine. Ver o cluster HIT×N na memória do projeto.
