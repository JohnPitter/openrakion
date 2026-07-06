# OpenRakion — Biblioteca de Habilidades (handoff)

> Documento de passagem de bastão. Tudo o que este projeto aprendeu — as técnicas que
> funcionam, as que falharam e por quê — condensado para quem continuar o trabalho.
> O código diz *o que* o sistema faz; este documento diz *como se descobre* o que ele
> precisa fazer. Leia junto com [`CODE_AUDIT.md`](CODE_AUDIT.md) (dívida viva) e os docs
> de RE por assunto (índice no fim).

**Contexto**: preservação/interoperabilidade de um jogo online descontinuado (2007, Serious
Engine 1). Sem código-fonte; binários do próprio autor; servidor .NET reconstruído de raiz
para rodar um cliente offline pessoal. Nada aqui toca serviço de terceiros.

---

## 1. Mapa do sistema (o que existe e onde)

```
server/RakionServer/src/
  RakionServer.Broker      — diretório de servidores (porta 40706); world se registra aqui;
                             o launcher decide "online" consultando o broker
  RakionServer.World       — O CORAÇÃO: TCP 40708 (login/lobby/sala/stage) + UDP 40708/40709
                             (gameplay). Vertical slices em partial class por domínio
                             (WorldHandlers.<Dominio>.cs, ClientSession.<Concern>.cs) +
                             serviços de domínio (Services/, BotManager, ProgressionService…)
  RakionServer.Buddy       — messenger F9 (porta própria; registro 0x94, presença, P2P broker)
  RakionServer.Accounts    — registro de conta via launcher (POST /register)
  RakionServer.Admin       — plataforma Blazor (:8080)
  RakionServer.LauncherWeb — launcher web (:80) + /launcherlogin + /fetch
  RakionServer.Peer        — codec do netcode SE1 (handshake CONNECT/CRC/ADDPLAYER, reliable
                             stream) — HOJE só p/ o sub-projeto headless-H3 e testes; NÃO fala
                             com o cliente no caminho do bot (ver §4.6)
  RakionServer.EngineHost  — host .NET x86 da engine.dll real (headless, experimental)
client/RakionLauncher      — launcher WinForms (.NET 9): patches de modo janela/mutex,
                             lançamento suspenso, multi-instância. ZERO injeção de DLL.
tools/                     — capturas MITM, decoders, patchers, frida, XFS (ver §5)
docs/                      — RE por assunto (ver índice §8)
```

Banco: MySQL (`user` → `usergameinfo` → `characterinfo`, `itembox`, `messenger_session`).
Senha em texto plano em `user.password` (projeto pessoal offline — não expor).
Deploy: **parar o exe → `dotnet build -c Release` → subir** (start-stack.ps1). O server roda
de `bin/Release/net9.0`; build Debug NÃO atualiza o processo em execução. Log: `worldserver.log`.

---

## 2. As três leis do projeto

Estas não são estilo — cada uma nasceu de dias de depuração. Violar qualquer uma custa caro.

**Lei 1 — Síntese, nunca replay.** Todo frame que o servidor emite é serializado do domínio
(estado → DTO → bytes) com constantes de protocolo nomeadas. Captura/MITM/oracle são *research
e golden test* (validar a síntese byte-a-byte), nunca a implementação. Os replays legados
(oracle_0c.bin, blobs `_rNN`) foram todos migrados para síntese pura (`LoginCharListWriter`,
`LobbyFrames`) com golden tests contra a captura.

**Lei 2 — Só mandar ao cliente formas de frame que ele já viu.** O cliente é um binário
fechado de 2007 sem tolerância a erro: um frame de formato desconhecido trava ou crasha.
Antes de emitir qualquer coisa nova: (a) capture o servidor original emitindo, ou (b) crave o
leitor por RE e valide com golden test. Palpite→testa→ajusta não funciona aqui — cada teste
in-game custa minutos e um crash contamina o diagnóstico.

**Lei 3 — Server-side sempre; nenhuma DLL injetada para funcionalidade.** O jogo original
rodava sem DLL alguma, logo *sempre existe um caminho server-side* — geralmente é questão de
ORDEM/semântica das mensagens (ex.: o F9 vazio era um header de 8 bytes, não 6; resolvido no
servidor). Injeção só para RE/diagnóstico de desenvolvimento, e mesmo assim SEMPRE pelo
launcher com processo suspenso (inject.exe externo dispara o anti-tamper e trava o jogo).

