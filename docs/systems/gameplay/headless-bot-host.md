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

Essa ativação é apenas o primeiro gate. O supervisor, login, seleção de personagem e entrada no
field já estão conectados. Ainda faltam a criação das fontes adicionais, associação fonte-seat,
carregamento da partida e controle das ações. Um processo iniciar com a flag ou entrar na sala não
significa que o combate multippeer esteja validado.

O smoke local confirmou o processo dedicado e a ausência de `d3d`, `ddraw`, `dxgi` e `opengl`
entre os módulos carregados. A aplicação ainda cria uma janela de shell, que o `RakionBotHost`
desabilita e oculta assim que o handle aparece. O supervisor também envia a desativação explícita
de foco e restaura a janela que estava ativa antes do bootstrap, impedindo o processo dedicado de
capturar teclado ou mouse do cliente gráfico.

O engine pode tentar reativar o DirectInput durante a troca para o stage. Por isso, o isolamento
também existe dentro da DLL: exclusivamente com `OPENRAKION_HEADLESS=1`, ela resolve `_pInput` e as
APIs públicas `CInput::IsInputEnabled`, `DisableInput` e `ClearInput`. A captura é desativada e o
estado de teclas e eixos é limpo sempre que o engine a reabre. Isso libera mouse e teclado para o
cliente interativo durante toda a partida. Se esses exports não existirem na build carregada, o
headless falha fechado em vez de disputar a entrada.

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

O smoke atual valida bootstrap dedicated, ausência de módulos gráficos, shell ocultado, limpeza do
filho, login válido, seleção de personagem e entrada no field. A criação das fontes adicionais e o
carregamento da partida continuam pendentes.

## Estado da sessão nativa

Em modo headless, a DLL observa `CNetworkLibrary::GetLocalPlayerCount` somente depois de
`_pNetwork` estar disponível e registra cada transição no log de compatibilidade. Esse diagnóstico
é restrito ao processo com `OPENRAKION_HEADLESS=1` e não altera o cliente interativo.

O primeiro smoke com Broker, World e Buddy locais ativos chegou apenas ao Broker em
`127.0.0.1:40706` e permaneceu em `localPlayerCount=0`. Logo, a linha de comando legada inicializa o
shell, mas não conclui de forma autônoma a seleção do servidor, personagem e field. Ocultar a janela
não transforma esse fluxo em uma sessão multippeer pronta.

O bootstrap headless agora usa a ABI exportada do próprio engine para conectar diretamente ao
World e enviar o login. Ele decodifica a credencial hex recebida na linha de comando antes de chamar
`IScavengerWorldNet::SendLogin`; não escreve a credencial no log. Esse caminho só existe com
`OPENRAKION_HEADLESS=1`.

O smoke seguinte comprovou TCP estabelecido em `40708`, `onlinePlayers=1` no status do World,
identidade na lista de contas ativas e conexão Buddy em `8500`. A resposta de login foi consumida
pelo cliente.

Depois do login, o bootstrap consulta o `AccountInfo_s` do engine. O consumer v258 mantém a
quantidade de slots em `+0x6C` e até quatro records visíveis em `+0x1338`, com stride `0x424`; o
primeiro dword de cada record é o `characterId`. O host seleciona o primeiro ID não nulo por
`IScavengerWorldNet::SendCharacterSelect` e só considera o gate concluído quando
`GetSelectedCharacter` deixa de retornar nulo.

O smoke confirmou as mensagens `primeiro personagem selecionado` e `personagem confirmado pelo
engine`, mantendo as conexões World e Buddy. `localPlayerCount` continua em zero nessa fase porque
um personagem selecionado ainda não é uma fonte local carregada dentro de uma partida.

O supervisor também publica `OPENRAKION_HEADLESS_FIELD` com o field solicitado. Depois de confirmar
o personagem, a DLL chama `IScavengerWorldNet::SendFieldEnter`. A resposta `0x37` preenche
corretamente o roster, mas o cliente v258 tenta construir a tela da sala em seguida. Essa UI depende
de um componente gráfico ausente no modo dedicado e causava acesso inválido em
`CTFileName` (`engine.dll+0x16E7`).

O caminho headless valida o prólogo do callback de roster após o executável ser desempacotado e
desvia somente a cauda que destrói e recria a UI. Todo o parsing do field e dos 20 registros de
jogadores permanece no fluxo original. O cliente interativo não recebe esse desvio, e uma build
desconhecida não entra no field sem a validação dos bytes esperados.

No smoke com uma sala competitiva recém-criada, a terceira sessão recebeu a entrada `0x36` com
`field_id=1`, `players=2`, `capacity=12` e nome `HeadlessHost`. O processo headless permaneceu vivo
depois da resposta `0x37`. Isso comprova o peer registrado no field; ainda não comprova mapa
carregado, fonte local ou combate.

Depois da entrada, o host envia `IScavengerWorldNet::SendFieldReady(1)`. Um master controlado
recebeu a publicação `0x3D` do segundo jogador e o World aceitou o start com `0x43 00 00`; assim, o
peer já não bloqueia a partida por falta de ready.

A resposta de start constrói o estado `Play Game`, mas três chamadas consecutivas tentavam carregar
somente texturas de UI sem subsistema gráfico. O caminho headless valida e omite essas três chamadas,
preservando o restante do construtor: inicialização de sessão, MD5 e transição de estado. Depois
disso, o processo permaneceu vivo.

O cliente gráfico v258 não usa `SendFieldGameEnter` nessa transição. O fluxo canônico é
`0x43 → request 0x48 → primeiro 0x4B`; o host segue agora esse mesmo caminho por
`SendFieldGameRoundStart`. O próximo gate é confirmar que a resposta `0x48` cria a fonte local
primária e faz o próprio engine publicar `SendFieldGameAddPlayer`, sem sintetizar estado de
movimento no supervisor.

`CGame::StartGame` não recebe um modo P2P genérico. O caso `2` abre `TCP/IP Server`; o caso `4`
constrói `CNetworkSession` com o endpoint recebido e entra como `TCP/IP Client`. Neste rollout, o
BotHost é exclusivamente um segundo cliente controlado: só inicia o engine no caso `4`, depois de
`FieldInfo::IsMasterSlot()` confirmar que existe outro master. Se for promovido, falha fechado em
vez de abrir uma sessão server incompleta. Isso eliminou o acesso inválido causado por um segundo
seat tentando abrir outra sessão server.

O fixture TCP comprova ready/start e falha de join controlada, mas não pode validar o primeiro
`0x4B`: ele não implementa o host P2P do engine e deixa o placeholder `serveraddress:0`. Esse gate
exige uma sala cujo master seja um cliente Rakion real.

Fontes adicionais só podem ser criadas depois que a fonte primária possuir personagem e sessão
válidos; chamar `AddPlayer_t` antes desse ponto não é um caminho seguro. O próximo gate é iniciar a
partida, carregar o mapa e observar a criação da fonte local primária.

## Gates de lançamento

1. Processo headless inicia sem dispositivo gráfico, áudio ou janela interativa.
2. Uma instância registra um field e cria pelo menos dois `CPlayerSource`.
3. Cada fonte publica `0x030A/0x030F/0x0311` com seat correto e sequência independente.
4. Humanos acertam bots e bots acertam humanos usando o pipeline nativo.
5. Queda, morte, respawn e placar convergem no World.
6. Queda ou timeout do BotHost troca o field para o bot sintético sem desconectar humanos.
7. Teste de carga mede memória e CPU por field e por bot adicional.
