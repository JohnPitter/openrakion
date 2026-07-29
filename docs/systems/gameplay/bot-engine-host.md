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

O `build_client_package.ps1` compila e empacota o host em `Bin\BotEngineHost.exe`, e o
`verify_client_package.ps1` cobra o arquivo — sem ele a instalação limpa sobe sem bots.

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
nativos para o estado de domínio. O `0x3B` do cliente já traz o mapa battle no catálogo original da
engine (`200..213`, capturado em sala real: `0xD3` = Mammoth) — o World repassa o byte ao Host sem
tradução, e `BattleWorldCatalog` recusa qualquer valor fora dessa faixa. Modo `0` (stage PvE) usa
outro namespace (`< 100`) e não admite bots. O Host é encerrado quando o field fica vazio, é
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

## Marco 8: reação de dano nativa

Cada dano aceito pelo World incrementa `DamageSequence` e registra o seat atacante. Antes do tick
seguinte, o coordenador envia exatamente uma `DamageReaction` para o Host. A chamada ocorre na
thread proprietária da engine e reutiliza o export `ExecDamageAnim` da `entitiesmp.dll`; não há
animação, queda ou deslocamento sintetizado no backend.

O build x86 com `/W4 /WX`, o probe IPC contra o cliente original e os testes integrados do worker
confirmam ABI, capability, request/response e aplicação do comando nas fontes nativas.

## Marco 9: combate autoritativo bot → humano

O cérebro nativo (`BotEngineBrain`) abre a janela de ataque do bot com o mesmo
`PlayerCombatState` do humano (cadência + janela 120–450 ms). O domínio resolve o alvo humano do
`TargetSeat` com hitbox idêntica (alcance `3,25`, vertical `2,0`, cone frontal). O dano usa
`BotHumanDamagePolicy` (`10 + level + bônus de dificuldade`, clamp `10..80`) e a armadura do
humano absorve primeiro via `PlayerCombatVitals`.

Publicação no fio:

1. animação `0x0311 kind=Damage` no seat da vítima;
2. evento tipado de dano (`ServerCombatDatagrams.Damage`);
3. vitais autoritativos (`PlayerRemainHp`);
4. em morte: `PlayerDeath` + TCP `0x4F` com placar e respawn após o delay competitivo.

O respawn humano restaura HP/AP cheios e publica `Respawn` + vitais. Não existe ramo sintético de
movimento: `BotManager.Tick` e o planejador de obstáculos server-side foram removidos.

## Bloqueio aberto: locomoção nativa não acontece

O bot **não anda**. A entidade cai até assentar no chão e gira pelo `Aim`, mas nunca se desloca no
plano — em partida real e no smoke isolado. O gate anterior media deslocamento incluindo o eixo
vertical, então a queda de alguns centímetros o satisfazia; `HasMoved` e o `ipc_smoke.ps1` passaram a
medir só X/Z e falham honestamente.

Sondas em runtime contra o cliente original, todas com o mundo Mammoth carregado (26/07/2026):

| Hipótese | Sonda | Resultado |
| --- | --- | --- |
| `ApplyAction` reconstrói e zera a ação | dump do struct antes/depois | **refutada** — valores sobrevivem e a engine arquiva a cópia em `+0x58` |
| falta aplicar na entidade | chamada direta de `CPlayer::ApplyAction` | **refutada** — executa sem falha, entidade não anda |
| offset errado do eixo de translação | varredura dos 22 campos `float` do `CPlayerAction` | **refutada** — nenhum campo desloca |
| conteúdo/layout da ação | `ctl_ComposeActionPacket` (composição da própria engine) varrendo os 176 bytes de `ctl_pvPlayerControls` | **refutada** — nenhuma entrada desloca |
| simulação parada | `CTimer::GetCurrentTick` + `CNetworkLibrary::IsPaused` | **refutada** — tick avança (`0.15 → 2.65`), `paused=0` |
| player fora de estado jogável | `CPlayer::IsPlayerReady` / `IsAlive` | **refutada** — `ready=1`, `alive=8` |
| fonte não registrada | `pls_Index` / `pls_Active` | **refutada** — `0` / `1` |

