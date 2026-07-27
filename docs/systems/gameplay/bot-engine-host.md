# Bot Engine Host x86

## Objetivo

O caminho de lançamento de bots é um worker nativo x86 executado no servidor. Ele reutiliza a
`engine.dll`, a `entitiesmp.dll`, a `gamemp.dll` e os XFS da build v258 para física, colisão,
movimentação, animações e detecção espacial. O worker não inicia `rakion.exe`, não possui janela de
jogo e não admite o peer sintético como fallback.

O World permanece dono do field, roster e comandos. O Host é uma infraestrutura nativa isolada por
processo de field e expõe um contrato IPC binário versionado. Falha de bootstrap, hash, ABI ou IPC
deve recusar os bots daquele field sem substituir o resultado por física aproximada.

## Marco 1: bootstrap autônomo

O projeto `client/RakionBotEngineHost` produz `BotEngineHost.exe` x86 e deve ser implantado no
diretório `Bin` do cliente golden. Essa localização é parte da ABI: `SE_InitEngine` deriva o
diretório de dados a partir do caminho do executável.

Sequência comprovada:

1. o loader mapeia estaticamente `entitiesmp.dll`, como no import table do cliente original;
2. o Host define `_bDedicatedServer=1` antes de chamar `SE_InitEngine("Rakion")`;
3. `CTStream::EnableStreamHandling` habilita as páginas sob demanda dos XFS na thread do worker;
4. `CEntitiesDLL::loadDLL("Bin\\Entities.dll")` aplica o sufixo `MP` e registra o pacote protegido;
5. `GAME_Create` cria o `CGame` sem criar display;
6. `CGame::Initialize("Data\\SeriousSam.gms")` executa o DataSetup na ordem canônica;
7. o adaptador `IScavengerWorldNet` fornece field e personagem selecionado à `gamemp.dll`;
8. `CNetworkLibrary::StartPeerToPeer_t` carrega o `.wld` e inicializa o mundo nativo;
9. o encerramento executa `StopGame`, `GAME_Destroy`, `SE_EndEngine` e desabilita o stream handler.

O pacote legado tem entry point protegido em uma seção PE de dados. O `rakion.exe` original e as
DLLs v258 não usam `NX_COMPAT` nem `DYNAMIC_BASE`; o worker precisa das mesmas características para
executar a rotina de inicialização. Essa exceção é restrita ao processo x86 isolado. O host valida
exports obrigatórios e, antes da distribuição, também deverá validar hashes das DLLs.

## Build e probe

```powershell
.\client\RakionBotEngineHost\build.ps1 `
  -ClientRoot 'C:\Rakion' `
  -RunProbe `
  -World 'LevelsSV\Mammoth\Mammoth.wld' `
  -MapId 211 `
  -Mode 2
```

O probe só passa quando:

- `_pNetwork` e `_pTimer` estão inicializados;
- `entitiesmp.dll` está registrada;
- o mundo solicitado foi carregado;
- nenhum `d3d8`, `d3d9`, `d3d11`, `ddraw`, `dxgi` ou `opengl32` foi carregado;
- a sessão encerrou sem processo órfão.

Em 26/07/2026, o probe carregou e encerrou `LevelsSV\Mammoth\Mammoth.wld` a partir dos XFS do
cliente original. A saída confirmou `worldLoaded=1`, `entitiesLoaded=1` e lista vazia de módulos
gráficos.

## Marco 2: IPC e supervisor

O Host atende um named pipe local em modo byte. Cada frame possui header little-endian de 20 bytes:

| Campo | Tipo | Regra |
| --- | --- | --- |
| magic | `uint32` | `0x4842524F` |
| version | `uint16` | `7` |
| messageType | `uint16` | bit `0x8000` indica resposta |
| payloadSize | `uint32` | máximo de 4096 bytes |
| correlationId | `uint32` | ecoado na resposta |
| status | `uint32` | zero em sucesso |

Os comandos disponíveis são `Hello`, `LoadField`, `AddBot`, `Aim`, `Input`, `Tick`, `Snapshot`,
`Lifecycle`, `DamageReaction`, `Ping` e `Shutdown`. `LoadField` transporta `fieldId`, capacidade,
map ID original (`200..213`), modo battle (`1..4`) e caminho relativo sob `LevelsSV`. Ele aceita
um único carregamento por processo e não permite capacidade superior à oferecida pela engine.
`AddBot` transporta identidade explícita e cria a fonte local por
`CNetworkLibrary::AddPlayer_t`.

`Input` aceita combinações não conflitantes de forward, backward, left, right, jump e primary
attack. O Host converte essas intenções para os bits/eixos do `CPlayerAction` original e chama
`CPlayerSource::ApplyAction`/`SendAction`. `Tick` avança `CTimer::HandleTimerHandlers` e
`CNetworkLibrary::MainLoop` na mesma thread dona da engine. `Snapshot` retorna entidade pronta,
alive, HP, posição e rotação lidos da entidade nativa.

`Aim` recebe a posição do alvo conhecida pelo World e altera somente a orientação do placement da
entidade local. O caminho reutiliza `DirectionVectorToAngles` e `CEntity::SetPlacement`, já
comprovados no peer headless; posição e colisão nunca são escritas pelo World ou pelo Host.

`Lifecycle` aceita somente `Alive` ou `Dead`. O Host resolve a entidade ligada ao
`CPlayerSource` e chama os exports originais `CPlayer::SetAlive` ou `CPlayer::SetDead` da
`entitiesmp.dll`. A sequência monotônica do domínio impede reaplicar transições antigas.

`DamageReaction` recebe bot e seat do atacante. O Host resolve a entidade nativa e chama
`CPlayer::ExecDamageAnim(0x0F, 0x07, attackerSeat)`, combinação extraída do consumidor real de
`0x0311 kind=Damage`. O comando não altera HP: o World continua sendo a única autoridade de dano.
Uma sequência monotônica separada impede repetir a reação do mesmo impacto. Em golpe letal, o
coordenador aplica a reação antes de enviar `Lifecycle=Dead`.

O pipe recusa clientes remotos. Antes de carregar o mapa, o World valida PID, versão e as
capabilities `EngineBootstrap`, `NativeWorld`, `NativePlayerSources`, `NativeSnapshots`,
`NativeInputs`, `NativeTargeting`, `NativeLifecycle` e `NativeDamageReactions`.

`BotEngineSupervisor` mantém no máximo um worker persistente por field. Ele inicia o processo sem
shell ou janela, drena stdout/stderr, aplica timeout ao handshake e mata a árvore do processo quando
o encerramento cooperativo falha. Não existe ramo para iniciar o `BotManager` sintético.

O smoke completo do marco executa:

```powershell
.\client\RakionBotEngineHost\build.ps1 `
  -ClientRoot 'C:\Rakion' `
  -RunProbe `
  -RunIpcProbe
```