---

## 3. Habilidades de engenharia reversa

### 3.1 RE completa ANTES de testar
Reconstruir a função inteira no Ghidra e cravar o layout byte-a-byte ANTES de implementar.
Teste in-game só quando o entendimento está sólido. O ciclo palpite→teste é lento (subir
stack, logar, criar sala…) e resultados ambíguos: um fix "quase certo" falha igual a um
totalmente errado.

### 3.2 Fontes de verdade, na ordem de custo
1. **engine.dll TEM SÍMBOLOS** (é a Serious Engine 1 da Croteam — open source, 8736 exports
   casam). O netcode inteiro está em fonte pública: **leia a fonte antes de reverter cego**.
   122 senders `Send*` em `IScavengerWorldNet`; dispatch RX do cliente em
   `ProcessWorldRecvBuffer@0x36197a40`. Siga os 7 bytes do engine.dll.
2. **Captura MITM** do servidor original (container `openrakion-server:latest`) — decifra o
   TCP (AES-128-ECB, chave no script; a cifra É intencional do jogo, não "corrija") e loga
   tudo. Uma captura de sessão real fecha formato+entrega+ordem de uma vez — foi assim que
   0x0C, lobby, inventário, 0x37, 0x38 e o spawn de stage foram cravados.
3. **Captura-diff**: duas sessões com UMA variável diferente (ex.: nome de char) → os bytes
   que mudam são o campo. Fechou o formato completo do 0x0C.
4. **RE estática** (Ghidra): rakion.bin (x86, base 0x400000, sem ASLR), worldserv.exe,
   engine.dll (base 0x36000000). Projetos Ghidra e binários ficam FORA do git, em
   `Desenvolvimento\Rakion`.

### 3.3 Diagnóstico runtime quando a RE estática empaca
Quando cada fix baseado em RE estática falha in-game, PARE de refinar a análise e troque por
um **efeito visível no ponto suspeito** (um log, um beep, um botão que aparece) — o teste
então crava ONDE quebra, não "se" quebra. Foi o que destravou o botão nativo Add Bot.

### 3.4 Crash de cliente sem debugger
O anti-debug bloqueia procdump/anexar. O **Event Log do Windows** (Application Error) entrega
módulo+offset do crash sem privilégio → `objdump` no RVA identifica a função. Barato e sempre
disponível.

### 3.5 Cuidado com o binário errado
`rakion-final` (o jogo real, 2007) ≠ `rakion-new` (binário usado em parte da RE estática).
`entitiesmp.dll` difere — **offsets não transferem** (explicou HIT×N e 0x307 "impossíveis").
engine.dll é estável entre builds; o resto, confirme no binário vivo antes de confiar num
offset. RE ao vivo em rakion-final = DLL de diagnóstico carregada pelo launcher.

### 3.6 Frida e patch estático
`tools/frida_*.py` (hooks pontuais; a UI congela com hooks intrusivos — prefira rastreio
estático p/ UI), `tools/patch_*.py` (patches de bytes com caves; sempre com script de
restauração `swap_*`/`orig_restore`). O launcher aplica os patches estáveis (mutex 2-clientes
@0x402C96, modo janela) em memória, no lançamento suspenso.

---

## 4. Habilidades de protocolo (o conhecimento duro)

### 4.1 Canais e cifras
- TCP world: frames `[u16 size][conteúdo]`, conteúdo cifrado AES-128-ECB (chave fixa do jogo).
  O texto C→S chega em claro (a cifra é só outbound do servidor... conferir por opcode).
- UDP gameplay (40708/40709): handshake ping/echo `0x0202`/`0x0201`, relógio `1583`,
  CNetMessages `0x03xx` (unreliable) / `0x83xx` (reliable, bit 0x8000).
- Opcodes de WORLD (pequenos: 0x38 join, 0x43 start, 0x47 chat de sala/stage, 0x22 chat do
  canal, 0x4b spawn, 0x4f morte…) ≠ CNetMessages de GAMEPLAY (0x30a move, 0x30f keystate,
  0x311 golpe, 0x307 CreateNpc, 0x319 registro de endpoint). Dois espaços de numeração.

