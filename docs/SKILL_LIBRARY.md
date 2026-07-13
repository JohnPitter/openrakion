# A Biblioteca de Habilidades — OpenRakion

> Carta de um engenheiro principal que está se aposentando. O tempo acabou; o que eu sei
> não pode sair comigo pela porta. Isto não é um manual de API — o código conta o *quê*.
> Isto conta o *como* e o *porquê*: as técnicas que eu levei meses para descobrir, os erros
> que custaram noites, e o jeito de pensar que faz o trabalho render neste projeto tão
> incomum. Leia antes de tocar em qualquer coisa séria. Guarde perto.
>
> — 2026-07-06

---

## 0. Onde você está

OpenRakion é **preservação de software**. Um jogo online morreu — servidores oficiais fora do
ar, sem código-fonte, só os binários que o autor possui. Nós reconstruímos, em .NET, um
**servidor compatível** que faz um cliente **offline e pessoal** voltar a rodar. Nada disto
toca serviço vivo de terceiros. Todo o trabalho de engenharia reversa, de análise de crash,
de estudo de protocolo, existe para **interoperabilidade** — fazer o cliente que o autor tem
conversar com o servidor que nós escrevemos.

Se você internalizar uma frase deste documento, que seja esta:

> **O comportamento do servidor é SINTETIZADO do domínio, nunca REPLAYADO de uma captura.**

Toda técnica abaixo orbita esse princípio. A captura é a *verdade de referência* (o golden
test), não a *implementação*. Um `oracle_0c.bin` no disco é uma muleta que envenena o projeto;
um `LoginCharListWriter` que serializa o estado e é testado byte-a-byte contra aquela captura é
engenharia de verdade. Já migramos TODOS os replays legados (login 0x0C/0x0D, a cadeia
lobby→canal→sala→stage). **Não recrie nenhum.**

---

## 1. As habilidades de Engenharia Reversa

Você vai passar mais tempo entendendo binários alheios do que escrevendo C#. Estas são as
técnicas que funcionam, em ordem de quando alcançá-las.

### 1.1 RE completa ANTES de testar — não palpite→testa→ajusta
A armadilha mais cara do projeto é tratar o cliente como oráculo de tentativa-e-erro: mudar um
byte, subir, ver se crasha, repetir. Isso queima horas e ensina nada. **Reconstrua a função
inteira no Ghidra e crave o layout byte-a-byte primeiro.** Só vá ao jogo quando a sua hipótese
for sólida o bastante para você prever o resultado. Quando o teste in-game contradiz uma RE que
você achava sólida, o problema quase sempre é que a RE não estava sólida — era um palpite bem
vestido.

### 1.2 Quando a RE estática empaca, troque por um efeito VISÍVEL (diagnóstico runtime)
A regra 1.1 tem um contraponto, e a tensão entre as duas é onde mora a maestria. Às vezes cada
fix de RE estática falha in-game e você não sabe *onde* quebra. Aí você inverte: em vez de mais
leitura estática, injeta um **efeito visível** no ponto suspeito — um botão que aparece, uma cor
que muda, um log no offset exato. O teste crava ONDE a execução realmente passa. Foi assim que o
botão nativo "Add Bot" saiu do limbo: rastreio estático dizia uma tela, o efeito visível provou
outra. Use isto para *localizar*; use a 1.1 para *entender*. Nunca troque uma pela outra.

### 1.3 Cravar o offset de um crash sem admin — via Event Log
O anti-debug do cliente bloqueia procdump e afins. Mas o **Visual Studio / Windows Event Log**
(Application Error) registra módulo + offset de qualquer AV, sem privilégio elevado. Pegue o RVA
de lá, jogue no `objdump` (MinGW) sobre o módulo, e você tem a instrução exata que morreu. Essa
técnica destravou mais crashes do que qualquer debugger.

