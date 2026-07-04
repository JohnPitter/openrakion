using System;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.Handlers;
using RakionServer.World.CharSelect;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Sessao de um cliente TCP. Replica o frame/seq do worldserv.exe:
    ///   frame = [u16 size][u16 A][u16 B][data]  (size inclui o proprio campo).
    ///   cliente->servidor: A=opcode, B=seq (validado == ultimoSeq+1, wrap 65000;
    ///   0x0C/0x0F isentos, 0x0C reseta). servidor->cliente: A=serverSeq++, B=msgType.
    /// Ver PROTOCOL.md (FUN_0042bd70 / FUN_0042ab40 / FUN_0041b940 / FUN_004048e0).
    /// </summary>
    public sealed partial class ClientSession
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
        public System.Collections.Generic.List<int> BoxItems { get; set; } = new(new int[0x78]); // grade do box: 120 celulas FIXAS (0=vazia). Esparsa p/ casar com a grade do cliente — mover item p/ celula vazia nao pode "sumir"
        public System.Collections.Generic.List<int> BoxCount { get; set; } = new(new int[0x78]); // contador por celula (pocao empilha; gear=1)
        public System.Collections.Generic.List<int> BoxLevel { get; set; } = new(new int[0x78]); // nivel de refino (+N) por celula (itembox.level)
        public System.Collections.Generic.List<int> BoxRowId { get; set; } = new(new int[0x78]); // id da linha itembox por celula (update/delete preciso do refino)
        public System.Collections.Generic.List<RakionServer.World.Database.UserItem> Items { get; set; } = new(); // inventario (useriteminfo) p/ o Box (0x2f)
        public byte CharLevel { get; set; } = 1;             // nivel do char ativo -> overlay 0x0C @96 (offset cravado no diff golden)
        public byte CharClass { get; set; }                  // classe do char ativo -> curva de level (classlevelinfo)
        public long CharExp { get; set; }                    // exp TOTAL acumulado do char ativo (level-up 0x50)
        public byte[]? StageRanks { get; set; }              // ranks de stage (userstageinfo) -> overlay 0x0C @333 (RANK X CLEAR na seleção)
        public uint CharWin { get; set; }                    // -> overlay 0x0C @73 (captura-diff)
        public uint CharLose { get; set; }                   // -> overlay 0x0C @77
        public uint CharDraw { get; set; }                   // -> overlay 0x0C @81
        public uint CharLevelPoint { get; set; }             // pontos de level p/ distribuir -> overlay 0x0C @101
        public uint PowerLevelPoint { get; set; }            // usergameinfo.powerlevelpoint = PU Bonus Points -> overlay 0x0C @48
        public ushort[] Stats { get; } = new ushort[10];     // stats alocados (this+0x1568+idx*2) p/ a alocacao 0x33
        public CharList? LoginCharList { get; set; }         // lista de chars (char-select) sintetizada p/ o 0x0C
        public string Md5Hash1 { get; set; } = "";           // MD5 do client (modo 0)
        public string Md5Hash2 { get; set; } = "";           // MD5 do client (modo 1)
        public byte PendingRoomMap;                          // map do 0x3b (sala criada) -> aplicado ao Field no 0x4b
        public byte PendingRoomMode;                         // mode do 0x3b: 0=stage (client-side), !=0=Battle/PvP (networked)
        public ushort PendingRoomDurationSec;                // duracao do round em SEGUNDOS (u16 do 0x3b, 290..1210)
        public byte PendingRoomRounds;                       // rounds configurados na sala (byte do 0x3b, <0x16; stage=1)
        public string PendingRoomName = "";                  // nome da sala (0x3b) -> match-end 0x44 (era "asdd" hardcoded)
        public ushort PendingRoomSlot;                       // mapSlot (u16 0x122..0x4ba) do 0x3b -> Field.MapSlot (entry 0x36)
        public string PendingRoomPass = "";                  // senha da sala (0x3b, <9) -> validada no join 0x38
        public byte PendingRoomFrag;                         // b3 do 0x3b (frag/points limit) -> Field.FragLimit (0x37 +f)
        public byte PendingRoomMinLevel;                     // b4 do 0x3b -> Field.MinLevel (0x37 +8)
        public byte PendingRoomMaxLevel;                     // b5 do 0x3b -> Field.MaxLevel (0x37 +9)

        // estado de combate/field (campos do user[slot] resolvidos por FUN_0040b7d0 e helpers de field)
        public ushort FieldTargetIndex; // user+0x14a0 (indice do field-objeto alvo resolvido por FUN_0040b7d0)
        public byte FieldTargetOwner;  // user+0x14a2 (byte de owner/slot do alvo resolvido por FUN_0040b7d0)
        public uint FieldCash;         // user+0x1534 (saldo de cash/pontos em campo; debitado por FUN_0040b900)
        public byte FieldCashCost;     // user+0x1531 (custo base usado por FUN_0040b900: cost = (this+0x1531>>1) + slot*5)
        public short FieldPairA;          // playerRecord (field+0xe4 + slot*0x3c0) +0x2c4
        public short FieldPairB;          // playerRecord +0x2c6
        public byte FieldRecordState;     // playerRecord +0x8 (estado do registro; 2 = inativo p/ acao)
        public ushort FieldTargetA;       // playerRecord +0x2c8 (alvo/objetivo, arg0<10)
        public ushort FieldTargetB;       // playerRecord +0x2ca (alvo/objetivo, arg0>=10)
        public ushort FieldObjectIndex; // user+0x14a0 (indice deste user no array de field-objects, lido por FUN_0040b7d0)
        public byte FieldSeat;          // user+0x14a2 (byte de seat/owner do user no field, lido por FUN_0040b7d0)
        public byte[]? StageSpawnUpload;  // os bytes do 0x4b que ESTE humano subiu (posição/stats reais) — relayados ao peer p/ o avatar aparecer no lugar certo
        public bool ExpBonusActive;     // user+0x236c != 0 (bônus de exp ativo; no nosso server = PU ativo)
        public bool PuActive;           // PU vigente (usergameinfo.powertimedate futuro)

        /// <summary>Aplica o multiplicador de XP do PU (pu_config, ciente de promoção) quando o bônus está
        /// ativo. Substitui o ×3/2 fixo do original por um fator configurável.</summary>
        public uint BonusExp(uint exp) =>
            ExpBonusActive ? (uint)(exp * _server.PuConfig.EffectiveExpMult(System.DateTime.Now)) : exp;

        /// <summary>Aplica o multiplicador de gold do PU (pu_config, ciente de promoção) quando o PU está ativo.</summary>
        public uint BonusGold(uint gold) =>
            PuActive ? (uint)(gold * _server.PuConfig.EffectiveGoldMult(System.DateTime.Now)) : gold;

        private ushort _clientSeq;   // user+0x146e (ultimo seq recebido)
        private byte[] _invReqBody = System.Array.Empty<byte>();  // body do 0x2c (SlotActive/user14a4 do cliente) p/ ecoar no 0x12/0x13
        // HANDLE de sessao (0x0C@13, ecoado nos acks de inventário 0x2c/0x2d/0x34). É um PONTEIRO autoral do
        // servidor: no worldserv original variava por conexão (0x0C@13 = 8deb863f/b3c3863f/700e873f ~ 0x3f86xxxx)
        // e o cliente apenas o ECOA, nunca dereferencia (é memória de outro processo). Por isso GERADO por sessão
        // — NÃO copiado de captura: o cliente aceita qualquer valor estável (provado pelo diff de 3 sessões reais).
        // (A cadeia de LOBBY já NÃO usa handle: todo 0x14/0x1e/0x1f/0x36/0x43 é LEN-real + zero-pad — ver LobbyFrames.)
        private readonly byte[] _invHandle = NewHandle();

        /// <summary>Gera um handle autoral do servidor (4B não-zero), único por sessão. O cliente só o ecoa,
        /// então o valor é arbitrário — só precisa ser estável dentro da sessão e diferente de zero.</summary>
        private static byte[] NewHandle()
        {
            byte[] h = new byte[4];
            System.Random.Shared.NextBytes(h);
            h[0] |= 0x01;   // garante != 0
            return h;
        }
        private bool _r36bSent;   // 0x36b (arma a lista de games) so' 1x; remandar a cada poll travava o cliente
        private readonly int[] _potionSlot = new int[0x13];   // quickslot/equip = user+0x1da4 (19 celulas; type1 do move 0x31)
        private readonly int[] _potionCount = new int[0x13];  // quantidade empilhada por celula (contador 'v' do 0x31)
        private readonly int[] _potionLevel = new int[0x13];  // nivel de refino (+N) das celulas type1 — segue o item no move
        private readonly int[] _potionRowId = new int[0x13];  // id da linha itembox das celulas type1 — segue o item no move
        private bool _potionPainted;          // quickslot pintado no 1o open (0x2c) da sessao — fallback do auto-render
        private bool _potionLoginPainted;     // quickslot pintado na entrada do lobby (0x14) — auto-render no relog (CONFIRMADO)
        private bool _chanJoinAnnounced;      // 0x1e-append do novato já broadcastado (1x por sessão; o widget acumula)

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

        /// <summary>
        /// Envio pelo canal "lobby" (FUN_004038e0 -> FUN_004048e0): frame [u16 size][payload],
        /// size = payload+2 (inclui-se). O payload comeca com [u16 subtype]. No original o
        /// payload e cifrado em AES quando a cripto esta ligada (this+0x208&amp;1); aqui vai em
        /// texto enquanto o key-setup AES nao e reconstruido (ver PROTOCOL.md / FUN_00401670).
        /// </summary>
        public void SendLobby(byte[] payload)
        {
            Log.Debug("tx", "[{0}] LOBBY(SendLobby) {1}B: {2}", Slot, payload.Length, Convert.ToHexString(payload));
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

        /// <summary>
        /// Envia um frame world->client com o PLAINTEXT ja pronto ([u16 opcode][u16 seq][data]):
        /// cifra (AES 12->16) e enquadra com [u16 size]. Usado pela síntese dos frames de
        /// lobby/canal/sala/stage (LobbyFrames) e dos acks de inventário/loja.
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
            // Socket disposto/fechado = a sessao ja desconectou (ex.: bot AI/relay enviando logo apos o peer sair).
            // Esperado na race de desconexao — Debug, nao Error (poluia o log e mascarava falhas reais).
            catch (ObjectDisposedException) { Log.Debug("client", "[{0}] send em socket disposto (desconectou)", Slot); }
            catch (System.Net.Sockets.SocketException ex) { Log.Debug("client", "[{0}] send socket: {1}", Slot, ex.Message); }
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