### Causa estrutural encontrada: o driver de tick da SE1 está stubado

Desassemblando a `engine.dll` (que **não** é empacotada, ao contrário da `entitiesmp.dll`):

```
?ProcessGameStream@CSessionState@@QAEXXZ        -> ret
?ProcessGameStreamBlock@CSessionState@@QAEXAAV… -> ret 4
```

O caminho de simulação da SE1 (`MainLoop` → `ProcessGameStream` → tick → `CPlayerTarget`) **não
existe** neste binário: o Rakion substituiu o netcode por protocolo próprio. Por isso o host, que
chamava `HandleTimerHandlers` + `MainLoop`, nunca simulava nada — a entidade levantada 5 unidades
ficava parada no ar (`queda=0.000`) enquanto os ticks "avançavam".

As primitivas de simulação continuam vivas e exportadas. Dirigindo-as manualmente o mundo passa a
simular de verdade:

```
session = *(void**)((byte*)_pNetwork + 0x24)      // via ?IsPaused@CNetworkLibrary
_pTimer->SetCurrentTick(t += TickQuantum)
session->HandleMovers()                           // ?HandleMovers@CSessionState@@QAEXXZ
```

Com isso a entidade caiu 5,31 unidades e assentou no chão — gravidade e colisão reais. O
`HandleTimers` (timers de entidade) ainda falha ao cruzar o primeiro agendamento e ficou de fora.

Sobre a locomoção: `?UpdatePlacement@CPlayer@@UAEXABVCPlayerAction@@@Z` escreve o placement **a
partir da ação** — com ação sem posição, teleportou a entidade para a origem. Ou seja, existe um
caminho de placement absoluto na ação, coerente com o `0x030A` do fio.

### RE da `entitiesmp.dll` destravado por dump do módulo desempacotado

A `entitiesmp.dll` em disco é empacotada: os RVAs dos exports caem em espaço sem seção, então
desassemblar o arquivo não serve. O host, porém, tem o módulo já desempacotado em memória — despejar
`SizeOfImage` a partir da base (`0x35000000`, 5.100.032 bytes) produz uma imagem plana em que
`offset == RVA` e o Ghidra/objdump leem normalmente. **É assim que se faz RE dessa DLL.**

Com isso, `CPlayer::ApplyAction` (RVA `0x153DE0`) ficou legível:

| Fato | Evidência |
| --- | --- |
| gate de ação | `[CPlayer+0x3E0] & 1` deve estar setado; `& 2` aborta. No bot vale `0x00000001` ⇒ **passa** |
| a ação é copiada | `rep movsl` de `0x12` dwords (72 B) para `CPlayer+0xBB0` |
| botões | `ação+0x10` (o palpite do host estava certo) |
| eixos | `ação+0x2C / +0x30 / +0x34`, filtrados contra acumuladores em `+0xAA4/8/C` |
| desvio conhecido | `call *0x352B35B4` → `engine.dll 0x3600CFC0` = `!(CEntity+0x10 >> 28 & 1)`, o mesmo gate do HIT×N |

### O que ainda não anda

Com a simulação dirigida de verdade (controle positivo no mesmo caminho: entidade levantada cai
`5.310`), varrer **os 32 bits de botão** e **todos os campos float da ação** chamando
`CPlayer::ApplyAction` + `HandleMovers` não produz deslocamento horizontal. `CPlayerEntity::DoMoving`
é só um `jmp` para `CMovableModelEntity::DoMoving`, isto é, integração pura — alguém precisa setar a
translação desejada antes.

### Corrigido: o tick engolia as faltas de página dos XFS

`HandleTimers` parecia falhar ao cruzar o primeiro agendamento. Não falhava: o rastro mostrou

