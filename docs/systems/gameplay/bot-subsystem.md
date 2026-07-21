# Subsistema de Bots (peer sintético server-side) — reconstruído do RE

Bot **funcional** reconstruído do zero sobre o motor de partida do golden, ancorado no RE. A
implementação antiga (do master pré-golden) foi descartada. O bot é um **peer sintético
server-side**: entra no roster como um jogador, é movido pela IA e tem o movimento sintetizado no
fio. Nos testes headless, o ataque chega pela extensão UDP do World; no cliente gráfico, a DLL de
compatibilidade captura movimento e ataque na entrada de `CNet::SendToOtherClient` e os espelha ao
World pelo envelope `0xB07A`, sem duplicar o pacote entregue aos peers e sem exigir um peer humano.
O retorno do bot segue a rota real de cada humano: `0x57` via TCP para clientes com tunneling e
datagrama UDP somente para peers com rota direta.
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
| Domínio/IA (puro) | `Domain/BotProfile.cs`, `BotVector.cs`, `BotSteering.cs`, `BotPlayer.cs` | dificuldade Easy/Normal/Hard, perseguição/orbita-melee/antecipação (EMA), estado do bot |
| Field | `Domain/Field.Bots.cs` (+ `PlayerRec.Bot`/`Position`) | assentos de bot, `AddBot`/`RemoveAllBots`, `BotSlots` |
| Serviço | `BotManager.cs`, `BotManager.Tick.cs` | add host-only/time-oposto/pré-match, lifecycle, tick de IA→síntese |
| Rede | `Network/RoomRosterFrames.cs` (bot record), `BotMovement.cs` | member-join 0x38 do bot, síntese do 0x030A |
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
3. **Cleanup**: fim de match / último humano sai → `RemoveAllBots`.

## Combate (server-side, dentro do teto RE)

O servidor é a **autoridade do HP do bot** (`BotPlayer.Health`, curva `100 + level*10`). O que é
entregue e o que é teto:

- **Bot como VÍTIMA (funcional no canal World)**: quando o World recebe um ataque de melee humano
  (`0x0311` kind=Attack),
  o `UdpGameplay` chama `WorldServer.ResolveBotMeleeAttack` → `BotCombat.ResolveMeleeAttack` aplica
  dano aos bots inimigos vivos no alcance (posições rastreadas). A cada acerto, o servidor devolve
  um `0x0311 kind=Damage` estendido com o assento do bot e segura a IA por 300 ms, tornando a reação
  publicada ao cliente. Ao zerar o HP, a morte é liquidada
  pelo **`Field.ApplyReportedDeath`** do modo e transmitida com **`0x4f`** aos humanos: o atacante
  recebe o kill/pontos. A detecção autoritativa é por ataque, time, estado e proximidade no servidor;
  o cliente apresenta a reação, mas não computa o número cosmético HIT×N (gate `[+0x394]`, limite
  type-7).
- **Bot como ATACANTE (cosmético)**: no melee, o tick sintetiza a animação de ataque (`0x0311`
  kind=Attack) para os humanos verem o bot golpear. O dano bot→humano é **client-authoritative** —
  o cliente do humano não processa dano de um peer sintético, então é apresentação, não dano real.
  Esse é o teto arquitetural do RE, não um bug.

## Cobertura de testes

- `BotSteeringTests` (8): perseguição, orbita no melee, aceleração, antecipação, tick e convergência
  em até dez segundos na escala wire real (100 unidades wire = 1 unidade de mapa).
- `BotManagerTests` (6): add pelo host no time oposto, gates (não-host / em-jogo / solo), limpeza,
  time cheio.
- `BotMovementSynthTests` (4): formato do 0x030A, assento/`sourceEcho`, roundtrip da posição e reação
  estendida `0x0311 kind=Damage`.
- `E2E/AddBotButtonCommandE2ETests` (1): envia exatamente `GoHeroi : /addbot` pelo `0x47` capturado
  do botão e confirma bot no time oposto sem derrubar o host da sala.
- `E2E/BotMovementE2ETests` (1): no fio, um humano recebe o `0x030A` sintetizado do bot com a
  partida em jogo.
- `E2E/BotStageValidationTests`: o primeiro golpe reduz HP e retorna `0x0311 kind=Damage` ao humano;
  golpes seguintes matam o bot e publicam `0x4F` com o assento correto; também cobre movimento e
  reação de dano encapsulados em TCP `0x57` quando `ForceTunneling` está ativo.

## Fronteira (o teto do RE, não um bug)

Entregue e provado no cliente gráfico em 18/07/2026: botão Add Bot, roster, criação da entidade
`Rok` no stage Mammoth, captura do primeiro ataque pela DLL, HP `500→0` no World e aplicação nativa
dos lifecycles `alive seq=1` e `dead seq=2`. Ainda não foi aprovada visualmente a animação de dano,
o respawn, nem o HUD de kill (a captura permaneceu em `0 Kills`); esses itens continuam no Smoke 3.
**Não** entregue por limite arquitetural: o número cosmético HIT×N nativo (exige peer de sessão real). Extensões possíveis
(hitbox mais precisa, projéteis e dano bot→humano) seguem o mesmo teto sem um peer simulado pelo
engine. Ver o cluster HIT×N na memória do projeto.