### 4.2 Handles/tokens são ponteiros ecoados
Os "tokens" dos frames de lobby/stage são PONTEIROS do processo do servidor original que o
cliente apenas ECOA. Gere por sessão (estável, != 0) e correlacione pelo eco — nunca copie de
captura. O token-echo do P2P é o socket do `getsockname` (broker de endpoint do buddy).

### 4.3 O combate é cliente-autoritativo
A VÍTIMA reporta a própria morte (0x4f). O servidor arbitra só o que o cliente não pode:
hit humano→bot (bot não tem cliente), dano ao golem, W.O. Não tente "validar" dano no
servidor — o original não valida.

### 4.4 Movimento é SÓ UDP 0x30a
Corpo de 19B cravado (dt, actState|slot, x/y/z packed ×100, heading em graus, subFrame nonce,
aim-vec). O slot do dono vai DUAS vezes (srcSlot do header E 5 bits baixos do actState) e
precisam bater. O 0x30f (keystate) acompanha SEMPRE o 0x30a. Detalhe completo:
[`bot-movement-capture.md`](bot-movement-capture.md) e [`pvp-stage-re.md`](pvp-stage-re.md).

### 4.5 O gate de movimento do cliente (a pegadinha nº 1)
O cliente só APLICA um 0x30a de um peer se:
1. o avatar do slot existe (0x4b AddPlayer prévio, TCP);
2. `playerRec[slot].addr/port` == origem (IP,porta) do datagrama — gravado EXCLUSIVAMENTE
   pelo **0x319** (handler grava a origem do datagrama incondicionalmente);
3. o lockstep de sessão andou: o host manda 0x0304 ao endpoint do peer e espera o eco
   **0x0305** (o UdpGameplay ecoa no lugar do bot).

### 4.6 REGRA: uma ÚNICA origem fala com o cliente
Corolário do 4.5 que já causou UMA regressão real (bot congelado, 2026-07): **nenhum pacote
sintetizado fala com o cliente de outro socket**. O socket do `UdpGameplay` é a única origem
client-facing (é ele que o 0x319 registra). O mini-peer que abria canal reliable do socket do
bot (41xxx) direto ao cliente re-ligava o peer do slot ao endpoint errado → todos os 0x30a
relayados passavam a ser rejeitados. O bot emite do socket dedicado dele APENAS rumo ao
servidor (loopback), que relaya do socket certo. Prova automatizada: `BotMovementChainTests`.

### 4.7 Loopback multi-ator (2 clientes + bots no mesmo IP)
Tudo é 127.0.0.1, então identidade por IP quebra. As desambiguações que funcionam:
- porta de BOT ≥ 41000 nunca registra endpoint de humano (`BotUdpPortBase`);
- pacote de gameplay é do BOT se o srcSlot (offset 6) aponta um seat de bot no field;
- ping UDP resolve pela sessão que ainda ESPERA endpoint (o cliente manda slot=0 fixo);
- messenger: identidade por IP + proximidade de porta (`BuddyIdentity`).

### 4.8 P2P do messenger é P2P DE VERDADE
PM/convite correm UDP cifrado direto cliente↔cliente; o servidor só broka endereços.
O tunnel TCP relay 0x2020→0x2021 é PROIBIDO (feedback fixo do dono). Lacuna conhecida:
endpoint de B pré-amizade.

---

## 5. Habilidades de tooling (o arsenal em `tools/`)

| Ferramenta | Para quê |
|---|---|
| `mitm_botcap.py` / `mitm_cap.py` / `mitm_connectcap.py` | Proxy MITM TCP+UDP com decifra AES; loga W→C/C→W |
| `decode_bot_action.py` | Decodifica envelope + corpo 0x30a de uma captura |
| `analyze_p2p_pcap.py` / `cap_p2p_loopback.ps1` | Captura e análise do P2P em loopback |
| `patch_botbtn.py` + `swap_botbtn.ps1` | Botão nativo Add Bot (patch com cave + restore) |
| `frida_roombtn.py` / `frida_findroom.py` | Hooks de runtime na UI da sala |
| `xfs_read.py` / `xfs_repack.py` | Assets XFS (multi-chunk zlib) — items.dat, LevelsSV.xfs |
| `orig_capture.ps1` / `orig_restore.ps1` | Alternar stack original (Docker) ↔ nosso p/ captura |
| `difftest.py` / `listprobe.py` / `worldprobe.py` | Sondas de protocolo diversas |

