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
| version | `uint16` | `4` |
| messageType | `uint16` | bit `0x8000` indica resposta |
| payloadSize | `uint32` | máximo de 4096 bytes |
| correlationId | `uint32` | ecoado na resposta |
| status | `uint32` | zero em sucesso |

Os comandos disponíveis são `Hello`, `LoadField`, `AddBot`, `Input`, `Tick`, `Snapshot`, `Ping` e
`Shutdown`. `LoadField` transporta `fieldId`, capacidade, map ID original (`200..213`), modo battle
(`1..4`) e caminho relativo sob `LevelsSV`. Ele aceita um único carregamento por processo e não
permite capacidade superior à oferecida pela engine. `AddBot` transporta identidade explícita e
cria a fonte local por `CNetworkLibrary::AddPlayer_t`.

`Input` aceita combinações não conflitantes de forward, backward, left, right, jump e primary
attack. O Host converte essas intenções para os bits/eixos do `CPlayerAction` original e chama
`CPlayerSource::ApplyAction`/`SendAction`. `Tick` avança `CTimer::HandleTimerHandlers` e
`CNetworkLibrary::MainLoop` na mesma thread dona da engine. `Snapshot` retorna entidade pronta,
alive, HP, posição e rotação lidos da entidade nativa.

O pipe recusa clientes remotos. Antes de carregar o mapa, o World valida PID, versão e as
capabilities `EngineBootstrap`, `NativeWorld`, `NativePlayerSources`, `NativeSnapshots` e
`NativeInputs`.

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

O protocolo v4 foi validado com as quatro fontes locais oferecidas pela engine. Para cada uma, o
Host avançou a simulação e resolveu uma entidade distinta. O smoke também aplicou forward ao
primeiro bot repetindo o ciclo humano `ApplyAction → SendAction → timer → main loop`; a posição
publicada pelo snapshot mudou sem escrita direta em placement. Isso comprova que o movimento veio
da engine, não de teleporte ou física reimplementada.

O teste integrado do World cobre o mesmo fluxo pelo supervisor real: quatro bots, um tick,
snapshots finitos e movimento observável após input. Ainda falta associar esse worker ao lifecycle
das salas e publicar o estado para clientes gráficos; ataque, HIT, queda, morte e respawn também
continuam fora deste marco.

## Gates restantes

1. configuração e associação do supervisor ao lifecycle real de fields no World;
2. associação independente de seat, sequência e personagem por fonte no field real;
3. publicação dos snapshots e eventos nativos para os clientes gráficos;
4. combate server-authoritative: janela, hitbox, dano, HIT, queda, morte e respawn;
5. desativação dos subsistemas de input e som não necessários ao worker;
6. remoção do `BotManager` sintético e dos patches de física/animação correspondentes;
7. validação visual e de carga com múltiplos bots.
