# Subsistema de Bots — peer sintético server-side

O bot é um peer sintético controlado pelo World: ocupa um assento do field, aparece no roster,
recebe movimento e animações no formato do cliente v258. HP, morte e respawn sintéticos existem no
domínio, mas não são alterados por uma mera animação de ataque.
A DLL não implementa regra de combate. Ela apenas espelha ao World, pelo envelope autenticado
`0xB07A`, o movimento `0x030A` e o início do ataque local `0x0311` que o cliente normalmente envia
somente aos peers.

O retorno do bot usa a rota real de cada humano: `0x57` via TCP quando há tunneling e datagrama UDP
para uma rota direta autenticada. O servidor nunca inventa endpoint para o bot e não interfere no
canal humano↔humano.

## Limite confirmado no cliente

A entidade remota sintética não percorre o mesmo caminho local de colisão/dano de um peer real.
Por isso ela não produz a confirmação de impacto que um cliente-vítima real enviaria. O contrato
anterior que aproximava impacto por alcance e cone foi removido: fazia o bot perder HP ao carregar
`kind=Attack`, antes de qualquer contato. Sem cliente-vítima ou evento nativo de colisão comprovado,
o fallback sintético permanece invulnerável. Movimento humano autenticado ainda atualiza a pose,
mas `0x0311 kind=Attack` nunca chama `TakeDamage`, publica HIT ou gera morte.

## Camadas

| Camada | Arquivos | Responsabilidade |
|---|---|---|
| Domínio/IA | `Domain/BotProfile.cs`, `BotVector.cs`, `BotSteering.cs`, `BotPlayer.cs` | dificuldade, perseguição, HP, reação, morte e respawn |
| Field | `Domain/Field.Bots.cs` e `PlayerRec` | assentos, pose humana e estado efêmero do bot |
| Serviço | `BotManager.cs`, `BotManager.Tick.cs` | tick, movimento e lifecycle |
| Rede | `Network/BotMovement.cs`, `BotTelemetryDatagram.cs`, `UdpGameplay.cs` | síntese, validação e transporte |
| Cliente | `client/RakionClientCompat/bot_telemetry.cpp`, `rakion_client_patch.cpp` | espelho de movimento/ataque e apresentação do resultado confirmado, sem regra de dano |

## Fluxo

1. `BotManager.AddBotToField` aceita somente o host, antes da partida e em sala competitiva. O bot
   entra no time oposto, ocupa um assento do bloco `0..9` ou `10..19` e já fica pronto.
2. O roster recebe o member-join `0x38`; o primeiro snapshot `0x4B` humano é replicado com o seat do
   bot para o engine criar a entidade no stage.
3. A cada 150 ms em partida, `BotManager.TickField` encontra o humano inimigo vivo mais próximo,
   avança a IA e sintetiza o movimento `0x030A` e o keystate `0x030F`.
4. Nas transições, o World publica o estado de locomoção no snapshot por porta UDP. Na thread de
   `CPlayer`, a DLL chama `ExecNormalAnim(4)` ao andar e `ExecNormalAnim(1)` ao parar, uma vez.
5. `0x0311 kind=Attack` pode ser espelhado para diagnóstico, mas não altera HP ou lifecycle.
6. No fim da partida ou na saída do último humano do gameplay, o field retorna ao game room. Os
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

`MoveForward` é publicado quando o bot passa de parado para andando, e `Stand` na transição
inversa. Enquanto há controle de locomoção, o World renova a animação a cada 800 ms. Essa cadência
acompanha as repetições de `kind=Normal` observadas na captura humana e evita que o avatar remoto
volte a `Stand` enquanto os snapshots de posição continuam chegando.

O lifecycle usa um snapshot isolado por porta UDP autenticada. A DLL o consome na thread do jogo;
o mesmo canal transporta a transição de locomoção. Dano, HIT e morte só poderão ser ativados
novamente quando houver uma fonte de impacto diferente de `kind=Attack`.

## Navegação server-side

O bot sintético possui um planejador stateful no domínio, separado da rede e da persistência. Ele
produz controles `W/A/S/D/Space/Attack`, e não posições arbitrárias. A cinemática converte esses
controles em velocidade, heading e posição; o adaptador de rede continua responsável apenas por
serializar `0x030A`, `0x030F` e `0x0311`.

Quando a distância deixa de melhorar, o planner tenta diagonal com pulo, strafe e recuo lateral. A
direção de contorno permanece fixa no mundo enquanto o avatar continua olhando o alvo, evitando a
inversão de `A/D` observada quando dois peers cruzam o mesmo eixo. O lado é trocado somente quando a
manobra não desloca o bot ou aumenta a distância.