Receita de captura sem tocar no cliente: o endereço do world vem do **broker** — registre no
broker um world cujo ip/port aponta pro proxy, e o proxy encaminha ao original (Docker).
Ambiente de teste do buddy isolado: container MySQL :13306 + Buddy :18500.

---

## 6. Habilidades de código (como este repo se mantém são)

- **Golden source**: UMA implementação por comportamento. A tabela de dispatch é a verdade;
  handler que ela não chama, apague. Constante de protocolo tem uma só fonte.
- **Fatiar por domínio em `partial class`** quando um grupo de handlers cresce
  (`WorldHandlers.<Domínio>.cs`). Gates mensuráveis: função >60 linhas sinaliza; arquivo >600
  sinaliza, ~800 exige plano de split. Dívida atual e histórico em [`CODE_AUDIT.md`](CODE_AUDIT.md).
- **Domínio ≠ rede**: regra de progressão/partida mora em serviço de domínio
  (`ProgressionService`, `Field`, `BotManager`); o handler só traduz bytes↔chamada.
- **Reader seguro por construção**: bounds-check antes de cada leitura; frame curto/forjado
  nunca vira exceção. SQL só parametrizado.
- **Comentários de RE são documentação do protocolo** (`FUN_xxxx`, offsets `this+0x...`) —
  mantenha-os; são o mapa de volta ao binário.
- **Testes**: golden tests byte-a-byte p/ todo frame sintetizado (contra captura real);
  testes de domínio puros (Field/BotPlayer sem rede); e testes de integração com sockets
  reais + reflection p/ estado privado (`BotMovementChainTests` é o modelo — WorldServer +
  UdpGameplay reais, cliente fake, prova a cadeia inteira sem o rakion.exe). Suíte: 166.

---

## 7. Frentes abertas (por onde continuar)

1. **Hittability nativa do bot (HIT×N/faísca)** — exige o bot como `CPlayerEntity` real
   (type-7 via canal reliable de um peer REAL). Caminhos: headless-H3 (hospedar a engine.dll
   num host .NET x86 — fundação provada, crash do AddPlayer documentado em
   [`headless-engine-re.md`](headless-engine-re.md) §12) ou aceitar a mitigação atual
   (recuo server-arbitrado). 0x307/NPC foi descartado (dossiê em §4 do
   [`bot-movement-status.md`](bot-movement-status.md)).
2. **Validação 2-clientes-servidor** do PvP humano×humano (relay 0x30a humano→humano já
   implementado; "modo observação" do 2º humano tem RE em [`pvp-stage-re.md`](pvp-stage-re.md) §10).
3. **Messenger: endpoint de B pré-amizade** (convite P2P puro; sonda `DiagTunnelPacket`).
4. **Dano real do golem / fórmulas de combate** (placeholders marcados no código).
5. **Pós-clear do stage solo** (pendente de captura).

---

## 8. Índice dos documentos de RE

| Doc | Assunto |
|---|---|
| [`protocol-world.md`](protocol-world.md) | Opcodes do world TCP |
| [`protocol-buddy.md`](protocol-buddy.md) | Messenger F9 (Buddy2.dll) |
| [`pvp-stage-re.md`](pvp-stage-re.md) | Stage PvP completo: transporte, spawn, movimento, combate, rounds |
| [`bot-movement-status.md`](bot-movement-status.md) | Estado do bot + regressão do mini-peer (regra §4.6) |
| [`bot-movement-capture.md`](bot-movement-capture.md) | Wire do 0x30a/0x30f/0x311/0x319 |
| [`cell-monster-re.md`](cell-monster-re.md) | Monstros Cell (CNpc, 0x307) |
| [`headless-engine-re.md`](headless-engine-re.md) / [`headless-bot-status.md`](headless-bot-status.md) | Hospedar a engine.dll headless (H1–H3) |
| [`known-issues.md`](known-issues.md) | Bugs conhecidos e wire fixes |
| [`config-xfs.md`](config-xfs.md) | Formato XFS dos assets |
| [`gameguard.md`](gameguard.md) | Neutralização do componente p/ rodar offline |
| [`setup-guide.md`](setup-guide.md) | Subir o ambiente do zero |
| [`CODE_AUDIT.md`](CODE_AUDIT.md) | Auditoria vi