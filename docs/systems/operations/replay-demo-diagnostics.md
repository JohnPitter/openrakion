# RE de demo, replay e diagnósticos — Rakion v258

## Escopo e veredito

A `engine.dll` contém um gravador/reprodutor `.dem` completo da Serious Engine, mas o Rakion v258
não o expõe como uma feature funcional de jogador. O cliente conserva scanners e um helper de
playback, porém os caminhos que iniciariam reprodução não têm referência alcançável. Também não há
call site ativo para iniciar gravação, menu validado, upload, índice de partidas ou integração com o
World.

O servidor original e o .NET não armazenam timeline, inputs ou snapshots de replay. Portanto, o RE
desta build fecha replay como **infraestrutura genérica dormente da engine**, não como sistema Rakion
faltante. Implementar replay autoritativo seria uma feature nova.

## Fluxos no executável Rakion

Há dois caminhos diferentes:

| Rotina | Comportamento | Alcançabilidade |
|---|---|---|
| `FUN_00409800` | lista `Demo\Recorded-*.dem`, ordena em `DAT_004FEEA0` | sem caller; só o destrutor toca a lista |
| `FUN_0040BE60` | lista `EFNMDemo\*.dem` no boot e grava o count | chamada pelo inicializador |
| `FUN_0040DE90` | seleciona o índice e chama o método virtual `+0xA8` de playback | sem code ref nem ponteiro bruto no binário |

O cliente declara `m_bAutoPlayDemos`, mas nenhum fluxo ativo liga a lista a uma tela Rakion. Nas
cópias examinadas não existem os diretórios `Demo`/`EFNMDemo` nem arquivos `.dem` distribuídos.

Isso separa presença de código de disponibilidade de produto: o scanner ativo não torna o playback
acessível quando seu consumidor é órfão.

## Contrato genérico da `engine.dll`

Funções confirmadas:

- `CNetworkLibrary::StartDemoRec_t` e `StopDemoRec`;
- `CNetworkLibrary::StartDemoPlay_t` e `IsDemoPlayFinished`;
- `CServer::PrepareDemoData` e `WriteDemoData`;
- `CSessionState::ReadWorldAndState_t` e `RunDemoTick`.

A engine registra no shell `StartDemoRecording`, `StopDemoRecording`, `dem_iRecordedNumber`,
`net_bReportDemoTraffic` e `net_iDemoQuality`. Os wrappers dos dois comandos apenas sinalizam estado
interno; no cliente Rakion não foi encontrado caller ativo de `StartDemoRec_t` ou
`StartDemoPlay_t`. `StopGame` chama `StopDemoRec` defensivamente.

### Estrutura confirmada

O arquivo não é um replay de pacotes World. Ele serializa estado interno da Serious Engine:

```text
headerChunk
worldFileName
spawnFlags
2048 bytes de estado de rede
CSessionState.Write_t(fullState=true)

zero ou mais ticks:
  tickChunk
  tickTime
  compressedDataChunk
  compressedLength
  compressed entity/network messages

endChunk
```

Os IDs de chunk são objetos inicializados em runtime; não devem ser adivinhados a partir do zero da
imagem PE. O playback exige o mesmo contrato de versão, carrega o world referenciado, restaura
spawn flags/estado e rejeita incompatibilidade com asset de world mais novo. `net_iDemoQuality` é
limitado a `32..512` e controla a cadência/tamanho-alvo de gravação.

O formato é dependente de build, classes de entidade e assets. Abrir `.dem` arbitrário no processo
do jogo amplia superfície de parser e não é adequado como formato público sem isolamento.

## Relação com o servidor

Não há opcode, tabela SQL, diretório, job ou upload de replay no World v258. A gravação ocorre no
estado local da engine e inclui mensagens/entidades já recebidas pelo cliente. Ela não produz uma
fonte autoritativa para arbitragem, anti-cheat ou restauração de partida.

Os antigos arquivos `oracle_*.bin` usados no desenvolvimento eram fixtures de protocolo e foram
substituídos por builders/golden tests; não são `.dem` nem replay de gameplay.

## Diagnósticos disponíveis

- `engine_host.log`, `rakion.log`, `.RPT`, `ScreenShots\` e `%TEMP%\rakion_launcher.log`;
- `tools/orig_capture.ps1` e `orig_diag.ps1` para captura controlada do World original;
- sondas headless e logs estruturados do Broker, World e Buddy.

## Operação fiel e eventual extensão

Para fidelidade v258, não há replay a ativar. Manter:

1. capturas limitadas a conta/ambiente de teste;
2. logs com retenção e acesso restritos;
3. dumps, IPs, chat e identificadores fora de artefatos públicos.

Se replay for requisito de produto, criar formato próprio e versionado no backend:

```text
ReplayHeader(build, map, mode, seed, players, startedAt)
ReplayTimeline(sequence, serverTick, authoritativeEvent)
ReplayFooter(result, checksum, signature)
```

O World deve publicar somente eventos autoritativos para writer limitado e assíncrono. Falha do
writer nunca pode bloquear tick, settle ou persistência. Definir quota, retenção, consentimento,
anonimização e autorização antes de UI/listagem. Não reutilizar `.dem` como formato confiável.

## Evidência executada em 2026-07-15

- `TraceEngineDemoFlows.py` fechou exports, strings, call sites, shell e serialização da engine;
- `TraceRakionDemoUi.py` rastreou scanners, lista global e helper de playback;
- `TraceRakionDemoState.py` confirmou que a lista `EFNMDemo` só é criada, lida pelo helper órfão e
  destruída;
- busca de referências e ponteiro bruto não encontrou ligação para `FUN_0040DE90`;
- `StartDemoRec_t`/`StartDemoPlay_t` não têm caller Rakion ativo; `StopDemoRec` só aparece no teardown;
- cliente de operação e árvore de trabalho não contêm diretórios ou arquivos `.dem`.

## Classificação final

- **Confirmado:** formato local genérico, estado/ticks comprimidos e validações de compatibilidade.
- **Confirmado por ausência:** nenhum menu/call site Rakion ativo e nenhuma integração World.
- **Dormente:** scanners, helper de playback, shell e exports da engine.
- **Extensão futura:** replay autoritativo, upload, listagem, retenção e UI.
