# BotHost headless multippeer

## Objetivo e fallback

O BotHost executa uma instância nativa sem renderização por field ativo e controla os bots daquele
field como jogadores locais do engine. O `BotManager` sintético continua disponível como fallback
quando não houver BotHost saudável. O World permanece responsável por roster, autorização, HP,
pontuação, morte e respawn; o engine headless fornece física, colisão, animações e eventos de arma.

O alvo inicial é uma instância por field, não uma instância por bot. Um único world/mapa é global
dentro do engine, portanto compartilhar a mesma instância entre fields diferentes ainda não está
comprovado e não faz parte do primeiro rollout.

## Evidência nativa da build v258

`engine.dll` exporta estes contratos:

| Símbolo | RVA | Evidência |
|---|---:|---|
| `_bDedicatedServer` | `0x002A0680` | flag global consultada na inicialização gráfica, sonora e de display |
| `CNetworkLibrary::AddPlayer_t` | `0x000F3EB0` | cria uma nova fonte local em um slot livre |
| `CNetworkLibrary::GetLocalPlayerCount` | `0x000EFE00` | expõe a quantidade de fontes locais |
| `CNetworkLibrary::GetLocalPlayer` | `0x000EFE30` | resolve a fonte local primária |
| `CNetworkLibrary::IsPlayerLocal` | `0x000F41C0` | verifica se uma entidade pertence a alguma fonte local |

`AddPlayer_t` percorre o vetor de fontes em `CNetworkLibrary+0x28/+0x2C`, procura um slot livre e
inicializa um `CPlayerSource` de `0x370` bytes. O limite é a capacidade do vetor, não uma constante
de um único jogador nesse método. Isso comprova suporte estrutural a múltiplas fontes locais, mas
não comprova ainda que o protocolo Rakion aceite todas como seats distintos sem adaptação.

## Ativação segura

Quando `OPENRAKION_HEADLESS=1`, `RakionClientPatch.dll` resolve o export
`?_bDedicatedServer@@3HA` e grava `1` antes do entry point do executável. O caminho normal não é
alterado. Se o engine ou o símbolo não corresponderem à build esperada, a DLL falha fechado e o
processo headless não continua em modo gráfico por engano.

Essa ativação é apenas o primeiro gate. Ainda faltam o supervisor BotHost, registro no World,
criação das fontes adicionais, associação fonte-seat, entrada automática no field e controle das
ações. Um processo iniciar com a flag não significa que o combate multippeer esteja validado.

O smoke local confirmou a mensagem `headless ativado antes do entry point
(_bDedicatedServer=1)` e o processo permaneceu vivo. A inspeção dos módulos carregados não encontrou
`d3d`, `ddraw`, `dxgi` nem `opengl`. A aplicação ainda cria uma janela de shell, que o
`RakionBotHost` oculta assim que o handle aparece.

## Supervisor

`client/RakionBotHost` inicia o cliente suspenso com a linha de comando especial do Rakion, define
`OPENRAKION_HEADLESS=1`, associa o processo a um Windows Job Object e só então o libera. O job usa
`KILL_ON_JOB_CLOSE`: encerramento normal, Ctrl+C ou crash do supervisor não deixa um processo
órfão. A lógica de `CreateProcess` fica em `RakionClientRuntime` e é compartilhada com o launcher.

Exemplo de smoke administrativo:

```powershell
$env:OPENRAKION_BOT_CREDENTIAL = 'credencial-da-conta-host'
$env:RAKION_CLIENT_ROOT = 'C:\Rakion'
dotnet .\RakionBotHost.dll --user bot_host_01 --field 7
Remove-Item Env:\OPENRAKION_BOT_CREDENTIAL
```

A credencial não é herdada como variável pelo processo filho. O protocolo legado ainda exige a
credencial codificada no vetor de argumentos do próprio jogo; uma conta de serviço com privilégios
mínimos e ticket curto deve substituir senha estática antes do rollout remoto.

O smoke atual valida bootstrap dedicated, ausência de módulos gráficos, shell ocultado e limpeza
do filho. Login válido, seleção de personagem, registro do field e criação de fontes adicionais
continuam pendentes.

## Estado da sessão nativa

Em modo headless, a DLL observa `CNetworkLibrary::GetLocalPlayerCount` somente depois de
`_pNetwork` estar disponível e registra cada transição no log de compatibilidade. Esse diagnóstico
é restrito ao processo com `OPENRAKION_HEADLESS=1` e não altera o cliente interativo.

O smoke com Broker, World e Buddy locais ativos chegou ao Broker em `127.0.0.1:40706`, porém não
abriu conexão com o World e permaneceu em `localPlayerCount=0`. Logo, a linha de comando legada
inicializa o shell, mas não conclui de forma autônoma a seleção do servidor, personagem e field.
Ocultar a janela não transforma esse fluxo em uma sessão multippeer pronta.

O próximo gate técnico é inicializar a sessão do engine diretamente ou fornecer ao shell uma
máquina de estados explícita para login e seleção. Fontes adicionais só podem ser criadas depois
que a fonte primária possuir personagem e sessão válidos; chamar `AddPlayer_t` antes desse ponto
não é um caminho seguro.

## Gates de lançamento

1. Processo headless inicia sem dispositivo gráfico, áudio ou janela interativa.
2. Uma instância registra um field e cria pelo menos dois `CPlayerSource`.
3. Cada fonte publica `0x030A/0x030F/0x0311` com seat correto e sequência independente.
4. Humanos acertam bots e bots acertam humanos usando o pipeline nativo.
5. Queda, morte, respawn e placar convergem no World.
6. Queda ou timeout do BotHost troca o field para o bot sintético sem desconectar humanos.
7. Teste de carga mede memória e CPU por field e por bot adicional.