```
CParticleEmitter::InitializeParticle → CTextureObject::SetData_t → CStock_CTextureData::Obtain_t
  → CSerial::Load_t → CTextureData::Read_t → CTStream::ExpectID_t   (AV lendo o cursor do stream)
```

carregando `ModelsSV\Effects\Chaos\Mage\Swing_Invincible\rebirth003.tex` de `modelssv.xfs`. Essa
violação de acesso é o mecanismo **normal** de paginação sob demanda dos XFS: o filtro
`CTStream::ExceptionFilter` materializa a página e manda continuar. `AdvanceEngineSafely` usava
`EXCEPTION_EXECUTE_HANDLER` genérico — ao contrário dos outros três pontos de invocação — e portanto
abortava o tick em qualquer leitura de recurso. Passando o filtro da engine, os 60 ticks completam
sem falha e a lógica de entidade roda.

### Locomoção: caminho encontrado, ainda não fecha pelo host

Sonda isolada **fez o bot andar** pela primeira vez:

```
[walk] freeze=1 → apos limpar: freeze=0
[walk] campo 40 ANDOU dx=-19.668 dz=-16.415
```

Receita da sonda: encerrar o congelamento de spawn, escrever o vetor de translação na ação e chamar
`CPlayer::ActiveActions`, com `HandleTimers` + `HandleMovers` no mesmo laço.

O que a RE cravou:

| Peça | Fato |
| --- | --- |
| handler de locomoção | `?ActiveActions@CPlayer@@QAEXABVCPlayerAction@@@Z` — único que chama `CMovableEntity::SetDesiredTranslation`/`SetDesiredRotation` |
| translação | vetor em `ação+0x40/+0x44/+0x48` (relativo ao ponteiro publicado pela fonte) |
| congelamento | `CheckFreezeState()==1` ⇒ `SetDesiredTranslation(0,0,0)`; campos `CPlayer+0x27D4` (tipo), `+0x27D8` (início), `+0x27DC` (restante) |
| gate de entidade | `!(CEntity+0x10 >> 28 & 1)` — o mesmo do HIT×N |
| **a fonte zera os eixos** | medido: `apos escrever tz=-6.00` → `apos source tz=0.00`. `CPlayerSource::ApplyAction/SendAction` recompõe a ação do estado de controles, vazio no host ⇒ a intenção do bot precisa ser escrita **depois** da publicação |

O host já aplica tudo isso (freeze encerrado no input e no lifecycle, intenção escrita após a
publicação, `ActiveActions` chamado, tick dirigindo `HandleTimers`+`HandleMovers` com relógio
monotônico) e mesmo assim o gate horizontal não passa.

**A sonda decisiva foi feita e desmente o marco anterior.** Lendo
`CMovableEntity::GetDesiredTranslation` logo depois do `ActiveActions`, com a ação carregando
`tz=-6.00`:

```
[desired] tz_acao=-6.00 desired=(0.000,0.000,0.000)
```

Nenhuma translação é produzida. Ou seja, o deslocamento observado na sonda anterior **não era
locomoção** — muito provavelmente foi corrupção de estado da entidade por escrita em campo alheio,
da mesma família do teleporte já visto com `UpdatePlacement`. Locomoção nativa continua **não
alcançada**.

### O despacho de movimento é do animador

`ActiveActions` zera os vetores locais e desce por uma cadeia de estados lida do
`?GetPlayerAnimator@CPlayer@@QAEPAVCPlayerAnimator@@XZ`:

```
CPlayer+0x49C == 0        → segue
animator+0x12C == 0       → 0x35151450
animator+0x134 == 0       → 0x35151683
animator+0x144 == 0       → 0x351516DF
animator+0x140 == 0       → 0x35151B18   ← onde o bot aterrissa
```

Medido no bot: **todos esses campos valem 0** e `GetDesiredTranslation` continua `(0,0,0)`. Cada
campo do animador corresponde a um modo de movimento; sem nenhum ativo, não há translação.

