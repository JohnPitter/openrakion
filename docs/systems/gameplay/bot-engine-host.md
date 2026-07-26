# Bot Engine Host x86

## Objetivo

O caminho de lançamento de bots é um worker nativo x86 executado no servidor. Ele reutiliza a
`engine.dll`, a `entitiesmp.dll`, a `gamemp.dll` e os XFS da build v258 para física, colisão,
movimentação, animações e detecção espacial. O worker não inicia `rakion.exe`, não possui janela de
jogo e não admite o peer sintético como fallback.

O World permanece dono do field, roster e comandos. O Host é uma infraestrutura nativa isolada por
processo de field; a próxima borda será um contrato IPC versionado. Falha de bootstrap, hash, ABI ou
IPC deve recusar os bots daquele field sem substituir o resultado por física aproximada.

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
6. `CNetworkLibrary::StartPeerToPeer_t` carrega o `.wld` e inicializa o mundo nativo;
7. o encerramento executa `StopGame`, `GAME_Destroy`, `SE_EndEngine` e desabilita o stream handler.

O pacote legado tem entry point protegido em uma seção PE de dados. O `rakion.exe` original e as
DLLs v258 não usam `NX_COMPAT` nem `DYNAMIC_BASE`; o worker precisa das mesmas características para
executar a rotina de inicialização. Essa exceção é restrita ao processo x86 isolado. O host valida
exports obrigatórios e, antes da distribuição, também deverá validar hashes das DLLs.

## Build e probe

```powershell
.\client\RakionBotEngineHost\build.ps1 `
  -ClientRoot 'C:\Rakion' `
  -RunProbe `
  -World 'LevelsSV\Mammoth\Mammoth.wld'
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

## Gates restantes

1. contrato IPC binário, explícito e versionado entre World e worker;
2. lifecycle de um worker por field, com timeout e encerramento controlado;
3. criação de múltiplas fontes locais no mesmo worker;
4. associação independente de seat, sequência e personagem por fonte;
5. produção e encaminhamento de ações nativas equivalentes às humanas;
6. remoção do `BotManager` sintético e dos patches de física/animação correspondentes;
7. validação visual e de carga com múltiplos bots.
