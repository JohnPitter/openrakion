# Subsistema de Bots — peer sintético server-side

O bot é um peer sintético controlado pelo World: ocupa um assento do field, aparece no roster,
recebe movimento e animações no formato do cliente v258 e possui HP, morte e respawn autoritativos.
A DLL não implementa regra de combate. Ela apenas espelha ao World, pelo envelope autenticado
`0xB07A`, o movimento `0x030A` e o início do ataque local `0x0311` que o cliente normalmente envia
somente aos peers.

O retorno do bot usa a rota real de cada humano: `0x57` via TCP quando há tunneling e datagrama UDP
para uma rota direta autenticada. O servidor nunca inventa endpoint para o bot e não interfere no
canal humano↔humano.

## Limite confirmado no cliente

A entidade remota sintética não percorre o mesmo caminho local de colisão/dano de um peer real.
Por isso a colisão nativa não pode decidir o HP do bot. O World continua sendo a única autoridade:
ele valida alvo, alcance, cone, cooldown, dano, morte e respawn. Depois de cada acerto confirmado,
a DLL apenas chama, na thread do jogo, as rotinas nativas de reação e contador **HIT×N**. A DLL não
transforma uma animação local em dano e não altera HP.

Para manter o bot funcional para lançamento, o World resolve o golpe a partir de dados que o
cliente realmente fornece:

- início do ataque do humano (`0x0311 kind=Attack`);
- posição e rumo mais recentes do atacante (`0x030A`);
- bots inimigos vivos do field;
- alcance, cone frontal, alvo mais próximo e cooldown antirrepetição.

Esse contrato não transforma toda animação em dano: no máximo um bot inimigo é escolhido, precisa
estar a até 600 unidades wire e dentro de um cone de 150 graus centrado no rumo do atacante. O
cooldown de 250 ms elimina emissões duplicadas do hook durante o mesmo golpe.

## Camadas

| Camada | Arquivos | Responsabilidade |
|---|---|---|
| Domínio/IA | `Domain/BotProfile.cs`, `BotVector.cs`, `BotSteering.cs`, `BotPlayer.cs` | dificuldade, perseguição, HP, reação, morte e respawn |
| Combate | `Domain/BotCombat.cs` | alvo único, time, alcance, cone frontal, cooldown e dano |
| Field | `Domain/Field.Bots.cs` e `PlayerRec` | assentos, pose humana e estado efêmero do bot |
| Serviço | `BotManager.cs`, `BotManager.Tick.cs`, `WorldServer.ResolveBotMeleeAttack` | tick, feedback visual, lifecycle e placar |
| Rede | `Network/BotMovement.cs`, `BotTelemetryDatagram.cs`, `UdpGameplay.cs` | síntese, validação e transporte |
| Cliente | `client/RakionClientCompat/bot_telemetry.cpp`, `rakion_client_patch.cpp` | espelho de movimento/ataque e apresentação do resultado confirmado, sem regra de dano |

## Fluxo

1. `BotManager.AddBotToField` aceita somente o host, antes da partida e em sala competitiva. O bot
   entra no time oposto, ocupa um assento do bloco `0..9` ou `10..19` e já fica pronto.
2. O roster recebe o member-join `0x38`; o primeiro snapshot `0x4B` humano é replicado com o seat do
   bot para o engine criar a entidade no stage.
3. A cada 150 ms em partida, `BotManager.TickField` encontra o humano inimigo vivo mais próximo,
   avança a IA e sintetiza o movimento `0x030A` e o keystate `0x030F`. Nas transições ele também
   publica `0x0311 kind=Normal`: `MoveForward` ao começar a andar e `Stand` ao parar.
4. Ao atacar, a DLL envia o `0x0311` local dentro do `0xB07A`. O World autentica endpoint e seat,
   aplica rate limit e chama `BotCombat.TryResolveMeleeAttack`.
5. Em um acerto, o World zera velocidade e alvo da IA, publica `0x030A`/`0x030F` de parada e a
   reação `0x0311 kind=Damage` com o shape real `0F 07 <attackerSeat>`. Ele grava uma sequência monotônica de dano por cliente UDP; a DLL
   executa a reação nativa e incrementa HIT somente para o atacante confirmado. A IA permanece
   suspensa por 1,8 s, portanto o bot não persegue nem desliza enquanto está caído. Ao terminar a
   janela, o World emite `Rise` antes de voltar a aceitar movimento.
6. Ao zerar o HP, `Field.ApplyReportedDeath` liquida exatamente um kill, publica `0x4F`, a DLL chama
   o lifecycle nativo de morte e o bot permanece sem movimento até o respawn. Deathmatch, Team
   Death e Boss usam respawn autoritativo de sete segundos; Golem segue eliminação por round.
7. No fim da partida ou na saída do último humano do gameplay, o field retorna ao game room. Os
   humanos voltam a não-pronto, o bot é revivido, tem IA e pose efêmeras zeradas e permanece
   pronto para o rematch. A liderança da sala só muda quando o master realmente deixa a sala.

## Movimento e animação

O RE de `CPlayerSource::SendAction @ engine.dll+0x103940` e
`CSessionState::GetActionFromMessage @ engine.dll+0x10AFE0` fecha estes invariantes:

- `0x030A+9` compacta `seat` nos cinco bits baixos e `PlayerActionState` nos bits 5–6. `0x20`
  significa `Attack`, não deslocamento. Caminhar usa estado `Normal`;
- o receiver reconhece 32 valores de `ePlayerAction`, mas o produtor v258 grava explicitamente
  `actionCode=0` em `CPlayerSource::SendAction`. Locomoção não deve ser inventada nesse byte;