A tabela de estados está no próprio binário — `?ePlayerAction_values@@3PAVCEntityPropertyEnumValue@@A`
(RVA `0x38D810`) — e bate com o `PlayerNormalAnimation` que o servidor já usa:

```
0 None · 1 Stand anim · 2 Idle00 · 3 Idle01 · 4 Forward move · 5 Backward move
6 Left move · 7 Right move · … · 12 Jump · 14 Rise · 17 Guard · …
```

Chamar `?SetPlayerActionState@CPlayerAnimator@@QAEXW4ePlayerAction@@@Z` com `4` (Forward move)
**não** destrava: a translação segue zerada, ou seja, esse setter não escreve os campos que o
`ActiveActions` consulta.

### Quem liga os interruptores: `CPlayerAnimator::AnimatePlayer`

Varrendo o dump por escritas nesses campos e filtrando por métodos de `CPlayerAnimator`:

| Campo | Escritor |
| --- | --- |
| `+0x12C` | `?AnimatePlayer@CPlayerAnimator@@QAEXABVCPlayerAction@@@Z` |
| `+0x134` | `?AnimateGuard@…` |
| `+0x13C` | `?AnimateDamage@…`, `?ChangeAttack01State@…`, `?SetPlayerActionState@…` |
| `+0x140`, `+0x150` | `?AnimateHoldAttack@…`, `?AnimateHoldTry@…` |
| `+0x144` | `?ChangeWeapon@…`, `?SetWeapon@…` |

`AnimatePlayer` é o driver por tick: recebe a **própria ação** e, logo no início, faz
`movzbl eax,[ação+0x44]` → `animator+0x128`. Ou seja, `ação+0x44` é um **byte de estado** cujos
códigos são o enum `ePlayerAction` do próprio binário — o mesmo catálogo do `PlayerNormalAnimation`
do servidor. O `ActionStateOffset = 0x44` que o host original tinha estava certo.

**Tentativa registrada como negativa:** chamar `AnimatePlayer(animator, ação)` no host, com o byte de
estado preenchido, **regrediu os testes de 7/8 para 4/8** — o animador precisa de contexto que o host
headless ainda não monta (provavelmente modelo/animação carregados). Revertido; baseline de volta em
7/8.

### A bifurcação local × remoto explica o impasse

`?IsLocalEntity@CPlayerAnimator@@QAEHXZ` é, no código, `!(CEntity+0x10 >> 28 & 1)` — o mesmo bit 28
do gate do HIT×N. Ele decide o caminho de animação:

| Caminho | Condição | Comportamento |
| --- | --- | --- |
| **local** | bit 28 limpo (caso do bot) | `AnimatePlayer` **ignora** `ação+0x44` e lê `CPlayer+0x148`; `ActiveActions` computa translação e chama `SetDesiredTranslation` |
| **remoto** | bit 28 setado | `AnimatePlayer` **escreve** `CPlayer+0x148` a partir do estado da ação; `ActiveActions` desvia (`je 0x35152365`) e não computa movimento — a posição vem transportada (`UpdatePlacement`) |

Ou seja, o Rakion tem dois modelos: peer remoto **não simula** locomoção (recebe posição, coerente com
o `0x030A` do fio carregar posição absoluta), e player local simula, mas depende de `CPlayer+0x148`,
que **nenhum método de `CPlayer` ou `CPlayerAnimator` escreve no caminho local** — a varredura de
escritas mostra só `ResetAnimationFlags` (reset) e o próprio `AnimatePlayer` (caminho remoto).

Isso indica que o estado que destrava a locomoção local é escrito **fora da `entitiesmp.dll`**,
provavelmente na camada de input/jogo do `rakion.exe` — que o host headless não tem. Forçar
`CPlayer+0x148 = 1` à mão não produz translação e **derruba o processo**.