`IBotNavigationSurface` é o contrato entre domínio e geometria do mapa. O primeiro perfil é Cage
(`mapId=209`): o obstáculo central usa o volume observado nas trajetórias nativas, expandido pelo
raio do avatar. A resolução é separada por eixo para bloquear entrada e permitir deslizamento
tangencial pela borda. Passos grandes também são barrados, impedindo tunneling. A colisão entra como
feedback explícito do planner e inicia o contorno imediatamente; ela nunca libera movimento direto
como fallback. Mapas ainda não catalogados usam superfície aberta. O golden do Cage exige bloqueio,
`Space`, strafe, saída do volume sólido antes do cruzamento e retorno ao alcance de ataque.

As combinações são convertidas para as animações capturadas do humano: frente, trás, esquerda,
direita, quatro diagonais e pulo. A trajetória vertical é calculada no World, mas ainda precisa do
gate visual para calibrar altura e gravidade contra cada mapa.

O ataque do bot alterna as três animações observadas (`0x1B`, `0x1A`, `0x12`) e usa cooldown por
dificuldade. O dano bot→humano, queda e morte integralmente dirigidas pelo engine continuam fora do
modelo sintético: o cliente exige confirmação visual adicional para esse pipeline. O headless
permanece como golden source de equivalência; o peer sintético é o caminho escalável e conserva a
apresentação mínima da DLL enquanto esse fluxo não for aceito nativamente pelo cliente.

## Captura reproduzível das ações

A DLL possui captura opt-in do produtor real. Inicie o launcher com
`OPENRAKION_CAPTURE_ACTIONS=1` ou crie `Bin/action.capture`; cada processo grava em
`%TEMP%\openrakion_action_capture_<pid>.csv` os payloads `0x030A`, `0x030F` e `0x0311` antes do
transporte P2P. A sequência mínima de captura é idle, avançar, recuar, strafe, giro, salto,
aterrissagem, ataque básico, ataque especial, guarda, dano, queda, levantar, morte e respawn.
Os bytes capturados são a golden source para o driver do cliente real; não se inferem botões ou
animações por tentativa e erro.

No mesmo modo opt-in, a DLL intercepta `CPlayerSource::SetAction` somente depois de validar o
prólogo da função e grava `%TEMP%\openrakion_player_action_<pid>.csv`. Cada linha contém o relógio,
o endereço da fonte local e os 72 bytes de `CPlayerAction` produzidos pelo próprio engine. Essa
captura correlaciona os controles locais com os pacotes de rede sem alterar a ação aplicada. Se a
assinatura da build não corresponder, o hook falha fechado e o cliente segue sem instrumentação.

Como referência estrutural, o código público do Serious Engine mostra o fluxo
`CControls::CreateAction → CPlayerSource::SetAction → CPlayerSource::SendAction`. Rakion v258
estende a estrutura e o wire, portanto a referência serve para localizar responsabilidades, não
para copiar offsets ou valores. Fontes: [Serious Engine oficial](https://github.com/Croteam-official/Serious-Engine)
e [anúncio da Croteam](https://www.croteam.com/serious-sam-source-code-released/).

## Testes

- `BotRespawnTests`: parada durante a reação, lifecycle, prazo e restauração do respawn.
- `BotMovementSynthTests`: formato, pose/heading, ausência de deltas de câmera e reação estendida.
- `E2E/BotStageValidationTests`: `kind=Attack` autenticado não reduz HP nem por UDP direto nem por
  tunneling; movimento e perseguição continuam no fio.
- `E2E/BotRematchE2ETests`: dois clientes e um bot saem do gameplay, reencontram a sala no filtro
  Available e iniciam outra partida no mesmo game room.
- `E2E/AddBotButtonCommandE2ETests` e `BotMovementE2ETests`: comando, roster e movimento no fio.

## Estado de validação

Build e testes automatizados validam protocolo e regra de negócio. Em 22/07/2026, o RE corrigiu o
heading em escala/eixo errados e moveu a animação de locomoção para a thread nativa do jogador.
Em 26/07/2026, o golden server-side do Cage passou usando controles, colisão, contorno e pulo. O
primeiro gate visual confirmou spawn, mas revelou animação sem renovação e passagem por obstáculo
quando o contorno falhava. O World passou a renovar a locomoção e a usar volume sólido com reação
imediata à colisão. O gate visual dessas duas correções ainda está pendente. HIT, queda por dano e
morte ainda não são considerados implementados no peer sintético.