Além do smoke nativo, `BotEngineWorkerIntegrationTests` valida o processo real quando
`RAKION_BOT_ENGINE_HOST` e `RAKION_BOT_ENGINE_CLIENT_ROOT` estão definidos. O teste comprova
handshake, carregamento persistente do field, deduplicação do worker, criação de fonte local, ping
e shutdown sem processo órfão.

## Marco 3: fonte local real

O Host cria o personagem pelo mesmo caminho nativo usado por um peer local:

1. o adaptador seleciona a `_CharacterInfo` do bot;
2. `CPlayerCharacter` é construído com nome e espécie;
3. `CNetworkLibrary::AddPlayer_t` cria e registra o `CPlayerSource`;
4. o retorno confirma ID, fontes ativas e capacidade nativa.

O smoke validado em 26/07/2026 criou `BotProbe` com uma fonte ativa, capacidade nativa quatro,
mundo Mammoth carregado, nenhum módulo gráfico e encerramento limpo. Esse marco comprova criação e
registro da fonte. Ainda não comprova ações, simulação temporal, animação ou combate no stage.

## Marco 4: múltiplas fontes, input e snapshots

O protocolo v7 foi validado com as quatro fontes locais oferecidas pela engine. Para cada uma, o
Host avançou a simulação e resolveu uma entidade distinta. O smoke também aplicou forward ao
primeiro bot repetindo o ciclo humano `ApplyAction → SendAction → timer → main loop`; a posição
publicada pelo snapshot mudou sem escrita direta em placement. Isso comprova que o movimento veio
da engine, não de teleporte ou física reimplementada.

O teste integrado do World cobre o mesmo fluxo pelo supervisor real: quatro bots, um tick,
snapshots finitos, movimento observável após input e transições `Dead → Alive` confirmadas pelo
flag nativo do snapshot.

## Marco 5: lifecycle real do World

O comando `/addbot` não chama mais o tick de movimento sintético. O World primeiro reserva um seat
sem publicá-lo, inicia ou reutiliza o Host do field e cria a fonte nativa. O roster só recebe o
member-join após a confirmação do Host. Se bootstrap, mapa, IPC ou criação falhar, a reserva é
desfeita e os bots do field são removidos; não há troca automática para o motor sintético.

Durante uma partida battle, o clock do World avança o Host e copia posição e rotação dos snapshots
nativos para o estado de domínio. O ID de mapa usado pelo protocolo do cliente (`0..13`) é traduzido
para o catálogo original da engine (`200..213`). O Host é encerrado quando o field fica vazio, é
fechado pelo master, perde o processo nativo ou o World é desligado.

Configuração no `worldserver.ini`:

```ini
[BotEngine]
Enabled=1
HostPath=BotEngineHost.exe
ClientRoot=.
StartupTimeoutSeconds=30
ShutdownTimeoutSeconds=5
MaxBotsPerField=4
```