Consequência para o goal: ou se reproduz no host a camada que alimenta esse estado, ou se aceita o
modelo remoto (posição calculada no servidor e transportada), que é como o próprio jogo move os
outros jogadores — mas aí a locomoção deixa de ser simulada pela engine, o que contraria o goal como
está escrito. **Essa escolha é do dono do projeto, não minha.**

### RESOLVIDO: o bot anda pela engine

O gate horizontal passa e a suíte fecha em **945/945**, com os oito testes de host nativo verdes —
locomoção, combate humano→bot, combate bot→humano, morte, respawn e multi-bot.

A receita, toda derivada da desassemblagem:

1. encerrar o congelamento de spawn (`CPlayer+0x27D4/+0x27DC`), que a engine usa para zerar a
   translação;
2. publicar a ação pela fonte **antes** de escrever a intenção (a fonte recompõe os eixos a partir
   do estado de controles, vazio no host);
3. ligar o modo de movimento do animador (`CPlayerAnimator+0x12C`) e escrever o vetor de
   deslocamento (`+0x16C/+0x170/+0x174`) conforme o input do World;
4. chamar `CPlayer::ActiveActions`, que converte isso em `SetDesiredTranslation`;
5. avançar o tick com `HandleTimers` + `HandleMovers`, que integram com física e colisão reais.

O deslocamento nunca vem de "teleporte": o World só declara intenção; cálculo, colisão e integração
são da engine. A verificação usa dois sinais independentes — o oráculo
`GetDesiredTranslation` (diferente de zero) e o deslocamento horizontal medido no snapshot.

### De onde sai o vetor de locomoção

Lendo o ramo com `animator+0x12C != 0`:

```
call GetPlayerAnimator ; add eax,0x16C
mov ecx,[eax] ; mov edx,[eax+4] ; mov eax,[eax+8]   → vira a translação desejada
```

O vetor **não vem dos eixos da ação** — vem de `CPlayerAnimator+0x16C`, preenchido pelo lado da
animação. Isso explica de forma econômica por que nenhum campo da ação jamais moveu o bot: em
Rakion o deslocamento do player é dirigido pela animação (root motion), e o `ActiveActions` apenas
copia esse vetor para `SetDesiredTranslation`.

Consequência prática: locomoção nativa depende do pipeline de **modelo/animação carregado**, que o
host headless não monta — o mesmo motivo pelo qual chamar `AnimatePlayer` derrubou os testes.
Nenhum escritor de `+0x16C` aparece em métodos de `CPlayerAnimator`/`CPlayer` com as formas usuais
de store, o que reforça que o preenchimento vem do sistema de modelos da `engine.dll`.

Fio solto para a próxima rodada: identificar quem preenche `CPlayerAnimator+0x16C` (provável API de
modelo/animação da engine) e medir o custo de carregar esse pipeline sem display.

## Marco 10: o combate chega ao cliente pelo túnel

Três defeitos de apresentação, invisíveis para a suíte anterior porque todos os gates de combate
rodavam com o cliente em UDP direto:

1. **Assimetria de publicação.** O golpe humano → bot publicava só a animação de HIT. O cliente
   desenha recuo e barra de vida do alvo a partir dos eventos tipados, então o bot apanhava em
   silêncio. Agora esse sentido publica dano, vitais e morte, como o sentido oposto já fazia.
2. **Respawn sem vitais.** A barra do bot ficava vazia depois que ele renascia; o respawn republica
   `EPlayerRemainHP`.
3. **Túnel sem cobertura.** O cliente original recebe gameplay por `0x57`, caminho que nenhum teste
   exercitava — foi assim que o `BuildTunnelPayload` recusando evento de entidade passou despercebido.

Contratos cravados nos gates:

- corpo do túnel = `[u16 opcode][u16 len][tipo(2) + bytes do offset 7]`; a sequência é reinserida no
  cliente;
- `UsesTunneling` é **limpo no start da partida** — o cliente real se registra depois de entrar no
  stage, e um teste que crave a flag antes está testando ficção;