### 1.4 A engine.dll É a Serious Engine 1 (open source) — LEIA a fonte
A descoberta que mudou o jogo: a `engine.dll` do cliente **é a SE1 da Croteam**, que é open
source. 8736 exports casam. Isso significa que o netcode, o `CSessionState`, o
`IScavengerWorldNet`, o handshake de peer — tudo tem fonte pública legível. Antes de reverter
qualquer coisa da engine cega no Ghidra, **procure na fonte da SE1**. A `gamemp.dll` traz os
overrides do Rakion por cima; a `engine.dll` é o esqueleto público. Igualmente: a `engine.dll`
TEM símbolos (`IScavengerWorldNet`, 122 métodos `Send*`) — o dispatch de recepção do cliente é
`ProcessWorldRecvBuffer`. Você raramente está tão cego quanto pensa.

### 1.5 O binário do RE ≠ o binário que roda
Cuidado de ouro, aprendido com dor: o `entitiesmp.dll` que você desempacota para RE estática
(`rakion-new`) **não é** o `rakion-final` que o jogo real de 2007 executa. Offsets de entidade
não transferem entre eles — foi isso que fez o HIT×N e o 0x307 falharem por semanas. A
`engine.dll` é estável entre builds; o resto, não. Sempre confirme que você está lendo o binário
que de fato roda. O launcher real vive em `Rakion\rakion-final\`.

### 1.6 Captura/MITM fecha protocolo de uma vez
Quando um frame é complexo demais para reverter só na estática, uma **captura de sessão real**
(MITM que decifra o TCP AES-128-ECB) mostra os bytes exatos no instante certo. Foi o método que
fechou 0x0C, a cadeia de lobby, o inventário. As ferramentas estão em `tools/` (`mitm_botcap.py`,
`decode_bot_action.py`). O truque logístico: o launcher só lança se o world estiver "online" no
broker — então para capturar o servidor ORIGINAL você aponta o registro do broker para um proxy
no meio, mantendo o seu world de pé para o login passar. Documentado em
[`bot-stage-capture-retake.md`](bot-stage-capture-retake.md). E lembre: a captura vira **golden
test**, nunca a implementação embarcada.

---

## 2. As armadilhas do cliente (o "muro")

O cliente é fechado, anti-tamper, e tem opiniões fortes. Estas são as leis dele que você não vai
mudar — só respeitar.

### 2.1 Só mande formas de frame que o cliente JÁ VIU
A lição-mestra do subsistema de rede. O cliente aceita um frame se ele tem a forma exata de algo
que o servidor original já lhe enviou naquele contexto. Uma variação — um byte de tamanho
diferente, um campo a mais — não dá erro elegante: **crasha** (AV) ou congela. Foi assim com o
roster 0x37 (mexer no tamanho do record travava), com o spawn 0x4b, com o quickslot no
char-select. Antes de sintetizar um frame novo, tenha um **baseline nativo**: prove que o
original manda aquela forma, naquele momento, e replique a *forma* (sintetizando o *conteúdo* do
estado).

### 2.2 NENHUMA DLL injetada para funcionalidade — sempre há caminho server-side
Regra fixa e inegociável. O jogo original rodava sem nenhuma DLL nossa; logo, para qualquer
comportamento, **existe um caminho server-side** — pela ordem e semântica das mensagens. Se você
está pensando em injetar uma DLL para "consertar" um bug de cliente, pare: o bug se resolve na
sequência/timing dos pacotes, e você precisa do baseline nativo antes (2.1). Já removemos a
`msgfix.dll` e a `msgprobe.dll` por isso. Injeção só se justifica para **RE/diagnóstico de dev**
(a técnica 1.2), nunca para entregar uma feature.

### 2.3 Se for injetar para diagnóstico — pelo LAUNCHER, nunca por inject.exe externo
Corolário da 2.2. Um `inject.exe` externo aciona o anti-tamper e trava o jogo. A única injeção
que sobrevive é a do próprio launcher, no lançamento suspenso do processo. Mas releia 2.2 antes:
isto é só para RE.

### 2.4 Movimento/PM/convite entre peers é P2P puro — o servidor só broka endereços
O gameplay em tempo real (posição, ataque, mensagens privadas do messenger, convites de amigo)
corre **UDP cifrado direto cliente-a-cliente**. O servidor descobre e distribui os endpoints; ele
**não** faz relay do conteúdo P2P. O tunnel TCP relay (0x2020→0x2021) é **proibido** — usá-lo foi
tentador e sempre errado. A simulação 3D é 100% client-side entre os peers; o `worldserv.exe`
original é socket + MySQL + brokering puro, sem engine.

### 2.5 O gate de movimento é client-side, e é literal
Este é o coração do bot que anda. O cliente valida a ORIGEM (IP, porta) de cada pacote de
gameplay 0x30a contra o endpoint que ele registrou para aquele slot — `IsValidUDP_ForPlayer`. O
registro vem do handshake **0x319**, gravado incondicionalmente com o (IP,porta) de origem
daquele datagrama. **Consequência prática que você não pode esquecer:** se dois componentes seus
falarem com o cliente pelo mesmo slot de endereços diferentes, o último sobrescreve e o gate passa
a rejeitar o outro. Foi exatamente a regressão do bot (§5). Um slot, uma origem.

---

## 3. As habilidades de Arquitetura (o lado .NET)

Aqui você tem controle total, então aqui a disciplina importa mais.

### 3.1 Domínio isolado de I/O — a regra que segura o projeto de pé
Regra de negócio (economia, motor de partida, progressão/exp, IA do bot) mora em **serviço de
domínio**. O handler de rede só traduz bytes↔chamada e serializa a resposta. O objeto de sessão
(`ClientSession`) é infra; o socket é infra; o DB é infra. Exemplo que ilustra o corte: o
resultado de um stage (exp/gold/rank) mora em `ProgressionService.ApplyStageResult`, **não** no
handler do opcode 0x53 nem numa partial de inventário. Quando você sentir vontade de pôr uma
regra "só ali, é mais rápido", é o sinal de que ela pertence a um serviço.

### 3.2 Como quebramos os god-files (e o padrão para os próximos)
Os monólitos originais decompilados vieram como paredões de 1000-2700 linhas. O padrão que
funcionou e está no repo: **`partial class` fatiada por domínio**. `WorldHandlers.Generated.cs`
(2692 linhas) virou `.Field.cs`, `.Room.cs`, `.Shop.cs`, `.GameResult.cs` etc., com
`RegisterGenerated()` como índice. O `WorldServer` foi desacoplado em cinco serviços
(`ItemCatalog`, `ProgressionService`, `EnchantService`, `BuddyService`, `BotManager`) mais
partials por concern. O `BotManager` mesmo saiu de seis partials de `WorldServer` para uma classe
própria que depende do servidor por **uma única lambda** (`Func<int> gameplayPort`) — todo o resto
trafega por parâmetro (`Field`), domínio (`BotPlayer`) e estáticos. Quando um grupo de handlers
crescer, fatie por domínio, não por tamanho arbitrário. Os gates medíveis estão no
[`CLAUDE.md`](../CLAUDE.md); o débito vivo em [`CODE_AUDIT.md`](CODE_AUDIT.md).

### 3.3 Golden source — UMA implementação por comportamento
Proibido manter versões paralelas. O caso clássico do projeto: um handler "antigo" e um `_Recon`
do mesmo opcode coexistindo, com a tabela de dispatch chamando um e o outro apodrecendo. **A
tabela de dispatch é a verdade; o que ela não chama, apague.** Código morto se remove (o git
guarda a história), não se comenta. Constante de protocolo tem uma só fonte — nunca uma `const`
e um literal divergentes. Ao unificar o spawn do bot recentemente, colapsei dois caminhos
quase-idênticos (`SpawnFieldBotsInStage` e o fallback do `BotTick`) num único `SpawnBotIntoRound`
justamente por isto.

### 3.4 Robustez em input externo é inegociável
Todo parse de pacote valida limites **antes de cada leitura** — reader seguro por construção. Um
frame curto ou forjado vira erro tratado, nunca `IndexOutOfRange`. SQL só parametrizado; coluna
dinâmica só de allowlist fixa. Fire-and-forget que mexe em saldo persistido precisa de rollback na
falha. Isso não é cerimônia: o cliente (e um atacante hipotético) manda bytes arbitrários, e o
servidor não pode caber num crash por causa deles. O `UdpGameplay.Process` tem essa disciplina —
veja o guard `pkt.Length < 23` que existe porque frames reliable curtos estouravam o `AsSpan(19)`.

### 3.5 Deploy = rebuild Release + reiniciar (não confie no que está rodando)
Trivial e esquecível: o servidor roda de `bin/Release/net9.0` (via `start-stack`). Um build Debug
**não** atualiza o processo em execução. O ciclo é: parar o exe → `dotnet build -c Release` →
subir. O log de verdade é o `worldserver.log`. Se você "corrigiu" algo e o comportamento não
mudou, a primeira suspeita é que você está olhando um binário velho.

---

## 4. O jeito de pensar (meta-habilidades)

As técnicas acima são específicas. Estas são o sistema operacional mental.

- **Síntese, não replay.** (§0, mas vale repetir uma terceira vez — é a alma do projeto.) O dado
  vem do domínio + constantes de protocolo nomeadas. Um dump é referência, nunca origem.

- **Baseline nativo antes de qualquer síntese nova.** Prove o que o original faz, naquele
  contexto, antes de escrever a sua versão. O cliente pune adivinhação com crash silencioso.

- **A tabela de dispatch é a fonte da verdade viva.** Quando duas coisas parecem fazer o mesmo,
  siga o dispatch para ver qual roda, e mate a outra. O que não é chamado não existe — só
  atrapalha quem lê.

- **Vocabulário de compatibilidade.** Ao descrever este trabalho, fale em "estudo de protocolo",
  "neutralizar componente para rodar offline", "compatibilidade do cliente" — não em termos de
  ataque/evasão. É research defensivo sobre software do próprio autor, e a linguagem deve refletir
  isso com honestidade.

- **Um crash tem uma causa específica; não pattern-matcheie.** Antes de mudar estado do sistema
  (reiniciar, apagar, reconfigurar) por causa de um sintoma, confirme que a evidência sustenta
  *aquela* ação. Um sinal que parece uma falha conhecida pode ter outra raiz.

---

## 5. Estudo de caso: o dia em que o bot parou de andar

Guardo este por último porque ele exercita quase toda a biblioteca de uma vez. Leia como um
exercício de leitura das técnicas em ação.

**Sintoma.** O bot aparecia no stage (renderizava via 0x4b TCP) mas ficava congelado no ponto de
spawn. Não andava.

**A tentação errada.** Palpitar no codec de movimento, mexer nos bytes do 0x30a, subir e ver.
Isso viola a §1.1 e teria queimado o dia.

**O que fiz.** Escrevi um **teste de integração fim-a-fim** que sobe o `WorldServer` e o
`UdpGameplay` reais, põe um "cliente" fake (um socket UDP) no papel do host, e roda o `BotTick`
como o motor de partida rodaria. Ele afirma três coisas concretas: o 0x319 (endpoint-register do
bot) chega ao endpoint do humano; um fluxo de 0x30a chega a ~10 Hz; e a posição codificada nesses
0x30a **avança** entre o primeiro e o último. Esse teste passou. Tradução: **a cadeia server-side
estava íntegra.** O bug não era o servidor sintetizar mal — era o cliente rejeitar o que chegava.

**A causa, à luz da §2.5.** Um mini-peer (`BotPeer.Connect`), adicionado meses antes para um
sub-projeto headless, mandava um OPEN de canal reliable (0x0304) + keepalives **do socket
dedicado do bot (porta 41xxx) direto ao cliente** — para o mesmo slot que o 0x319 já registrara
como `servidor:40708`. O cliente, obediente à sua lei literal (§2.5), re-ligou o peer do slot ao
endereço errado, e daí em diante o `IsValidUDP_ForPlayer` **rejeitou todo 0x30a relayado**. Um
slot, duas origens — o desastre exato que a §2.5 avisa.

**O fix, à luz da §3.3.** Tirei o peer/handshake do caminho do bot inteiro: o `BotNetLink` virou
só o socket UDP dedicado (origem bot→servidor); quem fala com o cliente é sempre o socket do
`UdpGameplay`, a mesma origem do 0x319 e do eco de lockstep 0x0305. O codec `RakionServer.Peer`
segue vivo — mas para o headless-H3, fora do bot. De quebra, unifiquei os dois caminhos de spawn
num golden source só (§3.3) e troquei o clamp de parede por uma **união** do box do mapa com o
hull empírico dos humanos (o hull minúsculo do começo do round clampava o bot para o quadradinho
do spawn do humano).

**A lição que fica no código.** A regra "nenhum pacote do bot fala direto com o cliente" agora
está escrita no `BotManager.Peer.cs` e em [`bot-movement-status.md`](bot-movement-status.md), e há
um teste que falha se alguém a violar de novo. É assim que uma dor vira uma barreira permanente:
não um comentário "cuidado", mas um teste que grita.

---

## 6. Mapa rápido — onde as coisas moram

| Você quer… | Vá para |
|---|---|
| Servidor do jogo (.NET, vertical slices) | `server/RakionServer/src/RakionServer.World/` |
| Motor/IA/rede do bot | `RakionServer.World/BotManager.*.cs` + `Network/BotMovement.cs` |
| Codec de peer (headless-H3, fora do bot) | `RakionServer.Peer/` |
| Frames de lobby/stage sintetizados | `Network/LobbyFrames.cs`, `CharSelect/LoginCharListWriter.cs` |
| Progressão/economia/refino (domínio) | `Services/ProgressionService.cs`, `EnchantService.cs`, `ItemCatalog.cs` |
| Broker (lista de servidores) | `RakionServer.Broker/` |
| Messenger (F9) | `RakionServer.Buddy/` + `protocol-buddy.md` |
| Launcher (modo janela, 2 clientes, registro) | `client/RakionLauncher/` |
| RE do stage PvP / combate | `docs/pvp-stage-re.md` |
| RE do monstro Cell / 0x307 | `docs/cell-monster-re.md` |
| Geometria dos mapas Golem War | `docs/*.md` (golem-war-map-geometry) + `Domain/GolemWarLayout.cs` |
| Débito de qualidade priorizado | `docs/CODE_AUDIT.md` |
| Ferramentas de captura/MITM/patch | `tools/` |

---

## 7. O que eu deixo por fazer (a mochila para o próximo)

Honestidade de saída — o que está aberto, para você não redescobrir do zero:

- **Bot in-game após o fix de §5.** O servidor está provado por teste; falta a validação com o
  cliente real na tela (o teste prova a cadeia, não os olhos). Rode `/addbot` no gravity e observe
  o bot percorrer o corredor rumo ao golem.
- **HIT×N / faísca nativa do bot.** O bot leva dano server-arbitrado e recua (feedback visível),
  mas a faísca/número nativos exigem o bot ser uma entidade real com colisão — o headless-H3 (um
  host de peer de verdade) ou um 2º cliente. Vetado o 2º cliente, o caminho é o headless. Estado
  em `docs/headless-bot-status.md`.
- **Convite/aceite de amigo P2P.** O popup de aceite é P2P direto (CCommP2P); hoje o P2P não
  alcança o peer pré-amizade e cai no relay proibido. Falta o brokering do endpoint de B antes da
  amizade existir. Detalhe na memória `convite-amigo-p2p-invitation-accept`.
- **Débito de tamanho remanescente.** `WorldDatabase.cs` (~700) e `WorldServer.cs` (~710) seguem
  acima de 600 — não são god-files do slice, são os núcleos DB e servidor, mas merecem um plano de
  split antes de 800. Ver `CODE_AUDIT.md`.

Não tente fechar tudo de uma vez. Escolha um, faça a RE completa (§1.1), tenha o baseline (§2.1),
sintetize do domínio (§0), e deixe um teste que segure a regra no lugar (§5). Foi assim que cada
peça que funciona hoje chegou aqui.

Boa sorte. Foi um bom trabalho de fazer.
