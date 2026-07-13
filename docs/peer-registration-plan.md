# Bot como PEER real — plano de registro de sessão (o caminho do HIT×N nativo)

> 2026-07-06. Confirmado com o usuário: o contador **HIT × N aparece entre 2 humanos** no PvP →
> a hittabilidade vem do avatar ser um **peer registrado na sessão da engine**, não de bytes que o
> servidor injeta. Provado por eliminação: type-7 sintético, 0x307 NPC e type-7 com template de
> jogador real — TODOS renderizam mas NENHUM é hittável. O único caminho é o bot ser um peer real.

## Verdade de referência (captura `C:\temp\p2p_loopback.pcapng`, analisada em `p2p-handshake-groundtruth.txt`)

O handshake que registra o peer (e dá colisão/HIT×N) corre **DIRETO entre os clientes** na faixa UDP
**2300-2399** (host=2301, joiner=2302), NÃO pelo nosso relay (40708/40709). Sequência real:

1. **Registro no servidor:** cada cliente manda `0x0201` (op 6 = host, op 7 = joiner) ao servidor
   (40708/40709). O servidor ecoa e broka os endpoints.
2. **Handshake direto (2302→2301 e 2301→2302):** o joiner abre o canal reliable com DOIS opens
   `0x0304` de 12B (byte6=0xff, byte7=seat destino) e troca pushes de **13B com 1 byte de payload**
   (byte6=seat do sender, byte7=seat destino, payload=seat do sender; joiner=0x0a, host=0x00). O host
   responde com os próprios pushes. **CORREÇÃO 2026-07-06 (re-análise dos 3726 frames): NÃO existe
   corpo CONNECT/STATEDELTA/CRC/ADDPLAYER — zero TAGV na captura inteira.** O canal de sessão é SÓ
   isso: opens + pushes de 1 byte + acks. O ack é o eco do frame com opcode 0x0305 e **bytes 6 E 7 =
   seat do ACKER** (l.13/17/19).
3. **Gameplay:** DEPOIS do canal vêm os `0x030a` (movimento) + `0x030f` (keystate) → peer registrado,
   `CPlayerEntity` com colisão criada em cada cliente.

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

## REVIRAVOLTA (2026-07-06, mesma noite) — o host SEMPRE engajou; o dialeto é que estava errado

Duas evidências viraram o veredito do make-or-break:

1. **Re-análise da captura completa (3726 frames): o CONNECT SE1 (TAGV/REQ_CONNECTREMOTE) NÃO existe
   no fio.** O "handshake de sessão" real entre 2 humanos é minúsculo: 2 opens de 12B + pushes de 13B
   com 1 byte de payload (o seat do sender). O mini-peer falava um protocolo que o joiner real nunca
   fala — por isso o host "só ackeava": ele ackeia qualquer 0x0304 no transporte e ignora payload que
   não é do dialeto.
2. **worldserver.log: o host JÁ MANDA o push de sessão dele PARA O BOT** — `0x0304` com byte6=0x00
   (seat do host), byte7=0x0A (seat do bot), payload 0x00, no socket do SERVIDOR (o 0x319 registrou o
   servidor como endpoint do bot), re-tentando a cada ~5s. O host nunca recusou o bot como peer; ele
   estava com a mão estendida o tempo todo. Nosso eco-clone ackeava ERRADO (mantinha bytes 6/7 do
   host em vez do seat do acker) → o host descartava o ack e re-pushava.

Conclusões antigas CORRIGIDAS: "o host não entra em modo networked-server p/ o bot" está REFUTADA
(ele pusha o canal ao bot sozinho); o lever do "join brokerado como 2º player networked" ficou
DESNECESSÁRIO para o canal (talvez ainda importe p/ outra coisa, mas não p/ abrir o lockstep).

## Implementação (2026-07-06) — lockstep real no caminho do bot
- `Network/BotLockstep.cs`: codec puro golden-tested (opens/pushes/acks byte-a-byte da captura
  l.12-23 + o push real do host no log).
- Lado do BOT (`BotManager.Peer.cs`): 2 opens por round + push periódico (1s), do socket dedicado →
  servidor → relay ao host do socket do UdpGameplay (regra fixa: nada fala direto com o cliente).
- Lado do HOST (`UdpGameplay`): push do host com destino em seat de bot → ACK correto (bytes 6/7 =
  seat do bot) no lugar do bot.
- Probe SE1 removido (BotPeer/SessionHandshake fora do caminho do World); flag `BotPeerProbe` e os
  gates de experimento do clock removidos (clock 1583 do servidor restaurado p/ solo/bot).

**Validação pendente (in-game):** com o canal fechado, o host deve parar de re-pushar a cada 5s
(acks aceitos) e — hipótese central — promover o avatar do bot a peer com colisão ⇒ HIT×N nativo.
Diagnóstico no log: `LOCKSTEP push do host -> bot seat N ACKEADO` + cadência dos pushes do host.

## Risco honesto
O ack correto + o lado-bot do canal são exatamente o que o fio dos 2 humanos mostra — mas a captura
não prova ONDE o cliente decide criar a entidade com colisão. Se o HIT×N não vier mesmo com o canal
estabelecido, o próximo passo é diffar o que o host recebe nos dois cenários DEPOIS do canal (0x830c/
0x8313/0x8315, os reliable de gameplay da captura que o bot ainda não fala).

## Reteste nativo (2026-07-13) — refutado no jogo

O reteste ocorreu depois da correção que isolou completamente o P2P humano↔humano. O bot manteve
uma máquina de handshake por humano: `OPEN 1 → ACK → OPEN 2 → ACK → PUSH → ACK → estabelecido`.
Cada frame só avança quando o cliente confirma o token correspondente; perdas repetem o mesmo frame.
As âncoras `0x830c` só começam depois do terceiro ACK, impedindo que mensagens reliable cheguem antes
da criação do canal.

Mesmo com esse fluxo, o HIT no bot não funcionou no cliente real. A opção de promover o bot sintético a
`CPlayer` apenas pelo handshake está refutada. O runtime voltou ao protocolo estável anterior e o contador
visual passou para `RakionClientCompat`, sem regra de dano no launcher.