- `EPlayerDamage 0x0191000B` sai no layout da RE (40 B: `playerId`, `damageType`, `damageMotionType`,
  `u16` reservado, dois escalares, dois `vec3f`), validado campo a campo.

### `WorkReduce_HP_AP` lido no dump desempacotado

A `entitiesmp.dll` em disco é empacotada; o host ganhou `--dump-entities <arquivo>`, que despeja a
imagem carregada (`SizeOfImage`, offset == RVA) e destrava o disassembly. Com ele:

| Slot virtual | Papel |
| --- | --- |
| `+0x158` / `+0x15C` | get/set de **HP** (usados por `ReduceHP`) |
| `+0x170` / `+0x16C` | get/set de **AP** (usados por `ReduceAP`) |
| `+0x164` | `ReduceHP` |
| `+0x178` | `ReduceAP` |

`CPlayer::WorkReduce_HP_AP(float a, float b)` manda **`a` para `ReduceHP` (`+0x164`)** e **`b` para
`ReduceAP` (`+0x178`)**; se o AP cruza zero, o resto vai para `ReduceHP` e o AP é zerado — o
transbordo clássico de armadura para vida. Isso fecha metade da pendência dos escalares.

Ainda **não** está cravado qual escalar do evento cai em cada argumento: o `ApplyReceiveDamage`
copia `firstDamageValue`/`secondDamageValue` para slots de pilha que sofrem `push` intermediários
antes do call, e amarrar isso exige rastrear o `esp` pela função inteira. Enquanto isso, o World
preenche o primeiro escalar e publica o snapshot autoritativo de HP logo em seguida, então a
aproximação local do cliente é corrigida no mesmo tick.

### `damageType` / `damageMotionType` são tabelados por arma × golpe

`?GetDamageMotionType@CPlayerWeapons@@QAE?AW4DamageMotionType@@J@Z` (RVA `0x17B020`) valida o índice
do golpe contra `0x17` (23 golpes por arma) e resolve o valor com

```
esi = byte[ esp + 0x28A + ((arma * 0x17 + golpe) * 0x50) ]
```

— registros de 80 bytes, um por par arma/golpe, carregados da tabela de armas. Índice fora da faixa
devolve `0`. Ou seja: **não existe um valor único de melee**; o valor depende de qual golpe o
personagem desferiu.

O World hoje emite `damageType=11` e `damageMotionType=4`, que são escolha, não medição. Para cravar
falta ler os dois bytes de um golpe real numa captura humano↔humano — é o que decide qual reação o
cliente toca (recuo, queda, contador de HIT).

## Marco 11: a captura humano×humano derruba o caminho do `EPlayerDamage`

