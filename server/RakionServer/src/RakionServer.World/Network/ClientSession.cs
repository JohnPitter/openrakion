using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.Handlers;
using RakionServer.World.CharSelect;
using RakionServer.World.Domain;

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
        private readonly SemaphoreSlim _storageMutationLock = new(1, 1);
        private int _inventoryMutationInProgress;
        private readonly SemaphoreSlim _lotteryPurchaseLock = new(1, 1);

        // estado de usuario (espelha campos do objeto user[slot])
        public ushort Slot { get; }
        public string RemoteIp { get; }
        public bool Connected { get; private set; }          // user+0x1440
        public bool Authenticated { get; set; }              // this+0x5b18 == 0 ? login : in-game
        public bool SlotActive { get; set; }                 // sessão autenticada no servidor atual
        public bool SecondActive { get; set; }               // personagem selecionado no servidor atual
        public string ExpectedSessionName { get; set; } = "";
        public string UserId { get; set; } = "";
        public int Authority { get; set; }
        public int Country { get; set; }
        public string BuddyName { get; set; } = "";
        public bool TutorialClear { get; set; }
        public WorldDatabaseInfo? Game { get; set; }
        public ChatSessionState ChatState { get; } = new();

        public bool IsGm => Authority > 0;

        public bool CanExecuteGm(Domain.GmPermission permission) =>
            Domain.GmAuthorization.IsAllowed(Authority, _server.Config.GmEnabled, permission);

        // estado de lobby/jogo (campos nomeados do user[slot])
        public byte Status { get; set; }                     // user+0x1440 (ver Domain.UserStatus)
        public byte SubStatus { get; set; }                  // user+0x146c (categoria normal/especial/GM)
        public int GroupId { get; set; }                     // canal/IDC local selecionado
        public int ChannelId { get; set; } = -1;             // user+0x148c (índice do canal social)
        public byte ChannelSlot { get; set; } = byte.MaxValue; // user+0x148d (slot local 0..99 no canal)
        public string CharName { get; set; } = "";           // nome do personagem
        public int ClanId { get; set; }                      // user+0x14d0 (ID de clã no registro de presença)
        public bool InField { get; set; }                    // associação local a sala/field
        public bool FieldSecondary { get; set; }             // contexto local de gameplay disponível
        public int FieldId { get; set; } = -1;               // field atual (indice em World.Fields)
        public int RoomId { get; set; } = -1;                // room/chat atual (indice em World.Rooms)
        public IPEndPoint? UdpEndpoint1 { get; private set; }  // endpoint observado na porta UDP 1
        public IPEndPoint? UdpEndpoint2 { get; private set; }  // endpoint observado na porta UDP 2
        public IPEndPoint? UdpAdvertisedEndpoint { get; private set; } // endpoint direto anunciado no handshake
        public IPEndPoint? UdpObservedEndpoint => UdpEndpoint1 ?? UdpEndpoint2;
        public IPEndPoint? UdpEndpoint => UdpEndpoint2 ?? UdpEndpoint1; // rota preferida de gameplay
        public byte GameSeq;                                    // relogio/frame da partida (tick 1583); avanca = timer corre
        public byte LastGameplayFeedbackSeq;                    // ultimo seq ecoado pelo cliente em 1583 client->world
        public byte LastGameplayFeedbackState;                  // ultimo estado ecoado pelo cliente em 1583 client->world
        private int _gameClockStarted;
        public uint UdpKey { get; set; }                     // user+0x1464 (chave de sessao UDP, validada nos pacotes)

        public int ConnectionLogId { get; set; }             // user+0x1468: LogUserConnect.id da sessão
        public ushort DisconnectReason { get; private set; }
        public byte VerifyMode { get; set; }                 // user+0x237c (tipo de conexao p/ MD5)
        public int WorldEchoValue { get; set; }              // ultimo token ecoado em 0x61
        public uint Gold { get; set; }                       // user+0x1538 (em jogo)
        public uint Cash { get; set; }                       // user+0x153c (em jogo)
        public byte BagCount { get; set; } = 1;              // user+0x1540 (expansoes do armazem)
        public byte CharacterSlotCount { get; set; } = 4;    // user+0x1541 (slots de personagem)
        public byte PotionSlotCount { get; set; } = 3;       // characterinfo.potionslot
        public uint StageLevelFreeMarker { get; set; }       // usergameinfo.stagelevelfree (minutos desde TO_DAYS)
        public uint ServerTimeMarker { get; set; }
        public uint PowerTimeMarker { get; set; }
        // --- estado de COMPRA / char ativo (shop 0x2e) ---
        public int PreviewCharId { get; set; } = -1;         // characterinfo.used carregado para montar o 0x0C
        public int ActiveCharId { get; set; } = 0;           // user+0x14a4: zero até o 0x14 selecionar personagem
        public int GameInfoId { get; set; } = -1;            // user+0x1460: usergameinfo.id autenticado
        public volatile bool ShopBuyInProgress;              // espelha user+0x144c==2 (anti-duplo-clique)
        public Guid BotInitialStateMatchId;
        public Guid PlayerSpawnMatchId;
        private bool InventoryMutationInProgress =>
            ShopBuyInProgress || System.Threading.Volatile.Read(ref _inventoryMutationInProgress) != 0;
        private bool TryStartInventoryMutation() =>
            System.Threading.Interlocked.CompareExchange(ref _inventoryMutationInProgress, 1, 0) == 0;
        private void FinishInventoryMutation() =>
            System.Threading.Volatile.Write(ref _inventoryMutationInProgress, 0);
        private readonly InventoryUiState _inventoryUiState = new();
        public System.Collections.Generic.List<int> BoxItems { get; set; } = new(new int[0x78]); // grade do box: 120 celulas FIXAS (0=vazia). Esparsa p/ casar com a grade do cliente — mover item p/ celula vazia nao pode "sumir"
        public System.Collections.Generic.List<int> BoxCount { get; set; } = new(new int[0x78]); // contador por celula (pocao empilha; gear=1)
        public System.Collections.Generic.List<int> BoxLevel { get; set; } = new(new int[0x78]); // nível de refino por célula
        public System.Collections.Generic.List<int> BoxRowId { get; set; } = new(new int[0x78]); // id canônico de useriteminfo
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
        public byte PendingRoomMap;                          // map do 0x3b (sala criada) -> aplicado ao Field no 0x4b
        public byte PendingRoomMode;                         // mode do 0x3b: 0=stage (client-side), !=0=Battle/PvP (networked)
        public ushort PendingRoomDurationSec;                // duracao do round em SEGUNDOS (u16 do 0x3b, 290..1210)
        public byte PendingRoomRounds;                       // rounds configurados na sala (byte do 0x3b, <0x16; stage=1)
        public string PendingRoomName = "";                  // nome da sala (0x3b) -> match-end 0x44 (era "asdd" hardcoded)
        public Guid StageRunId { get; private set; }
        public byte ActiveStageId { get; private set; }
        public byte StageRunPreviousBestRank { get; private set; }
        public bool StageRunCleared { get; private set; }
        public IReadOnlyList<CellProgressionChange>? StageRunCellChanges { get; set; }

        public void BeginStageRun(Guid runId, byte stageId, byte previousBestRank)
        {
            StageRunId = runId;
            ActiveStageId = stageId;
            StageRunPreviousBestRank = previousBestRank;
            StageRunCleared = false;
            StageRunCellChanges = null;
        }

        public bool MarkStageRunCleared()
        {
            if (StageRunId == Guid.Empty || ActiveStageId == 0) return false;
            StageRunCleared = true;
            return true;
        }

        public void FinishStageRun()
        {
            StageRunId = Guid.Empty;
            ActiveStageId = 0;
            StageRunPreviousBestRank = 0;
            StageRunCleared = false;
            StageRunCellChanges = null;
        }

        // estado de combate/field (campos do user[slot] resolvidos por FUN_0040b7d0 e helpers de field)
        public ushort FieldTargetIndex; // user+0x14a0 (indice do field-objeto alvo resolvido por FUN_0040b7d0)
        public byte FieldTargetOwner;  // user+0x14a2 (byte de owner/slot do alvo resolvido por FUN_0040b7d0)
        public int ActionCounterA;        // user+0x2395, zerado em morte nos modos 2/3
        public ushort ActionCounterB;     // user+0x2399
        public byte FieldRecordState;     // playerRecord +0x8 (estado do registro; 2 = inativo p/ acao)
        public ushort FieldObjectIndex = Domain.Field.NoSeat; // user+0x14a0 (índice no field; 0x14 = nenhum)
        public byte FieldSeat = Domain.Field.NoSeat;          // user+0x14a2 (seat; 0x14 = nenhum)
        public bool ExpBonusActive;     // user+0x236c != 0 (bônus de exp ativo; no nosso server = PU ativo)
        public bool PuActive;           // PU vigente (usergameinfo.powertimedate futuro)

        /// <summary>Aplica o multiplicador de XP do PU (pu_config, ciente de promoção) quando o bônus está
        /// ativo. Substitui o ×3/2 fixo do original por um fator configurável.</summary>
        public uint BonusExp(uint exp) =>
            (uint)(exp * EffectiveExpMultiplier(System.DateTime.Now));

        /// <summary>Aplica o multiplicador de gold do PU (pu_config, ciente de promoção) quando o PU está ativo.</summary>
        public uint BonusGold(uint gold) =>
            (uint)(gold * EffectiveGoldMultiplier(System.DateTime.Now));

        private ushort _clientSeq;   // user+0x146e (ultimo seq recebido)
        private long _lastKeepAliveTick = Environment.TickCount64;
        private int _gameGuardChallengeSent; // 0x10 e assíncrono ao UDP; trava atômica evita duplicar no fallback 0x0e
        private readonly int[] _potionSlot = new int[0x13];   // quickslot/equip = user+0x1da4 (19 celulas; type1 do move 0x31)
        private readonly int[] _potionCount = new int[0x13];  // quantidade empilhada por celula (contador 'v' do 0x31)
        private readonly int[] _potionLevel = new int[0x13];  // nivel de refino (+N) das celulas type1 — segue o item no move
        private readonly int[] _potionRowId = new int[0x13];  // id de useriteminfo na zona ativa
        private readonly bool[] _fieldPotionUsed = new bool[0x13];
        private readonly int[] _fieldPotionPending = new int[0x13];
        private readonly object _fieldPotionSync = new();
        private bool _potionPainted;          // quickslot pintado no 1o open (0x2c) da sessao — fallback do auto-render
        private bool _potionLoginPainted;     // quickslot pintado na entrada do lobby (0x14) — auto-render no relog (CONFIRMADO)

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

        internal long RecordKeepAlive()
        {
            long now = Environment.TickCount64;
            long elapsed = now - _lastKeepAliveTick;
            _lastKeepAliveTick = now;
            return elapsed;
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
                    Log.Debug("client", "[{0}] RX {1} bytes", Slot, n);
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

                        Log.Debug("client", "[{0}] <- opcode={1:X4} seq={2} data={3}",
                            Slot, opcode, seq, FormatPayloadForLog(opcode, data));
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

        internal static string FormatPayloadForLog(ushort opcode, byte[] data) =>
            opcode == Protocol.Op.Login ? $"<{data.Length}B redacted>" : Convert.ToHexString(data);

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
        /// payload e cifrado em AES quando a cripto esta ligada (this+0x208&amp;1). Start() aplica
        /// a chave e o IV v258 reconstruídos em FUN_00403c10/FUN_00401000.
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
            DisconnectReason = reason;
            Log.Warn("client", "[{0:0000}] DISC {1:000}", Slot, reason);
            if (Connected && SlotActive)
            {
                byte[] body = SessionControlFrames.Disconnect(
                    ConnectionLogId, reason, GameInfoId);
                try { SendMessage(Protocol.SubType.Disconnect, body); } catch { }
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
        public byte Bag = 1;
        public byte CharacterSlots = 4;
    }
}