`HostPath` e `ClientRoot` relativos são resolvidos a partir do diretório do INI. Em uma distribuição
onde o World não fica dentro do cliente, aponte `ClientRoot` para a raiz que contém os XFS e
`HostPath` para o `BotEngineHost.exe` compilado sob o `Bin` dessa mesma raiz. Caminhos de máquina de
desenvolvimento não fazem parte da configuração distribuída.

## Marco 6: intenção e publicação do snapshot

O World seleciona o humano inimigo mais próximo, envia o alvo por `Aim` e aplica W, S ou ataque
primário pelo contrato nativo. A aproximação e o recuo usam os limites obtidos no peer headless:
`3,25` para alcance de ataque e `1,25` para espaçamento mínimo. A orientação é renovada quando o
alvo muda ou se desloca materialmente, evitando reinicializar o placement em todo frame.

Após cada tick, o snapshot da engine atualiza posição e heading do bot. O World publica esse estado
no canal de gameplay do field, incluindo o túnel TCP já usado pelo cliente. O teste E2E com Host
real confirma admissão, seleção do alvo, input W e recebimento do `0x030A` com o seat do bot. O
smoke isolado confirma deslocamento físico após `Aim + W`; a convergência visual desse deslocamento
no ciclo completo de uma partida ainda é um gate pendente e não equivale a combate validado.

A borda wire usa centésimos de unidade: `100` no `s16` de `0x030A` representa `1,0` na engine.
Snapshots nativos são multiplicados por `100` somente no codec de saída, e poses humanas são
divididas por `100` ao entrar no domínio. O Host e o cérebro trabalham sempre na unidade nativa.

## Marco 7: combate autoritativo humano → bot

O espelho autenticado do cliente encaminha ao World a pose `0x030A` e o início de ataque
`0x0311`. O domínio aceita somente sequências crescentes, limita a cadência e abre uma janela de
impacto entre 120 e 450 ms depois do início. Durante a janela, o servidor seleciona o bot inimigo
mais próximo que esteja jogando, vivo e ligado ao Host, limitado a:

- distância horizontal de `3,25` unidades nativas;
- diferença vertical máxima de `2,0`;
- cone frontal de 75 graus;
- uma confirmação de dano por sequência de ataque.

O dano atual é uma política de lançamento explícita, não uma fórmula alegada como original:
`50 + 2 × BasicAttack`, limitada a `50..250`. A política fica isolada no backend para ser
substituída pelas fórmulas por arma/equipamento quando esses contratos forem incorporados.

Um impacto confirmado reduz o HP do bot somente no World, publica `0x0311 kind=Damage` para os
humanos e interrompe input/snapshot de movimento durante a reação. Ao chegar a zero, o World
aplica a morte com causa comum `8`, atualiza o placar do modo, agenda o respawn e incrementa a
sequência de lifecycle. No tick seguinte, o coordenador envia `Dead` ao Host e a engine executa
`CPlayer::SetDead`. Em Deathmatch, Team Death e Boss, após sete segundos o domínio restaura o HP,
envia `Alive` e a engine executa `CPlayer::SetAlive`.

Os gates automatizados cobrem:

1. sequência, cadência, janela, alcance, altura, time e cone frontal;
2. dano idempotente e morte única;
3. ataque autenticado atravessando UDP, World e Host real;
4. pacote visual de HIT com atacante correto;
5. morte, um ponto em Deathmatch, lifecycle nativo e respawn com HP cheio;
6. quatro fontes nativas alternando `Dead → Alive` sem processo gráfico.

Esse marco ainda não prova o contato físico por evento de colisão da engine, linha de visão,
fórmula por arma, queda visual no cliente ou combate bot → humano.

## Marco 8: reação de dano nativa

Cada dano aceito pelo World incrementa `DamageSequence` e registra o seat atacante. Antes do tick
seguinte, o coordenador envia exatamente uma `DamageReaction` para o Host. A chamada ocorre na
thread proprietária da engine e reutiliza o export `ExecDamageAnim` da `entitiesmp.dll`; não há
animação, queda ou deslocamento sintetizado no backend.

O build x86 com `/W4 /WX`, o probe IPC contra o cliente original e os testes integrados do worker
confirmam ABI, capability, request/response e aplicação do comando nas fontes nativas. A queda e a
recuperação ainda precisam do gate visual no cliente gráfico; o smoke headless comprova que a
engine aceitou a transição, mas não substitui essa observação.

## Gates restantes

1. validar visualmente a convergência do deslocamento nativo no cliente gráfico;
2. validar visualmente HIT, queda e recuperação produzidos por `ExecDamageAnim`;
3. implementar e validar combate autoritativo bot → humano;
4. validar visualmente morte e respawn;
5. substituir a política provisória pelas fórmulas por arma/equipamento;
6. desativar os subsistemas de input e som não necessários ao worker;
7. remover o código remanescente do `BotManager` sintético e os patches correspondentes;
8. validar visualmente e sob carga múltiplos bots em todos os mapas battle.