A captura de 28/07/2026 (dois clientes reais no mesmo field, quatro mortes) mostrou que o Marco 10
resolveu o transporte mas escolheu o **evento errado**. O detalhe completo do dialeto está em
[`combat-actions-status.md`](combat-actions-status.md#argumentos-de-kind2-medidos-entre-dois-clientes);
o que importa para o bot:

- **`EPlayerDamage 0x0191000B` não trafega** entre dois clientes. Nenhuma vez, em partida inteira.
  O que o Marco 10 acrescentou ao sentido humano → bot era, portanto, invisível: o cliente não roda
  reação de dano por evento imposto sobre entidade **remota**.
- O jogo é **peer-autoritativo sobre o próprio corpo**: a vítima publica sobre si mesma
  `PlayerRemainHP` e, no mesmo milissegundo, `0x0311 kind=2`; o atacante publica só `EShootWeapon`.
- Os três argumentos do `kind=2` ficaram medidos: `(01,02,01)`/`(02,01,01)` alternando em golpe que
  não derruba, `(0F,07,01)` no que derruba. O terceiro argumento é `01` em todos os frames — o World
  mandava ali o assento do atacante.

Correção aplicada no sentido humano → bot: publicar `RemainHP` + reação `kind=2` sobre o **próprio
assento do bot**, na ordem medida, e **remover** o `EPlayerDamage`, que era código sem efeito. O
sentido bot → humano mantém o `EPlayerDamage` sobre a entidade **local** do jogador, onde ele
comprovadamente funciona e serve à autoridade do servidor.

Isso também **reduz** a pendência de `damageType`/`damageMotionType` acima: eles pertencem ao evento
que só é usado no sentido bot → humano.

### Validação visual: aprovada em 29/07/2026

Partida no cliente original, com o usuário confirmando na tela:

- **humano → bot**: o bot **reage aos golpes — recuo e queda**, morre e renasce;
- **bot → humano**: o personagem do jogador cai sob os golpes do bot;
- barra de vida do bot acompanha o `RemainHP` autoritativo.

O log da partida confirma o fio: reações `11030A02010201` / `11030A02020101` alternando,
`11030A020F0701` na queda, `RemainHP` e `EPlayerDeath` com `sender == idxA == assento do bot`, e
**nenhum** `EPlayerDamage` sobre o bot.

**Em aberto — contador HIT×N.** Com a reação visível funcionando, o número flutuante continua
ausente sobre o bot. Não é defeito de wire (os frames são byte a byte idênticos aos de um humano
apanhando) e **não é teto arquitetural** — registro anterior nesse sentido está corrigido.

O gate `[vítima+0x394]` de fato barra a contagem em entidade dirigida por rede, mas o
`RakionClientPatch` já contorna isso: o cave em `0x351533e9` chama `AddHitCount` direto no jogador
**local**, que é onde o contador vive. O canal é o arquivo `C:\temp\bot_lifecycle_<porta>.txt`, com
oito campos — `seat gen seq dead dmgSeq attackerSeat attackerHitSeq moving`; a DLL só aplica o HIT
quando `attackerSeat` está na faixa válida e `attackerHitSeq` cresce.

Verificado em 29/07/2026, sem fechar o caso:

- servidor produz o dado certo — `ConfirmHit` devolve `++ConfirmedHitSequence` (cresce a partir de
  1) e `TakeDamage` grava assento e sequência do atacante;
- a DLL lê o arquivo — provado pelo log dela, que aplicou `reacao de dano seat=10 seq=1..4` e as
  transições de lifecycle na partida;
- os binários deployados (`version.dll`, `RakionClientPatch.dll`) batem **hash** com o build desta
  branch, então não há deploy defasado.

Próximo passo: o ramo do HIT local exige `localPlayer() == player` — caminho distinto do da reação
de dano, que age sobre a entidade do bot. Instrumentar esse ramo com log próprio (o padrão de
`compat_log` já existe) crava se ele é alcançado e com que valores.

## Gates restantes

1. contador HIT×N (acima);
2. substituir as políticas provisórias de dano pelas fórmulas por arma/equipamento;
3. desativar os subsistemas de input e som não necessários ao worker;
4. multi-bot sob carga nos mapas battle — coberto no fio, sem observação visual dedicada.

## Implantação

O `BotEngineHost.exe` é x86 e precisa ficar sob o `Bin` de uma raiz de cliente válida — a localização
faz parte da ABI, porque `SE_InitEngine` deriva o diretório de dados do caminho do executável. Em
uma instalação onde o World não roda dentro do cliente, aponte no `worldserver.ini`:

```ini
[BotEngine]
Enabled=1
HostPath=<raiz do cliente>\Bin\BotEngineHost.exe
ClientRoot=<raiz do cliente>
```

Build e verificação local:

```powershell
.\client\RakionBotEngineHost\build.ps1 -ClientRoot '<raiz do cliente>' -RunProbe -RunIpcProbe
```

O `-RunIpcProbe` exercita o contrato inteiro (quatro fontes, aim, input, reação de dano, lifecycle,
ticks e snapshots) e **falha se a locomoção horizontal parar de funcionar** — é o gate honesto, que
mede só o plano X/Z para não aceitar queda como movimento.