- `0x030A+17` é o heading absoluto nativo em graus inteiros. Não é um ângulo normalizado para toda
  a faixa de `i16`. A frente visual do modelo é o vetor oposto ao ângulo wire: nas capturas em que
  `MoveForward` estava ativo, o deslocamento ficou concentrado em `heading ± 180°`. O codec aplica
  essa inversão na entrada e na saída; o domínio continua trabalhando com a direção visual real;
- `0x030A+20/+22/+24` são deltas acumuláveis de câmera e permanecem zerados no bot;
- o snapshot idle capturado de `0x030F` termina em `00 03`; o de caminhada termina em `00 01`.
  Inverter esses dois bytes impede o animator remoto de entrar no estado correto.

O switch `ExecNormalAnim @ 0x3513E570` e duas capturas reais fecharam os IDs usados neste fluxo:

| ID | animação |
|---:|---|
| `01` | `Stand` |
| `02` / `03` | `idle01` / `idle02` |
| `04`..`0B` | frente, trás, esquerda, direita e quatro diagonais |
| `0C` | `Jump` |
| `0E` | `Rise` |
| `0F` / `10` | `RollFront` / `RollBack` |
| `11` / `12` | `Guard` / `Struck_Guard` |
| `13`..`1A` | oito direções em guarda |
| `1B` / `1C` | troca para arma 1 / 2 |

O valor `0D` não possui case no switch dessa build. Queda/dano não deve ser simulada com esse ID:
ela entra por `0x0311 kind=Damage`, cujo payload humano observado foi `0F 07 <attackerSeat>`.

`MoveForward` também não é um comando permanente. Nas capturas humanas ele reaparece durante a
caminhada, normalmente a cada 1–1,5 s. O World o renova a cada 1,2 s enquanto há velocidade e
publica `Stand` ao parar; emitir só na primeira transição deixa a animação acabar e o avatar deslizar.

Antes de cada reação de dano, o servidor publica `0x030A`, `0x030F` e `Stand`. O tick de
IA fica suspenso durante a queda; morto também não produz ação até o respawn. Assim, movimento
novo não sobrescreve a reação nativa.

O lifecycle usa um snapshot isolado por porta UDP autenticada. A DLL o consome na thread do jogo e
aplica `ExecDamageAnim`, `SetDead`, `SetAlive` e `AddHitCount` somente após a confirmação do World.
Assim, salas simultâneas não compartilham o estado visual de bots.

O ataque do bot alterna as três animações observadas (`0x1B`, `0x1A`, `0x12`) e usa cooldown por
dificuldade. O dano bot→humano, salto com física, colisão nativa, queda e morte integralmente
dirigidas pelo engine continuam fora do modelo sintético: o cliente exige um peer real para esse
pipeline. O caminho de lançamento para equivalência completa é um cliente v258 controlado pela IA;
o peer sintético permanece como fallback até esse caminho passar no gate visual.

## Captura reproduzível das ações

A DLL possui captura opt-in do produtor real. Inicie o launcher com
`OPENRAKION_CAPTURE_ACTIONS=1` ou crie `Bin/action.capture`; cada processo grava em
`%TEMP%\openrakion_action_capture_<pid>.csv` os payloads `0x030A`, `0x030F` e `0x0311` antes do
transporte P2P. A sequência mínima de captura é idle, avançar, recuar, strafe, giro, salto,
aterrissagem, ataque básico, ataque especial, guarda, dano, queda, levantar, morte e respawn.
Os bytes capturados são a golden source para o driver do cliente real; não se inferem botões ou
animações por tentativa e erro.

Como referência estrutural, o código público do Serious Engine mostra o fluxo
`CControls::CreateAction → CPlayerSource::SetAction → CPlayerSource::SendAction`. Rakion v258
estende a estrutura e o wire, portanto a referência serve para localizar responsabilidades, não
para copiar offsets ou valores. Fontes: [Serious Engine oficial](https://github.com/Croteam-official/Serious-Engine)
e [anúncio da Croteam](https://www.croteam.com/serious-sam-source-code-released/).

## Testes

- `BotCombatTests`: alvo inimigo mais próximo, rumo, cone frontal, alcance, time, cooldown, dano e
  morte de um único bot.
- `BotRespawnTests`: parada durante a reação, lifecycle, prazo e restauração do respawn.
- `BotMovementSynthTests`: formato, pose/heading, ausência de deltas de câmera e reação estendida.
- `E2E/BotStageValidationTests`: ataque `0x0311` autenticado reduz HP, publica parada/reação, mata o
  bot com `0x4F` e funciona também por tunneling TCP.
- `E2E/BotRematchE2ETests`: dois clientes e um bot saem do gameplay, reencontram a sala no filtro
  Available e iniciam outra partida no mesmo game room.
- `E2E/AddBotButtonCommandE2ETests` e `BotMovementE2ETests`: comando, roster e movimento no fio.

## Estado de validação

Build e testes automatizados validam protocolo e regra de negócio. Em 22/07/2026, o RE corrigiu o
estado `Attack` indevido no movimento, heading em escala e eixo errados, bytes invertidos do
`0x030F`, renovação ausente de `MoveForward` e reação genérica no lugar do payload humano
`0F 07 <attackerSeat>`.
O gate final permanece visual:
confirmar no cliente gráfico que cada golpe frontal próximo reduz HP, que a queda interrompe a
perseguição, que o bot morre uma vez, incrementa um kill e só volta a andar após o respawn.
