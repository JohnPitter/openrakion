using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.Handlers;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Sessao de um cliente TCP. Replica o frame/seq do worldserv.exe:
    ///   frame = [u16 size][u16 A][u16 B][data]  (size inclui o proprio campo).
    ///   cliente->servidor: A=opcode, B=seq (validado == ultimoSeq+1, wrap 65000;
    ///   0x0C/0x0F isentos, 0x0C reseta). servidor->cliente: A=serverSeq++, B=msgType.
    /// Ver PROTOCOL.md (FUN_0042bd70 / FUN_0042ab40 / FUN_0041b940 / FUN_004048e0).
    /// </summary>
    public sealed class ClientSession
    {
        public const int SeqWrap = 65000;

        private readonly Socket _sock;
        private readonly WorldServer _server;
        private readonly CancellationTokenSource _cts = new();

        // estado de usuario (espelha campos do objeto user[slot])
        public ushort Slot { get; }
        public string RemoteIp { get; }
        public bool Connected { get; private set; }          // user+0x1440
        public bool Authenticated { get; set; }              // this+0x5b18 == 0 ? login : in-game
        public bool SlotActive { get; set; }                 // user+0x1460
        public bool SecondActive { get; set; }               // user+0x14a4
        public byte ConnType { get; set; }
        public string ExpectedSessionName { get; set; } = "";
        public string UserId { get; set; } = "";
        public WorldDatabaseInfo? Game { get; set; }

        // estado de lobby/jogo (campos nomeados do user[slot])
        public byte Status { get; set; }                     // user+0x1440 (ver Domain.UserStatus)
        public byte SubStatus { get; set; }                  // user+0x146c (papel na sala)
        public int GroupId { get; set; }                     // user+0x1450 (canal/IDC selecionado)
        public string CharName { get; set; } = "";           // nome do personagem
        public bool InField { get; set; }                    // user+0x1460 != 0 (dentro de uma sala/field)
        public bool FieldSecondary { get; set; }             // user+0x14a4 != 0
        public int FieldId { get; set; } = -1;               // field atual (indice em World.Fields)
        public int RoomId { get; set; } = -1;                // room/chat atual (indice em World.Rooms)
        public System.Net.IPEndPoint? UdpEndpoint { get; set; } // endpoint UDP de gameplay (aprendido)
        public byte GameSeq;                                    // relogio/frame da partida (tick 1583); avanca = timer corre
        public byte LastInput = 5;                              // ultimo valor de input do cliente (0040 pkt[7]); o server ECOA no 1583
        public byte LastGameplayFeedbackSeq;                    // ultimo seq ecoado pelo cliente em 1583 client->world
        public byte LastGameplayFeedbackState;                  // ultimo estado ecoado pelo cliente em 1583 client->world
        private int _gameClockStarted;
        public uint UdpKey { get; set; }                     // user+0x1464 (chave de sessao UDP, validada nos pacotes)
        public int FieldHandleRaw { get; set; }              // valor cru de user+0x1460
        public int FieldSecondaryRaw { get; set; }           // valor cru de user+0x14a4
        public byte VerifyMode { get; set; }                 // user+0x237c (tipo de conexao p/ MD5)
        public int Ping { get; set; }                        // ultimo ping reportado
        public uint Gold { get; set; }                       // user+0x1538 (em jogo)
        public uint Cash { get; set; }                       // user+0x153c (em jogo)
        // --- estado de COMPRA / char ativo (shop 0x2e) ---
        public int ActiveCharId { get; set; } = -1;          // characterinfo.id do char ativo -> useriteminfo.characterid
        public int GameInfoId { get; set; } = -1;            // usergameinfo.id -> AddGoldAsync / useriteminfo.userid
        public volatile bool ShopBuyInProgress;              // espelha user+0x144c==2 (anti-duplo-clique)
        public System.Collections.Generic.List<int> BoxItems { get; set; } = new(); // itembox (armazem) carregado no login: exibido no box + a contagem = proximo slot da compra
        public System.Collections.Generic.List<RakionServer.World.Database.UserItem> Items { get; set; } = new(); // inventario (useriteminfo) p/ o Box (0x2f)
        public byte CharLevel { get; set; } = 1;             // nivel do char ativo -> overlay 0x0C @96 (offset cravado no diff golden)
        public byte CharClass { get; set; }                  // classe do char ativo -> curva de level (classlevelinfo)
        public long CharExp { get; set; }                    // exp TOTAL acumulado do char ativo (level-up 0x50)
        public uint CharWin { get; set; }                    // -> overlay 0x0C @73 (captura-diff)
        public uint CharLose { get; set; }                   // -> overlay 0x0C @77
        public uint CharDraw { get; set; }                   // -> overlay 0x0C @81
        public uint CharLevelPoint { get; set; }             // pontos de level p/ distribuir -> overlay 0x0C @101
        public ushort[] Stats { get; } = new ushort[10];     // stats alocados (this+0x1568+idx*2) p/ a alocacao 0x33
        public string Md5Hash1 { get; set; } = "";           // MD5 do client (modo 0)
        public string Md5Hash2 { get; set; } = "";           // MD5 do client (modo 1)
        public byte PendingRoomMap;                          // map do 0x3b (sala criada) -> aplicado ao Field no 0x4b
        public byte PendingRoomMode;                         // mode do 0x3b: 0=stage (client-side), !=0=Battle/PvP (networked)
        public ushort PendingRoomDurationSec;                // duracao do round em SEGUNDOS (u16 do 0x3b, 290..1210)
        public byte PendingRoomRounds;                       // rounds configurados na sala (byte do 0x3b, <0x16; stage=1)

        // estado de combate/field (campos do user[slot] resolvidos por FUN_0040b7d0 e helpers de field)
        public ushort FieldTargetIndex; // user+0x14a0 (indice do field-objeto alvo resolvido por FUN_0040b7d0)
        public byte FieldTargetOwner;  // user+0x14a2 (byte de owner/slot do alvo resolvido por FUN_0040b7d0)
        public uint FieldCash;         // user+0x1534 (saldo de cash/pontos em campo; debitado por FUN_0040b900)
        public byte FieldCashCost;     // user+0x1531 (custo base usado por FUN_0040b900: cost = (this+0x1531>>1) + slot*5)
        public short FieldPairA;          // playerRecord (field+0xe4 + slot*0x3c0) +0x2c4
        public short FieldPairB;          // playerRecord +0x2c6
        public byte FieldDirFlag;         // playerRecord +0x2bf
        public byte FieldDirCount;        // playerRecord +0x2c0
        public byte FieldRespawnCount;    // playerRecord +0x2c1
        public byte FieldPlayState = 1;   // playerRecord +0x2b4 (1=jogando, 2=derrotado/respawn)
        public byte FieldRecordState;     // playerRecord +0x8 (estado do registro; 2 = inativo p/ acao)
        public ushort FieldTargetA;       // playerRecord +0x2c8 (alvo/objetivo, arg0<10)
        public ushort FieldTargetB;       // playerRecord +0x2ca (alvo/objetivo, arg0>=10)
        public ushort FieldObjectIndex; // user+0x14a0 (indice deste user no array de field-objects, lido por FUN_0040b7d0)
        public byte FieldSeat;          // user+0x14a2 (byte de seat/owner do user no field, lido por FUN_0040b7d0)
        public bool ExpBonusActive;     // user+0x236c != 0 (multiplicador de exp x3/2 ativo)

        private ushort _clientSeq;   // user+0x146e (ultimo seq recebido)
        private byte[] _invReqBody = System.Array.Empty<byte>();  // body do 0x2c (SlotActive/user14a4 do cliente) p/ ecoar no 0x12/0x13
        // Maquina de estado do inventario = user+0x144c do worldserv.exe (FUN_0040b000 no 0x2c,
        // FUN_0040c960 no 0x2d). 0=fechado, 1=aberto (pos-enter, aguardando a 1a list), 2=loja.
        // O 0x2d so' responde a LISTA (0x13) na 1a chamada (estado 1 -> 0); nas seguintes responde
        // o ACK curto. Sem isto remandavamos 0x13 a cada 0x2d, e o 2o 0x2d (ao sair) recebia outra
        // lista em vez do ack -> o cliente reprocessava o grid e caia no CHAR-SELECT no Previous.
        private byte _invState;
        // HANDLE de sessao = bytes 13..16 do 0x0C (login). CAPTURA do worldserv ORIGINAL (mitm
        // inventario→Previous, 2026-06-11): os acks 0x2c/0x2d ECOAM esse handle (8deb863f no
        // original), NAO o body do cliente. Com o handle errado o cliente nao reconhecia o estado do
        // inventario e ficava em polling/sobreposto. Default = handle do oraculo (b3c3863f), sobrescrito
        // do 0x0C real no login.
        private byte[] _invHandle = new byte[] { 0xb3, 0xc3, 0x86, 0x3f };
        private bool _r36bSent;   // 0x36b (arma a lista de games) so' 1x; remandar a cada poll travava o cliente
        // Diagnóstico concluído (2026-06-10): com inventário VAZIO o cliente AINDA crasha no Previous
        // -> crash é 100% client-side (csComponent::PrevChild, lista de widgets corrompida no teardown),
        // independente dos dados do servidor. Conserto = patch no uitoolkit.dll (guard de alinhamento).
        // Flag mantido em false (box volta a aparecer normalmente).
        private const bool DiagEmptyInventory = false;

        /// <summary>
        /// Cifra do canal lobby/field (AES-128, chave/IV reais do worldserv.exe). No original
        /// e criada e LIGADA no setup da conexao (FUN_00403c10 -> FUN_00401000/00401200,
        /// ctx+0x208=3). Replicamos habilitando-a no Start().
        /// </summary>
        public readonly PacketCrypto Crypto = new();

        public ClientSession(Socket sock, ushort slot, WorldServer server)
        {
            _sock = sock;
            Slot = slot;
            _server = server;
            RemoteIp = (sock.RemoteEndPoint as IPEndPoint)?.Address.ToString() ?? "?";
        }

        public void Start()
        {
            Connected = true; // evento de conexao (opcode 0 / connect) marca user+0x1440
            Crypto.EnableWorldDefault(); // cifra do canal lobby ligada no setup da conexao (ctx+0x208=3)
            Log.Info("client", "[{0}] conectado de {1}", Slot, RemoteIp);
            _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
        }

        private async Task ReceiveLoopAsync(CancellationToken ct)
        {
            byte[] buffer = new byte[16384];
            int have = 0;
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    int n = await _sock.ReceiveAsync(
                        new ArraySegment<byte>(buffer, have, buffer.Length - have), SocketFlags.None, ct);
                    if (n <= 0) { Log.Warn("client", "[{0}] recv retornou {1} (peer fechou)", Slot, n); break; }
                    Log.Info("client", "[{0}] RX {1} bytes: {2}", Slot, n, Convert.ToHexString(buffer, have, n));
                    have += n;

                    int consumed = 0;
                    while (have - consumed >= 2)
                    {
                        ushort size = BinaryPrimitives.ReadUInt16LittleEndian(buffer.AsSpan(consumed));
                        if (size < 4 || size > buffer.Length) { Log.Warn("client", "[{0}] frame size invalido {1}", Slot, size); _ = CloseAsync(); return; }
                        if (have - consumed < size) break; // frame incompleto

                        // O client CIFRA todo o conteudo apos o size (AES, cada bloco de 16
                        // -> 12 bytes de plaintext, prefixo IV 0xc47f). Decifra ANTES de ler
                        // opcode/seq/data. Confirmado: 1o pacote = login 0x0C cifrado.
                        int contentLen = size - 2;
                        byte[] content;
                        if (Crypto.Enabled && contentLen >= 16 && contentLen % 16 == 0)
                            content = Crypto.Decrypt(buffer.AsSpan(consumed + 2, contentLen));
                        else
                        {
                            content = new byte[contentLen];
                            Array.Copy(buffer, consumed + 2, content, 0, contentLen);
                        }
                        consumed += size;
                        if (content.Length < 4) { Log.Warn("client", "[{0}] conteudo curto ({1}B) apos decifrar", Slot, content.Length); continue; }

                        // plaintext = [u16 opcode][u16 seq][data]
                        ushort opcode = BinaryPrimitives.ReadUInt16LittleEndian(content.AsSpan(0));
                        ushort seq = BinaryPrimitives.ReadUInt16LittleEndian(content.AsSpan(2));
                        byte[] data = new byte[content.Length - 4];
                        Array.Copy(content, 4, data, 0, data.Length);

                        Log.Debug("client", "[{0}] <- opcode={1:X4} seq={2} data={3}", Slot, opcode, seq, Convert.ToHexString(data));
                        await DispatchAsync(opcode, seq, data);
                    }

                    if (consumed > 0)
                    {
                        Array.Copy(buffer, consumed, buffer, 0, have - consumed);
                        have -= consumed;
                    }
                    else if (have == buffer.Length)
                    {
                        Log.Warn("client", "[{0}] buffer cheio sem frame — desconectando", Slot);
                        break;
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (SocketException ex) { Log.Warn("client", "[{0}] socket: {1} ({2})", Slot, ex.Message, ex.SocketErrorCode); }
            catch (Exception ex) { Log.Error("client", "[{0}] recv: {1}", Slot, ex.Message); }
            finally { await CloseAsync(); }
        }

        /// <summary>Replica FUN_0042bd70: checagem de seq + roteamento por opcode.</summary>
        private async Task DispatchAsync(ushort opcode, ushort seq, byte[] data)
        {
            if (!Connected) return;

            if (opcode == Protocol.Op.Login)
            {
                _clientSeq = 0; // login zera o contador (user+0x146e=0)
                if (!Authenticated)
                {
                    await LoginHandler.HandleAsync(_server, this, data);
                    return;
                }
                // ja autenticado: opcode 0x0C cairia no in-game (FUN_0042a310)
                Log.Debug("client", "[{0}] opcode 0x0C pos-login (in-game)", Slot);
                return;
            }

            if (opcode != 0x0F)
            {
                // checagem de sequencia
                int expected = _clientSeq + 1;
                if (expected > SeqWrap) expected = 0;
                if (seq != expected)
                {
                    Log.Warn("client", "[{0}] seq invalida (got {1}, esperado {2}) -> DISC 2", Slot, seq, expected);
                    Disconnect(2);
                    return;
                }
                _clientSeq = seq;
            }

            DispatchOpcode(opcode, data);
        }

        private void DispatchOpcode(ushort opcode, byte[] data)
        {
            // Sequencia de ENTRADA NO LOBBY/CAMPO (capturada do world ORIGINAL via MITM, ver
            // capture_field_entry/PROTOCOL_field_entry.md). Intercepta antes do dispatch generico.
            if (TryHandleLobbyEntry(opcode, data)) return;

            // Replica o switch de FUN_0042ab40: cada opcode -> handler nomeado em
            // WorldHandlers; opcode fora da tabela -> Disconnect(0xc9).
            WorldHandlers.Dispatch(new HandlerContext(_server, this, opcode, new PacketReader(data), data));
        }

        // --- ENTRADA NO LOBBY/CAMPO (replay do world ORIGINAL, sessao e7030064) ----------------
        // Apos o char select, o cliente faz o handshake UDP (echo 0x0201) e manda esta cadeia TCP;
        // o world responde levando-o ate a LISTA DE CANAIS (0x1e "dchannel01") e a sala/stage.
        private static byte[] Hex(string h) => Convert.FromHexString(h);
        private static readonly byte[] _r10 = Hex("10004e95dd29ce3a55db20b6ad97a65cc01c000000000000"); // GG challenge
        private static readonly byte[] _r14 = Hex("1400000020000000648c0509"); // 0x14 SPAWN: [00 00][20000000][handle]; ALINHADO ao frida do original (arma o scoring/combo)
        private static readonly byte[] _r1f = Hex("1f000000e7034a500001000000000008e703000000000000"); // e703=user id 999 do NOSSO 'test' — o cliente valida (sem ele trava no char-select re-mandando 0x14)
        private static readonly byte[] _r1e = Hex("1e000001646368616e6e656c3031000000e7034a500001000000000052a15ef61ea2c65b"); // e703=user id 999 do NOSSO 'test' (revertido — cliente valida)
        private static readonly byte[] _r36 = Hex("3600000020000000648c0509"); // ALINHADO ao frida do original ([00 00][20000000][handle], = 0x14)
        // SEGUNDO 0x36 (mitm_full_113423: o original o manda apos o ack UDP 0d03 do cliente; e' ele
        // que ARMA a lista de games — sem ele o botao Create do channel lobby nao faz nada).
        private static readonly byte[] _r36b = Hex("3600006a4dcaaf2b4b3d9fa5");
        private static readonly byte[] _r3b = Hex("3b00000000538b003600007f"); // ALINHADO ao world real (frida)
        private static readonly byte[] _r43 = Hex("43000008010142003b000000"); // ALINHADO ao world real (frida)
        private static readonly byte[] _r48 = Hex("480001b3010000141400a00f"); // ALINHADO ao world real (frida)
        // CONCLUSAO DO STAGE (capturado via frida no world original): cliente manda 0x4A data[0]=0x02
        // (StageClear) -> world responde 0x4A (resultado) + 0x44 (fim de partida -> volta ao lobby).
        private static readonly byte[] _r4a = Hex("4a0002010100737624007c04");      // 0x4A resultado do clear
        private static readonly byte[] _r44 = Hex("440002000100000061736464");      // 0x44 match-end (reason=02 + nome sala)
        // REFRESH do lobby PÓS-CLEAR (capturado do world ORIGINAL via frida — versões DIFERENTES das de
        // entrada _r1f/_r1e/_r36). O original manda estas APÓS o 0x44; reusar as de entrada pulava o Rank.
        private static readonly byte[] _r1f_clear = Hex("1f000000e7034a50000100000000000200cae202598fdc71");
        private static readonly byte[] _r1e_clear = Hex("1e000001646368616e6e656c3031000000e7034a500001000000000030002503e7030000");
        private static readonly byte[] _r36_clear = Hex("36000022be9a400002000000");

        /// <summary>
        /// 0x0e = OnRecvSuccessUDP. ALINHADO ao frida do world ORIGINAL: ecoa o ENDPOINT DO CLIENTE
        /// (IP+porta que o cliente mandou no ping UDP, 127.0.0.1:2301=08fd big-endian) nos DOIS slots +
        /// trailer ZEROS. Antes mandavamos as portas do SERVER (9f04/9f05) + um "key" inventado -> hipotese:
        /// isso punha o cliente em modo server-authoritative e SUPRIMIA o combo HIT×N (client-side). Ecoar
        /// o endpoint do cliente (como o original) = modo local-scored. Usa o UdpEndpoint aprendido; cai
        /// p/ 2301 se ainda nao conhecido. Formato: [0e 00 00][IP1 4][port1 BE 2][IP2 4][port2 BE 2][9x00].
        /// </summary>
        private byte[] BuildEndpoints0e()
        {
            var ep = UdpEndpoint;
            byte[] ip = ep != null ? ep.Address.MapToIPv4().GetAddressBytes() : new byte[] { 127, 0, 0, 1 };
            ushort port = (ushort)(ep != null ? ep.Port : 2301);
            byte portHi = (byte)(port >> 8), portLo = (byte)(port & 0xff); // big-endian (08 fd p/ 2301)
            byte[] f = new byte[24];
            f[0] = 0x0e; f[1] = 0x00; f[2] = 0x00;
            System.Array.Copy(ip, 0, f, 3, 4); f[7] = portHi; f[8] = portLo;   // endpoint 1 = cliente
            System.Array.Copy(ip, 0, f, 9, 4); f[13] = portHi; f[14] = portLo; // endpoint 2 = cliente
            // f[15..23] = 0 (trailer zeros, igual ao original)
            return f;
        }

        private bool TryHandleLobbyEntry(ushort opcode, byte[] data)
        {
            switch (opcode)
            {
                case 0x0e: // pedido de UDP-success -> endpoints + GG
                    SendEncryptedFrame(BuildEndpoints0e());
                    SendEncryptedFrame(_r10);
                    Log.Ok("lobby", "[{0}] 0x0e (endpoints 40708/40709) + 0x10 enviados", Slot);
                    return true;
                case 0x14: // -> ack + info sessao (0x1f) + LISTA DE CANAIS (0x1e). Tb marca o "channel lobby":
                    // InField/FieldSecondary/Status=2 (FieldLobby) p/ os opcodes de sala/shop (0x2d/2e/2f) passarem
                    // o gate a partir do Game List (Roomstate Fase A — habilita o inventario/shop).
                    InField = true; FieldSecondary = true; SecondActive = true; Status = 2;
                    SendEncryptedFrame(_r14);
                    SendEncryptedFrame(_r1f);
                    SendEncryptedFrame(_r1e);
                    Log.Ok("lobby", "[{0}] 0x14 + 0x1f + 0x1e (canais) + channel-lobby (Status=2)", Slot);
                    // POPULA o espelho do box (AccountInfo+0x78) JA no login — o painel do box no lobby (menu 0x14)
                    // pinta do espelho QUANDO E' CONSTRUIDO (igual o equip, carregado no 0x0C). O 0x13 da resposta
                    // ao 0x2d chega DEPOIS do painel construir (corrida) -> box vazio. Mandando o 0x13 aqui (antes
                    // de qualquer abertura do inventario), o espelho ja esta preenchido quando o painel monta.
                    // NADA alem de 14/1f/1e aqui: o original (mitm_full_113423) nao manda 0x13 nem
                    // pacotes de loja neste ponto. O box e' (re)pintado no open do inventario (0x2c/0x2d).
                    return true;
                case 0x36:
                    SendEncryptedFrame(_r36);
                    // 0x36b (arma a lista de games p/ o botao Create) SÓ na 1a vez. A captura do original
                    // mostra UM 0x36 por request; remandar o 0x36b a cada poll re-armava a game-list e
                    // mantinha o cliente em polling (telas sobrepostas), travando o Previous.
                    if (!_r36bSent) { _r36bSent = true; SendEncryptedFrame(_r36b); }
                    Log.Info("lobby", "[{0}] 0x36 ack{1}", Slot, _r36bSent ? " (+0x36b 1a vez)" : "");
                    return true;
                case 0x3b: // CRIAR SALA: room lobby = Status=2 (FieldLobby) + InField + FieldSecondary. (Era
                    // Status=3; 2 e' "sala montada antes do match" -> habilita shop 0x2d/2e/2f. O stage (0x4b)
                    // promove a Status=3 via CreateField.)
                    FieldSecondary = true; SecondActive = true; Status = 2; InField = true;
                    ParseRoomCreate(data); // guarda map/mode da sala -> decide stage (client-side) vs Battle (networked)
                    // nova sala = novo match: rearma o StartGameClock (a trava e' por ENTRADA NO STAGE,
                    // nao por sessao — sem isto a 2a sala da mesma sessao ficava com o field morto)
                    System.Threading.Interlocked.Exchange(ref _gameClockStarted, 0);
                    SendEncryptedFrame(_r3b);
                    Log.Ok("lobby", "[{0}] 0x3b sala criada -> room lobby (Status=2, FSec, map={1} mode={2})",
                        Slot, PendingRoomMap, PendingRoomMode);
                    return true;
                case 0x43: // engage/start do match: rearma o clock p/ REMATCH na mesma sala
                    System.Threading.Interlocked.Exchange(ref _gameClockStarted, 0);
                    SendEncryptedFrame(_r43);
                    Log.Info("lobby", "[{0}] 0x43 resp (clock rearmado)", Slot);
                    return true;
                case 0x48: SendEncryptedFrame(_r48); Log.Info("lobby", "[{0}] 0x48 resp", Slot); return true;
                case 0x4A: // 0x4A com data[0]=0x02 = StageClear. Combate usa outras 0x4A -> nao intercepta.
                    if (data.Length >= 1 && data[0] == 0x02)
                    {
                        // Manda SO a tela de Rank agora; o 0x44 (match-end) + refresh do lobby vem com um
                        // pequeno DELAY p/ o overlay de Rank ter tempo de aparecer. (Mandar tudo junto, ou
                        // disparar no 0x53, fazia o Rank sumir.) O 0x53 GameResultReport que o cliente manda
                        // logo apos e' CONSUMIDO (case 0x53) p/ nao cair no handler que desconecta em solo.
                        SendEncryptedFrame(_r4a);        // tela de RANK
                        Log.Ok("lobby", "[{0}] 0x4A StageClear -> Rank (0x44+refresh do lobby em ~5s)", Slot);
                        _ = ScheduleLobbyReturnAfterRankAsync();
                        return true;
                    }
                    return false;
                case 0x3A: // FieldLeaveGame: sair do game room -> volta a LISTA DE GAMES do canal. O handler
                    // real (Op_FieldLeaveGame) desconecta (DISC 0x50: guard InField&&FieldSecondary que o
                    // estado do solo nao satisfaz apos o stage). Tratamos aqui: reset + refresh da lista
                    // (1f/1e/36, os MESMOS frames capturados do original APOS o 0x44 = o "voltar pra lista").
                    InField = true; FieldSecondary = true; SecondActive = true; Status = 2; // volta ao channel lobby (shop segue ok)
                    SendEncryptedFrame(_r1f_clear);
                    SendEncryptedFrame(_r1e_clear);
                    SendEncryptedFrame(_r36_clear);
                    Log.Ok("lobby", "[{0}] 0x3A FieldLeaveGame -> lista de games (channel lobby Status=2)", Slot);
                    return true;
                // 0x2E (Shop Buy) NAO e' mais interceptado aqui -> cai no WorldHandlers.Dispatch (Op_RoomMemberQuery
                // = compra real). Removido o intercept de falha graciosa. (Ver default: 0x2e na lista de excecoes.)
                case 0x2C: // Inventory enter. O cliente manda no body o SEU [SlotActive:u32][user14a4:u32]
                    // (contexto/ponteiro da UI). O 0x12 (enter-ack, FUN_00420de0) deve ECOAR esses valores —
                    // com (1,1) o cliente nao reconhece e trata como "char created". Guardo o body p/ ecoar
                    // tambem no user14a4 do 0x13. (No MEU world a previa do char-select e' vazia => sem "varios armours".)
                    _invReqBody = data;
                    SendInventoryEnterAck(data);
                    // Pinta o box na abertura: no cliente no-GG o grid só pinta via 0x31 (FUN_0047d1d0),
                    // nunca via 0x13. Mandados UMA vez aqui (não a cada poll de 0x2d, que era o que prendia
                    // o cliente e quebrava o Previous) — a propria compra (0x2e) ja re-pinta com 0x31 sem
                    // quebrar o Previous, entao 0x31 na abertura tb e' seguro.
                    for (int i = 0; i < BoxItems.Count && i < 0x78; i++) SendBoxAdd(BoxItems[i], (byte)i, 1);
                    return true;
                case 0x2D: // FIEL à captura: o 0x2d responde SEMPRE o ack curto (handle), NUNCA 0x13.
                    // O original (test com box vazio) so' manda o ack; mandar 0x13 (lista) prendia o cliente
                    // re-pedindo 0x2d (telas sobrepostas) e o Previous nao voltava p/ a lista de salas.
                    SendInventoryAck(1);
                    return true;
                case 0x53: // GameResultReport pos-clear (SOLO PvE). O handler real (Op_0x53_Recon) exige o
                    // field-record do match-engine, que esta OFF no solo (combate client-side) e DESCONECTAVA.
                    // Consumimos aqui MAS creditamos o exp/gold reportados (FUN_00425010: [idx][cfgA][cfgB]
                    // [cfgB x u16][exp u32][gold u32]) — antes o stage clear nao dava progresso nenhum.
                    CreditSoloResult(data);
                    return true;
                case 0x4b: // SPAWN no stage (72B). Inicia o relogio da partida: um timer incrementa
                    // GameSeq e manda o tick 1583 (o cliente ecoa o seq; seq avancando = timer corre).
                    InField = true;
                    StartGameClock();
                    Log.Ok("lobby", "[{0}] 0x4b (spawn) -> STAGE; relogio de gameplay iniciado (udp={1})", Slot, UdpEndpoint?.ToString() ?? "-");
                    return true;
                case 0x0f: return true; // keepalive do cliente: sem resposta TCP
                // (0x4b acima inicia o relogio)
                default:
                    // Shop list/loadout (0x2d/0x2f) E buy (0x2e) DEVEM chegar aos handlers reais
                    // (Op_RoomRosterSync/Op_GroupMemberInfo/Op_RoomMemberQuery) -> NAO consumir aqui, deixa o
                    // Dispatch rodar (gate passa: channel/room lobby = InField+FSec+Status=2).
                    if (opcode == 0x2d || opcode == 0x2e || opcode == 0x2f || opcode == 0x33) return false;
                    // Salas BATTLE/PvP (Mode != 0): os opcodes de COMBATE vao aos handlers reais do
                    // motor de partida — 0x4d (par golem/facing: y==0 = golem inimigo destruido ->
                    // fim de round), 0x4f (morte), 0x46 (hit), 0x3d (troca de arma), 0x50 (reporte
                    // de exp/gold do fim de partida -> level-up). No SOLO (Mode 0) continuam
                    // engolidos: o combate e' client-side e o 0x4d (y==0) encerraria o stage.
                    if (opcode == 0x4d || opcode == 0x4f || opcode == 0x46 || opcode == 0x3d || opcode == 0x50)
                    {
                        var bf = _server.GetField(FieldId);
                        if (bf != null && bf.Mode != 0) return false; // dispatch -> Op_0xNN_Recon
                    }
                    // No lobby/stage o cliente manda varios opcodes que o world original so consome
                    // sem responder. Evita DISC enquanto a fase de gameplay nao esta toda portada.
                    if (InField) { Log.Debug("lobby", "[{0}] opcode {1:X2} em campo (sem resp)", Slot, opcode); return true; }
                    return false;
            }
        }

        /// <summary>
        /// Envio pelo canal "lobby" (FUN_004038e0 -> FUN_004048e0): frame [u16 size][payload],
        /// size = payload+2 (inclui-se). O payload comeca com [u16 subtype]. No original o
        /// payload e cifrado em AES quando a cripto esta ligada (this+0x208&amp;1); aqui vai em
        /// texto enquanto o key-setup AES nao e reconstruido (ver PROTOCOL.md / FUN_00401670).
        /// </summary>
        public void SendLobby(byte[] payload)
        {
            byte[] body = Crypto.Enabled ? Crypto.Encrypt(payload) : payload;
            int size = 2 + body.Length;
            byte[] frame = new byte[size];
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(0), (ushort)size);
            Array.Copy(body, 0, frame, 2, body.Length);
            SendRaw(frame);
        }

        // ---- envio ----------------------------------------------------------

        /// <summary>
        /// Envia uma mensagem world->client pelo canal FIELD: [u16 msgType][data], CIFRADO
        /// (AES 12->16, FUN_004038e0/FUN_00401040) e enquadrado com [u16 size].
        /// </summary>
        public void SendMessage(ushort msgType, byte[] data)
        {
            // WIRE REAL (mitm_full_113423, TODOS os frames W->C): [u16 msgType][data] — msgType
            // PRIMEIRO, igual ao canal lobby. (A 1a leitura da RE punha [serverSeq][msgType]; o
            // cliente lia o serverSeq como opcode — ex.: seq 0x000E virava "OnRecvSuccessUDP,
            // Unknown error" no fim de round. O serverSeq user+0x1488 e' contador interno.)
            byte[] content = new byte[2 + data.Length];
            BinaryPrimitives.WriteUInt16LittleEndian(content.AsSpan(0), msgType);
            Array.Copy(data, 0, content, 2, data.Length);
            Log.Debug("tx", "[{0}] FIELD msg=0x{1:X2} {2}B: {3}", Slot, msgType, content.Length, Convert.ToHexString(content));

            byte[] body = Crypto.Enabled ? Crypto.Encrypt(content) : content;
            int size = body.Length + 2;
            byte[] frame = new byte[size];
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(0), (ushort)size);
            Array.Copy(body, 0, frame, 2, body.Length);
            SendRaw(frame);
        }

        /// <summary>LoginComplete (msgType 2) — sucesso de FUN_0041f6c0.</summary>
        public void SendLoginComplete(string field2, string field3, ushort tail)
        {
            using var p = new PacketWriter();
            p.WriteCString(field2);
            p.WriteCString(field3);
            p.WriteWord(tail);
            p.WriteInt32(0);
            p.WriteByte(1);
            SendMessage(Protocol.SubType.LoginComplete, p.ToArray());
            Log.Ok("login", "[{0}] LoginComplete enviado (field2='{1}')", Slot, field2);
        }

        /// <summary>
        /// Envia um frame world->client com o PLAINTEXT ja pronto ([u16 opcode][u16 seq][data]):
        /// cifra (AES 12->16) e enquadra com [u16 size]. Usado pelo replay do oraculo.
        /// </summary>
        public void SendEncryptedFrame(byte[] plaintext)
        {
            Log.Debug("tx", "[{0}] LOBBY frame {1}B: {2}", Slot, plaintext.Length, Convert.ToHexString(plaintext));
            byte[] body = Crypto.Enabled ? Crypto.Encrypt(plaintext) : plaintext;
            int size = body.Length + 2;
            byte[] frame = new byte[size];
            BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(0), (ushort)size);
            Array.Copy(body, 0, frame, 2, body.Length);
            SendRaw(frame);
        }

        // ---- REPLAY do oraculo (RakionWorldServ.exe ORIGINAL, capturado via MITM) -------------
        // Resposta REAL de login = 3 frames: 0x0C (lista de chars), 0x0D (tabela zerada, 1332B),
        // 0x10 (GameGuard challenge, enviado SO depois do handshake UDP — ver NotifyUdpReady).
        // Usamos os BYTES EXATOS que o cliente real aceitou (e jogou): oracle_0c.bin (816B, 2
        // chars JP+Maguinhooo = estado real da conta 'test') + oracle_0d.bin (1332B). O 0x10
        // e' PULADO aqui (vai apos o ping UDP). Antes usavamos um 0x0C de 456B (1 char, captura
        // velha) que CRASHAVA o cliente real — por isso a troca p/ os 816B do MITM.
        private static byte[] ReadOracle(string name)
        {
            try
            {
                string p = System.IO.Path.Combine(AppContext.BaseDirectory, name);
                return System.IO.File.Exists(p) ? System.IO.File.ReadAllBytes(p) : Array.Empty<byte>();
            }
            catch { return Array.Empty<byte>(); }
        }

        public void SendLoginResponseReplay()
        {
            byte[] f0c = ReadOracle("oracle_0c.bin");
            byte[] f0d = ReadOracle("oracle_0d.bin");
            if (f0d.Length == 0) { f0d = new byte[1332]; f0d[0] = 0x0d; f0d[3] = 0x01; } // fallback gerado
            if (f0c.Length >= 105)
            {
                // OVERLAY DINAMICO do estado vivo (DB, carregado em LoadAndLogAsync ANTES deste envio).
                // Offsets da active record CRAVADOS via captura-diff do worldserv original (setei DB
                // levelpoint=99/win=88/lose=77/draw=66 e achei os offsets onde apareceram):
                BinaryPrimitives.WriteUInt32LittleEndian(f0c.AsSpan(56), Gold);        // gold@56
                BinaryPrimitives.WriteUInt32LittleEndian(f0c.AsSpan(60), Cash);        // cash@60
                BinaryPrimitives.WriteUInt32LittleEndian(f0c.AsSpan(73), CharWin);     // win@73
                BinaryPrimitives.WriteUInt32LittleEndian(f0c.AsSpan(77), CharLose);    // lose@77
                BinaryPrimitives.WriteUInt32LittleEndian(f0c.AsSpan(81), CharDraw);    // draw@81
                f0c[96] = CharLevel;                                                   // nivel@96 (u8)
                BinaryPrimitives.WriteUInt32LittleEndian(f0c.AsSpan(101), CharLevelPoint); // levelpoint@101
                // APARENCIA EQUIPADA: body@119 = 7 itens u16, body@157 = 7 bytes (slot/enhance). CAPTURADO do
                // servidor ORIGINAL p/ o JP (worldprobe headless vs 40708 + diff contra o oraculo). E' DAQUI que o
                // cliente renderiza o gear 3D + os ICONES do equip no inventario (NAO do 0x13, que cai num stub).
                // Antes zeravamos por crash de bone; com o cliente PATCHEADO (this+0x174=0) + itens nao-Helmet_D ok.
                // APARENCIA EQUIPADA (body@119 = itens u16, body@157 = enhance): montada do DB (session.Items).
                // E' DAQUI que o cliente renderiza o gear 3D + os ICONES do equip (NAO do 0x13, que cai num stub).
                ApplyEquipAppearance(f0c);
            }
            // handle de sessao p/ os acks 0x2c/0x2d (bytes 13..16 do 0x0C; ver _invHandle).
            if (f0c.Length >= 17) _invHandle = new[] { f0c[13], f0c[14], f0c[15], f0c[16] };
            if (f0c.Length > 0) SendEncryptedFrame(f0c);
            SendEncryptedFrame(f0d);
            Log.Ok("login", "[{0}] replay oraculo enviado (0x0C {1}B gold={3} cash={4} + 0x0D {2}B, 0x10 apos UDP)", Slot, f0c.Length, f0d.Length, Gold, Cash);
        }

        /// <summary>
        /// Monta a APARENCIA EQUIPADA no 0x0C a partir do DB (session.Items). O @119 e' POSICIONAL por TYPE:
        /// pos = type do item = itemId/100 - 10 (type0->pos0, type1->pos1 ... type5->pos5). CAPTURADO do servidor
        /// ORIGINAL com um set balanceado (1 item/type). body@157[pos] = nivel/enhance do item. 1 item por type (dedup).
        /// E' DAQUI que o cliente renderiza o gear 3D + os ICONES do equip (NAO do 0x13, stub no cliente GG-removido).
        /// Item no slot errado (type != pos) faz o cliente buscar o bone errado -> crash fatal (ex: 'Helmet_D').
        /// </summary>
        private void ApplyEquipAppearance(byte[] f0c)
        {
            if (f0c.Length < 168) return;  // precisa de body@157 + 7 slots (= file 161..167)
            var used = new System.Collections.Generic.HashSet<int>();
            foreach (var it in Items)
            {
                int pos = it.ItemId / 100 - 10;  // type -> posicao do slot
                if (pos < 0 || pos > 6 || !used.Add(pos)) continue;  // fora do equip (transforms 8xxx) ou type ja preenchido
                BinaryPrimitives.WriteUInt16LittleEndian(f0c.AsSpan(123 + pos * 2), (ushort)it.ItemId); // body@119
                f0c[161 + pos] = it.Level;                                                               // body@157 = enhance
            }
        }

        /// <summary>
        /// 0x12 InventoryEnter ack (FUN_00420de0). Apos o cabecalho [u16 seq][u16 0x12] (SendMessage):
        /// [u32 user+0x1460][u32 user+0x14a4]. E' o que faz o cliente TRANSICIONAR p/ a tela de inventario
        /// (menu state 0x19/0x1a/0x1b); sem ele o cliente volta pro char-select e fecha.
        /// </summary>
        public void SendInventoryEnterAck(byte[] reqBody)
        {
            // FORMATO REAL (captura do worldserv ORIGINAL, mitm inventario→Previous 2026-06-11):
            //   W->C 0x2c = [2c 00][00][handle:4][00 01][00 12][00]  (12B)
            // O original NAO ecoa o body do cliente — reflete o HANDLE de sessao (bytes 13..16 do 0x0C).
            // Ecoar o body (FFFFFFFF8F21347C) deixava o cliente sem reconhecer o estado do inventario ->
            // ficava em polling de 0x2d/0x36 (telas sobrepostas) e o Previous caia no char-select.
            using var w = new PacketWriter();
            w.WriteWord(0x2c);                 // 2c 00
            w.WriteByte(0);                    // 00
            w.WriteBytes(_invHandle);          // handle de sessao (4B)
            w.WriteByte(0); w.WriteByte(1);    // 00 01
            w.WriteByte(0); w.WriteByte(0x12); // 00 12
            w.WriteByte(0);                    // 00
            SendEncryptedFrame(w.ToArray());
            Log.Ok("shop", "[{0}] 0x2c enter-ack (handle {1})", Slot, System.Convert.ToHexString(_invHandle));
        }

        /// <summary>
        /// 0x13 lista de inventario (FUN_00420f10). Apos [u16 seq][u16 0x13] (SendMessage):
        /// [u32 user+0x14a4][u8 count1][count1*u32 itemId][count1*u8 slot][u8 count2=0][u8 flag=0].
        /// Popula o Box (FUN_004774e0). Framing REAL via SendMessage ([seq][msgType]); antes ia por
        /// SendLobby (msgType no offset 0) -> o cliente parseava errado e nao populava.
        /// </summary>
        public void SendInventoryList()
        {
            // Formato REAL (RE de FUN_00420f10 + FUN_0040bcb0, 2026-06-08): apos [u16 0x13][u16 seq] ->
            // [u32 user+0x14a4][u8 count1][count1*u32 itemId][count1*u8 slot][u8 count2][u8 count3flag].
            // Framing [opcode@0][seq@2] CONFIRMADO pela captura do servidor ORIGINAL (op=0x0c/0x0d/0x10 @off0).
            // Inventario = protocolo de DIFF; 1a vez = lista cheia. itemValue = itemId. TESTE de formato com
            // armas (1001/1002/1008 Swordman) — sem capacete, p/ evitar o bone 'Helmet_D' se o grid renderizar 3D.
            var inv = Items ?? new System.Collections.Generic.List<RakionServer.World.Database.UserItem>();
            // BAG/box (0x13) = SO itens NAO-equipados (slot >= 7). Os equipados (slots 0-6) vao na aparencia
            // 0x0C @119 e NAO podem entrar aqui: o handler de COMPRA do cliente le este bag e crasha ao clicar
            // Buy se ele contiver os equipados (bag malformado). Char todo-equipado -> bag vazio (count1=0).
            var bag = new System.Collections.Generic.List<RakionServer.World.Database.UserItem>();
            foreach (var it in inv) if (it.Slot >= 7) bag.Add(it);
            byte count1 = (byte)System.Math.Min(bag.Count, 19);       // bag = 19 slots (0x13)
            // user+0x14a4 = o mesmo valor que o cliente mandou no 0x2c (2o u32 do body) — ecoado tb aqui.
            uint secondActive = (_invReqBody != null && _invReqBody.Length >= 8)
                ? BinaryPrimitives.ReadUInt32LittleEndian(_invReqBody.AsSpan(4))
                : (FieldSecondaryRaw != 0 ? (uint)FieldSecondaryRaw : 1u);
            // count2 = ARMAZEM (itembox). FUN_0047e6f0 (handler 0x13) copia count2 itens p/ this+0x21 (a DATA do
            // box). POREM isso NAO desenha o grid VISUAL — o grid e' o FUN_0044deb0, chamado pelo 0x31 (ao vivo)
            // e pelo 0x2e (FUN_004774e0). Por isso o item comprado AO VIVO (0x31) aparece, mas os persistidos do
            // itembox (so' no 0x13) NAO apareciam no open. Mando o count2 (data) + um 0x2e count=N (visual) abaixo.
            byte count2 = (byte)System.Math.Min(BoxItems.Count, 0x78);          // box = ate 120 celulas
            // ===== DIAGNOSTICO (temporario): inventario VAZIO p/ isolar se a corrupcao da lista de
            // widgets (crash csComponent::PrevChild no Previous) vem dos DADOS de item que mandamos.
            // Se com count1=count2=0 o Previous AINDA crashar -> bug 100% client-side. Reverter depois. =====
            if (DiagEmptyInventory) { count1 = 0; count2 = 0; }
            using var w = new PacketWriter();
            w.WriteWord(0x13);                                                   // opcode @0
            w.WriteWord(0);                                                      // seq @2
            w.WriteUInt32(secondActive);                                        // user+0x14a4
            w.WriteByte(count1);                                                 // count1 = bag (nao-equipados)
            for (int i = 0; i < count1; i++) w.WriteUInt32((uint)bag[i].ItemId); // itemValue u32 = itemId real
            for (int i = 0; i < count1; i++) w.WriteByte((byte)(bag[i].Slot - 7)); // posicao no bag grid (slot-7)
            w.WriteByte(count2);                                                 // count2 = box (itembox) -> DATA this+0x21
            for (int i = 0; i < count2; i++) w.WriteUInt32((uint)BoxItems[i]);   // itemId u32 por celula
            for (int i = 0; i < count2; i++) w.WriteByte((byte)i);               // slot = indice da celula (0..)
            w.WriteByte(0);                                                      // count3 flag = 0 (sem bloco appearance)
            SendEncryptedFrame(w.ToArray());
            Log.Ok("shop", "[{0}] 0x13 inventario enviado: bag={1}, box(count2)={2}", Slot, count1, count2);
            // FIEL ao original (FUN_00420f10 + FUN_0040bcb0): o 0x2d responde SÓ este 0x13 — o count2
            // (itens do box) é o que pinta o grid. SEM 0x2e visual nem 0x31 (eram band-aids de quando o
            // 0x13 estava malformado; agora corrompiam a UI nas transições de tela).
        }

        /// <summary>
        /// 0x2d ACK curto (FUN_00420f10, path "else": FUN_004038e0 subtype 3 = [0x2d][status]). O
        /// worldserv original responde ISTO em todo 0x2d que NÃO seja a 1a list (FUN_0040c960 devolve
        /// 1 quando user+0x144c==0, ou 2 quando ==loja — sem remontar a lista). É o "nada mudou" que o
        /// cliente espera p/ concluir a saída do inventário e VOLTAR AO LOBBY. Remandar a lista 0x13 aqui
        /// fazia o cliente reprocessar o grid de widgets e cair no char-select no Previous.
        /// </summary>
        public void SendInventoryAck(byte status)
        {
            // FORMATO REAL (captura do original): W->C 0x2d = [2d 00][00 00][2c 00 00][handle:4][00] (12B).
            // Ecoa o handle de sessao (igual ao 0x2c). Antes mandavamos [2d 00][00 00][status] (5B) — o
            // cliente nao reconhecia, ficava em polling e o Previous nao concluia a saida p/ a lista de salas.
            using var w = new PacketWriter();
            w.WriteWord(0x2d);            // 2d 00
            w.WriteWord(0);              // 00 00
            w.WriteWord(0x2c);           // 2c 00
            w.WriteByte(0);              // 00
            w.WriteBytes(_invHandle);    // handle de sessao (4B)
            w.WriteByte(0);              // 00
            SendEncryptedFrame(w.ToArray());
            Log.Ok("shop", "[{0}] 0x2d ack (handle {1}) — fiel ao original", Slot, System.Convert.ToHexString(_invHandle));
        }

        /// <summary>
        /// 0x31 box-add: exibe um item no grid do BOX. O handler do cliente (FUN_0047d1d0) e' um MOVE com
        /// descritor de ORIGEM e de DESTINO, cada um escrevendo na celula = slot do descritor:
        ///   origem  (srcType==0 box): grava srcItem  na celula srcSlot   (call 0x47d3c9)
        ///   destino (destType==0 box): grava destItem na celula destSlot (call 0x47d740)
        /// Layout apos [u16 0x31][u16 seq]: [u32 srcDesc][u32 destDesc][u16 srcItem][u16 destItem][u32 lvl][u32 val].
        /// Descritor = slot no byte baixo, type no byte 1 (0 = box; slot &lt; 256 mantem type=0). Confirmado via frida.
        /// FIX overwrite: destDesc deve ser boxSlot (estava 0 -> jogava todo item na celula 0). srcItem=0 limpa a
        /// celula srcSlot=boxSlot, e logo em seguida o destino grava destItem=itemId na MESMA celula boxSlot.
        /// </summary>
        public void SendBoxAdd(int itemId, byte boxSlot, byte level)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x31);             // opcode @0
            w.WriteWord(0);                // seq @2
            w.WriteUInt32(boxSlot);        // F1 (confirmado -> srcSlot)
            w.WriteUInt32(0);              // F2 (confirmado -> param_7; revertido p/ 0)
            w.WriteWord((ushort)(boxSlot << 8)); // F3 = [destType:lo=0 (box)][destSlot:hi=boxSlot] -- byte baixo era destType
            w.WriteWord((ushort)itemId);   // F4 (confirmado -> destItem)
            w.WriteUInt32(level == 0 ? 1u : level); // level
            w.WriteUInt32(0x00403900);     // val (copiado da captura do 0x31 box-render do original)
            SendEncryptedFrame(w.ToArray());
            Log.Ok("shop", "[{0}] 0x31 box-add: item {1} -> box slot {2}", Slot, itemId, boxSlot);
        }

        /// <summary>
        /// Spawn no stage (0x4b da cadeia de entrada): garante que a sessao tem um Field real com
        /// seat alocado, marca o player como ready e delega ao MOTOR DA PARTIDA por-field
        /// (WorldServer.FieldEngine / FUN_00409940). O 1583 idle e o broadcast 0x48 sao do motor
        /// global, NAO mais por-sessao (substitui o StartGameClock parcial).
        /// </summary>
        private void StartGameClock()
        {
            if (System.Threading.Interlocked.Exchange(ref _gameClockStarted, 1) != 0) return;
            GameSeq = 5;
            var f = _server.EnsureFieldForSession(this);
            if (f.Settled) f.ResetMatch(); // field reaproveitado de um match concluido: zera Round/Wins/golens
            if (PendingRoomMode != 0) { f.Mode = PendingRoomMode; f.MapId = PendingRoomMap; }
            if (PendingRoomDurationSec != 0) f.RoundDurationSec = PendingRoomDurationSec; // tempo configurado na sala
            if (PendingRoomRounds != 0) f.MaxRounds = PendingRoomRounds;                  // rounds configurados na sala
            _server.NotifyPlayerReady(f, this);
            Log.Ok("field", "[{0}] sala aplicada ao field {1}: mode={2} map={3} dur={4}s rounds={5}",
                Slot, f.Id, f.Mode, f.MapId, f.RoundDurationSec, f.MaxRounds);
            // Sala Battle/PvP (mode != 0) = fluxo NETWORKED: o SERVER inicia o loop UDP com o 1o
            // tick 1583 (mitm_full_113423: o original manda 1583 ANTES do 1o 0040 do cliente; sem
            // ele o input fica congelado). Stage solo (mode 0) fica client-side: sem tick.
            if (f.Mode != 0 && UdpEndpoint != null) _server.SendGameplayTick(UdpEndpoint, LastInput);
            Log.Ok("field", "[{0}] spawn -> motor da partida (field {1}, seat {2}, mode {3})", Slot, f.Id, FieldSeat, f.Mode);
        }

        /// <summary>
        /// Credita o resultado do STAGE SOLO (0x53, FUN_00425010): parse [idx u8][cfgA u8][cfgB u8]
        /// [cfgB x u16 mapSlots][exp u32][gold u32]. Mesmo teto anti-cheat do caminho PvP (0x50).
        /// O level-up/persistencia ficam no WorldServer.GrantExp (curva classlevelinfo).
        /// </summary>
        private void CreditSoloResult(byte[] data)
        {
            try
            {
                var p = new PacketReader(data);
                p.Byte();                      // idx
                p.Byte();                      // cfgA
                byte cfgB = p.Byte();          // cfgB = qtde de u16 a pular
                for (int i = 0; i < cfgB && p.Remaining >= 2; i++) p.UInt16();
                uint exp = p.CanRead(4) ? p.UInt32() : 0;
                uint gold = p.CanRead(4) ? p.UInt32() : 0;
                const uint Max = 1_000_000;    // teto de sanidade (= ValidateGamePoints do 0x50)
                if (exp > Max || gold > Max)
                {
                    Log.Warn("field", "[{0}] 0x53 solo: Wrong Game Point! Exp:{1} Gold:{2} — ignorado", Slot, exp, gold);
                    return;
                }
                Gold += gold;
                if (gold > 0 && GameInfoId > 0) _ = _server.Db.AddGoldAsync(GameInfoId, (int)gold);
                _server.GrantExp(this, exp);
                Log.Ok("field", "[{0}] 0x53 stage clear solo — exp={1} gold={2} creditados", Slot, exp, gold);
            }
            catch (Exception ex) { Log.Warn("field", "[{0}] 0x53 solo parse: {1}", Slot, ex.Message); }
        }

        /// <summary>
        /// Parse do 0x3b (FUN_00423580: name\0 senha\0 desc\0 [map][mode][rounds][u16 durSec]...):
        /// guarda map/mode/rounds/duracao da sala p/ aplicar ao Field no spawn (0x4b). Mapas Battle
        /// (200-213) vem com mode 1-4; rounds validado &lt; 0x16 no original (sala de stage = 1);
        /// durSec validado em 0x122..0x4ba (290..1210s).
        /// </summary>
        private void ParseRoomCreate(byte[] data)
        {
            try
            {
                var p = new PacketReader(data);
                p.CString(0x29); p.CString(9); p.CString(0xc9);
                if (p.Remaining >= 2) { PendingRoomMap = p.Byte(); PendingRoomMode = p.Byte(); }
                if (p.Remaining >= 3)
                {
                    byte rounds = p.Byte(); // param_3[+2] (<0x16)
                    if (rounds >= 1 && rounds < 0x16) PendingRoomRounds = rounds;
                    ushort dur = p.UInt16();
                    if (dur >= 0x122 && dur <= 0x4ba) PendingRoomDurationSec = dur;
                }
            }
            catch { PendingRoomMap = 0; PendingRoomMode = 0; PendingRoomDurationSec = 0; PendingRoomRounds = 0; }
        }

        /// <summary>
        /// Pos-StageClear: depois de mostrar a tela de Rank (0x4A), espera um pouco p/ o overlay aparecer
        /// e entao manda 0x44 (match-end) + refresh do lobby POS-CLEAR (0x1f/0x1e/0x36) -> volta a selecao.
        /// O delay e' o que faz o Rank ser VISIVEL (mandar tudo junto fazia o cliente pular direto pro lobby).
        /// </summary>
        private async Task ScheduleLobbyReturnAfterRankAsync()
        {
            try { await Task.Delay(12000, _cts.Token); }
            catch (OperationCanceledException) { return; }
            catch { }
            if (!Connected) return;
            // SO o 0x44 (match-end): devolve o cliente ao GAME ROOM onde ele estava (p/ rejogar).
            // NAO mandar 0x1f/0x1e/0x36 aqui: o 0x1e e' a LISTA DE CANAIS ("dchannel01") e levava o
            // cliente pra LISTA DE GAMES em vez do room (o original volta pro room apos o stage).
            // Volta ao room lobby (Status=2, InField/FSec) -> shop continua disponivel no room pos-stage.
            InField = true; FieldSecondary = true; SecondActive = true; Status = 2;
            SendEncryptedFrame(_r44);
            Log.Ok("lobby", "[{0}] pos-Rank (delay) -> 0x44 match-end (volta ao game room)", Slot);
        }

        // ---- handshake UDP -------------------------------------------------------------
        // Fluxo REAL (capturado): o cliente faz ping UDP -> world ecoa 0x0201 (UdpGameplay/BrokerLink)
        // -> cliente manda TCP 0x0e -> TryHandleLobbyEntry responde 0x0e(endpoints)+0x10+... Aqui so
        // registramos o endpoint UDP do jogador (o 0x10 NAO sai aqui — vai apos o 0x0e do cliente).
        public void NotifyUdpReady(IPEndPoint udpEndpoint)
        {
            UdpEndpoint = udpEndpoint;
        }

        /// <summary>Erro de login (FUN_004038e0, cat 3, {0x0C, sub}).</summary>
        public void SendLoginError(byte sub)
        {
            using var p = new PacketWriter();
            p.WriteByte(Protocol.LoginError.Main); // 0x0C
            p.WriteByte(sub);
            SendMessage(Protocol.LoginError.Category, p.ToArray());
        }

        /// <summary>Desconecta com razao (FUN_0041eb20): loga "[NNNN] DISC NNN" e envia notify.</summary>
        public void Disconnect(ushort reason)
        {
            Log.Warn("client", "[{0:0000}] DISC {1:000}", Slot, reason);
            if (Connected && SlotActive)
            {
                using var p = new PacketWriter();
                p.WriteInt32(0);            // user+0x1468 (reservado)
                p.WriteWord(reason);
                p.WriteInt32(0);
                try { SendMessage(Protocol.SubType.Disconnect, p.ToArray()); } catch { }
            }
            _ = CloseAsync();
        }

        private readonly object _sendLock = new();
        private void SendRaw(byte[] frame)
        {
            // lock: o box-add atrasado (Task pos-0x13) envia em paralelo com o loop principal; Socket.Send
            // concorrente entrelaca os bytes e corrompe o framing. Serializa os envios.
            try { lock (_sendLock) { _sock.Send(frame); } }
            catch (Exception ex) { Log.Error("client", "[{0}] send: {1}", Slot, ex.Message); }
        }

        public async Task CloseAsync()
        {
            if (!Connected && _cts.IsCancellationRequested) return;
            Connected = false;
            try { _cts.Cancel(); } catch { }
            try { _sock.Shutdown(SocketShutdown.Both); } catch { }
            try { _sock.Close(); } catch { }
            await _server.RemoveSessionAsync(this);
        }
    }

    /// <summary>Snapshot dos dados de jogo carregados do DB para a sessao.</summary>
    public sealed class WorldDatabaseInfo
    {
        public int UserId;
        public string Name = "";
        public string CharName = "";
        public int Gold;
    }
}
