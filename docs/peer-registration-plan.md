# Bot como PEER real — plano de registro de sessão (o caminho do HIT×N nativo)

> 2026-07-06. Confirmado com o usuário: o contador **HIT × N aparece entre 2 humanos** no PvP →
> a hittabilidade vem do avatar ser um **peer registrado na sessão da engine**, não de bytes que o
> servidor injeta. Provado por eliminação: type-7 sintético, 0x307 NPC e type-7 com template de
> jogador real — TODOS renderizam mas NENHUM é hittável. O único caminho é o bot ser um peer real.

## Verdade de referência (captura `C:\temp\p2p_loopback.pcapng`, analisada em `p2p-handshake-groundtruth.txt`)

O handshake que registra o peer (e dá colisão/HIT×N) corre **DIRETO entre os clientes** na faixa UDP
**2300-2399** (host=2301, joiner=2302), NÃO pelo nosso relay (40708/40709). Sequência real:

1. **Registro no servidor:** cada cliente manda `0x0201` (op 6 = host, op 7 = joiner) ao servidor
   (40708/40709). O servidor ecoa e broka os endpoints. **DOIS registros = 2 players networked.**
2. **Handshake direto (2302→2301 e 2301→2302):** o joiner abre o canal reliable com `0x0304 role=0xff`
   (control/open, 2×), depois `0x0304 role=0x0a` (stream de sessão, offsets crescentes). **O host
   RESPONDE com o próprio stream `0x0304 role=0x0a`** — é o que faltava no bot. O corpo
   (CONNECT/STATEDELTA/CRC/ADDPLAYER, ~32KB) flui chunked (13B/frame), cada push com seu `0x0305` ack.
3. **Gameplay:** só DEPOIS do handshake vêm os `0x030a` (movimento) + `0x030f` (keystate) → sessão
   estabelecida, `CPlayerEntity` com colisão criada em cada cliente.

Learn-by-source (cravado `engine.dll 0x36100481`): o host aprende o endpoint do peer pela ORIGEM do
`0x0304` recebido e responde a ela. ⇒ o mini-peer pode falar do socket do nosso servidor; o host
aprende esse endpoint sozinho.

## Por que o mini-peer (`RakionServer.Peer`) empacou

O mini-peer manda `REQ_CONNECTREMOTE(7)` e o host **ecoa o `0x0305` (ack de transporte) mas NÃO
responde `REP_CONNECTREMOTE(8)`** (nem o próprio stream role=0x0a). Hipótese-mestra da captura: o
host só entra em **modo networked-server** (processa CONNECT de peers) quando vê **2 players
networked** — e isso é sinalizado pelos **dois `0x0201`** no servidor. Com 1 humano + bot sintético,
só **um** `0x0201` chega (o bot não tem cliente) → o host fica **solo** → ackeia no transporte mas
nunca processa o CONNECT no nível de sessão.

## Plano (sub-projeto, iterativo — precisa de teste in-game a cada passo)

1. **Fazer o host entrar em modo networked-server.** Bot registra um `0x0201` (op de joiner) do socket
   do mini-peer → o host vê 2 players networked. **Experimento-chave/make-or-break:** com o 2º
   registro, o host passa a RESPONDER o `REQ_CONNECTREMOTE` do bot (manda role=0x0a de volta ao
   endpoint do bot)? Diagnóstico: logar todo frame que o host manda ao endpoint do mini-peer.
2. **Completar o handshake** (`SessionHandshake`, 97 testes): CONNECT→STATEDELTA→CRC→CONNECTPLAYER →
   o host gera `SEQ_ADDPLAYER` e distribui às sessões → `CPlayerEntity` do bot com colisão. Casar o
   corpo reliable byte-a-byte contra a captura (golden).
3. **Movimento pelo peer:** os `0x030a` do bot passam a ir pelo canal do peer (não mais o type-7).
4. **HIT×N + morte:** vêm nativos (o bot é entidade real); a morte pode ser cliente-autoritativa (o
   host reporta) OU sintetizada.

## Estado atual do código
- `RakionServer.Peer` (codec do mini-peer) EXISTE e tem 97 testes, mas foi TIRADO do caminho do bot
  hoje (2026-07-06) porque, do jeito antigo, quebrava o gate 0x319 do type-7. Voltar como caminho
  PRIMÁRIO (não ao lado do type-7) elimina esse conflito.
- Caminho FUNCIONAL hoje = type-7 (anda + dano server-side, sem HIT×N). É o fallback enquanto o peer
  não fecha.

## Resultado do make-or-break (2026-07-06) — host NÃO engaja, 2 levers testados

Probe do peer ligado in-game: o bot manda o CONNECT ao endpoint P2P do host (2301) e o log `[peer]`
classificou CADA resposta. **Veredito: o host SÓ ACKeia (`0x0305`), NUNCA engaja** (zero
`REP_CONNECTREMOTE`/push role=0x0a) — 12+ respostas, todas ack-só.

- **Lever 1 — formato do CONNECT: DESCARTADO.** O CONNECT do bot está byte-correto: `TAGV=47415456`,
  ver `0x2710`=10000, op 6 — idêntico em forma ao 2º humano. O host ACKeia o frame (recebe), só não
  processa a sessão. Não é bug de formato.
- **Lever 2 — clock-authority: FALHOU.** Hipótese: o servidor dirigir o 1583 sinaliza modo
  server-driven → host não assume `ga_IsServer`. Parei o clock p/ match-com-bot; o host **continuou
  só ackeando**. Removeu o clock mas não fez o host virar session-server.

**Conclusão:** o host não entra em modo networked-server p/ o bot, e NÃO é pelo clock. O gatilho real
é mais fundo: o host decide ser servidor de sessão no START da partida, provavelmente pela contagem de
players NETWORKED (com endpoint brokerado). O bot entra por 0x38 injetado (slot sintético), não pelo
join brokerado que um 2º cliente real faz → o host conta 1 player networked → fica solo → ignora o
CONNECT. `BotPeerProbe=false` (type-7 restaurado); reativar só com o próximo lever.

## Próximo lever (não testado) — o join brokerado como 2º player networked
Fazer o host CONTAR o bot como player networked no START. Requer RE de onde o `StartPeerToPeer` do host
lê a contagem/lista de players (0x37 room-state? 0x48 field-status? session-properties?) e injetar o bot
lá como um peer com endpoint brokerado (o socket do mini-peer). É o mesmo miolo do headless §12
(session-properties/game-mode) visto do lado do host — sub-projeto aberto.

## Risco honesto
É o **núcleo-muro** do projeto (sessões anteriores investiram muito no peer/headless sem fechar). A
diferença agora: a verdade de referência está capturada e a hipótese do bloqueio (modo networked) é
concreta e testável. Ainda assim é multi-passo e depende de iteração in-game.
