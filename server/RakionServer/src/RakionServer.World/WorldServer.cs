using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using RakionServer.Common;
using RakionServer.World.Database;
using RakionServer.World.Domain;
using RakionServer.World.Infrastructure;
using RakionServer.World.Network;
using RakionServer.World.CharSelect;

namespace RakionServer.World
{
    /// <summary>
    /// Objeto central do world (equivalente ao CWorld do worldserv.exe): aceita
    /// conexoes TCP (porta do jogo), liga os sockets UDP de gameplay, mantem o
    /// canal IPC com o broker e o estado das sessoes/usuarios.
    /// </summary>
    public sealed partial class WorldServer
    {
        private readonly WorldConfig _cfg;
        private readonly WorldDatabase _db;
        private readonly ChatModerationEngine _chatModeration;
        private readonly ICharacterDeleteNotifier _characterDeleteNotifier;
        private BrokerLink? _broker;

        private Socket? _listener;
        private Network.UdpGameplay? _udpGame;
        private CancellationTokenSource? _cts;

        private readonly ConcurrentDictionary<ushort, ClientSession> _sessions = new();
        private readonly ConcurrentDictionary<Guid, byte> _settlementsInFlight = new();
        private readonly ConcurrentDictionary<Guid, byte> _settlementsApplied = new();
        private readonly ConcurrentDictionary<int, SemaphoreSlim> _characterLocks = new();
        private readonly SemaphoreSlim _characterNameLock = new(1, 1);
        private readonly SemaphoreSlim _buddyIdentityLock = new(1, 1);
        private int _currentUsers;
        private int _nextSlotHint;

        /// <summary>Grupos/IDCs externos (this+0x60/0x64/0x68), validados pelo opcode 0x01.</summary>
        public readonly List<Domain.WorldGroup> Groups = new() { new Domain.WorldGroup(1, false) };

        /// <summary>Canais sociais internos (this+0xd8/0xdc). Padrão v258: owner 100 + channel01.</summary>
        public readonly List<Domain.Channel> Channels = new()
        {
            new Domain.Channel(0, new Domain.ChannelOptions { Name = LobbyFrames.ChannelName })
        };

        /// <summary>Variaveis de servidor setaveis por GM (this+0x51c8, 0x200 entradas u32). Opcodes 0x08/0x0a.</summary>
        public readonly uint[] GmVars = new uint[0x200];

        /// <summary>Referência e contador do challenge/echo World 0x61.</summary>
        public int EchoReference;
        public int EchoMatchCount;

        /// <summary>Fields/partidas (this+0xe4) e rooms/chat (this+0xdc).</summary>
        public readonly List<Domain.Field> Fields = new();
        public readonly List<Domain.Room> Rooms = new() { new Domain.Room(0) };
        private readonly object _fieldCreationLock = new();

        public Domain.Field? GetField(int id)
        {
            if (id < 0) return null;
            lock (Fields) return Fields.Find(field => field.Id == id);
        }
        public Domain.Room? GetRoom(int id) => id < 0 ? null : Rooms.Find(r => r.Id == id);

        // O paginador de FUN_00422C90 usa zero como cursor/sentinela; fields pesquisáveis
        // começam em 1. Os cinco modos, inclusive Stage (mode=0), ocupam a lista pública.
        /// <summary>
        /// Aloca um field/sala (espelha a varredura de this+0xe4 por slot livre no
        /// RoomCreate FUN_00423580). Cria a entrada no dominio e devolve o Field.
        /// </summary>
        public Domain.Field CreateField(RoomCreationOptions options, ClientSession master)
        {
            lock (_fieldCreationLock)
            {
                int fieldId;
                lock (Fields) fieldId = FindFreeFieldId();
                if (fieldId < 0)
                    throw new InvalidOperationException("O limite de salas do World foi atingido.");

                var field = new Domain.Field(fieldId)
                {
                    Name = options.Name,
                    CreatorCharacterName = master.CharName,
                    Password = options.Password,
                    Description = options.Description,
                    Searchable = options.Searchable,
                    Mode = options.Mode,
                    MapId = options.MapId,
                    MaxPlayers = options.Capacity,
                    MaxRounds = options.Rounds,
                    RoundDurationSec = options.DurationSeconds,
                    FragLimit = options.FragLimit,
                    MinLevel = options.MinLevel,
                    MaxLevel = options.MaxLevel,
                    LevelRangeCode = options.LevelRangeCode,
                    State = 1, // ocupado (field+8 != 0)
                    // State=1 também representa match encerrado. Uma sala recém-criada não possui
                    // resultado pendente; ResetMatch troca para false somente no start autorizado.
                    Settled = true,
                };
                field.InitializeLobbySlots();
                if (!JoinField(master, field, true))
                    throw new InvalidOperationException("Não foi possível alocar o master da sala.");
                lock (Fields) Fields.Add(field);
                Log.Info("field", "[{0}] criou field {1} '{2}' (map={3} mode={4} cap={5})",
                    master.Slot, field.Id, options.Name, options.MapId, options.Mode,
                    field.MaxPlayers);
                return field;
            }
        }

        private int FindFreeFieldId()
        {
            int upperBound = Math.Clamp(_cfg.MaxField, 1, ushort.MaxValue + 1);
            for (int id = 1; id < upperBound; id++)
                if (!Fields.Exists(field => field.Id == id)) return id;
            return -1;
        }

        public bool JoinField(ClientSession session, Domain.Field field, bool asMaster)
        {
            if (session.FieldId >= 0 && session.FieldId != field.Id) LeaveField(session);
            lock (field.SyncRoot)
            {
                if (field.State != 1 || field.Count >= field.MaxPlayers ||
                    (!asMaster && field.IsVotePenalized(session, Environment.TickCount64))) return false;
                field.Add(session);
                int seat = field.AssignSeat(session);
                if (seat < 0)
                {
                    field.Remove(session);
                    return false;
                }
                session.FieldId = field.Id;
                session.FieldSeat = (byte)seat;
                session.FieldObjectIndex = (ushort)seat;
                field.Slots[seat].UsesTunneling =
                    _cfg.ForceTunneling || session.UdpObservedEndpoint == null;
                session.InField = true;
                session.FieldSecondary = true;
                session.SecondActive = true;
                session.Status = Domain.UserStatus.FieldLobby;
                if (asMaster)
                {
                    field.Master = session;
                    field.MasterSlot = seat;
                }
                Log.Info("room", "[{0}] entrou na sala {1} seat={2} master={3}",
                    session.Slot, field.Id, seat, asMaster);
                return true;
            }
        }

        public RoomJoinStatus TryJoinRoom(
            ClientSession session, Domain.Field field, string password)
        {
            lock (field.SyncRoot)
            {
                if (field.State == 0) return RoomJoinStatus.Unavailable;
                if (field.State == 2) return RoomJoinStatus.InGame;
                if (session.CharLevel < field.MinLevel || session.CharLevel > field.MaxLevel)
                    return RoomJoinStatus.Ineligible;
                if (field.IsVotePenalized(session, Environment.TickCount64))
                    return RoomJoinStatus.VotePenalty;
                if (field.Count >= field.MaxPlayers) return RoomJoinStatus.Full;
                if (!string.Equals(field.Password, password, StringComparison.Ordinal))
                    return RoomJoinStatus.InvalidPassword;
                return JoinField(session, field, false)
                    ? RoomJoinStatus.Success
                    : RoomJoinStatus.Full;
            }
        }

        public Domain.RoomListSnapshot[] ListJoinableFields(int startId, int maxCount)
        {
            Domain.Field[] fields;
            lock (Fields) fields = Fields.ToArray();
            return fields
                .Where(field => field.Searchable)
                .Select(field => field.CaptureRoomListSnapshot())
                .Where(field => field.FieldId >= startId && !field.InGame)
                .Take(Math.Clamp(maxCount, 0, 10))
                .ToArray();
        }

        public Domain.RoomListSnapshot[] ListJoinableFields(
            ClientSession session, Domain.RoomListQuery query)
        {
            Domain.Field[] fields;
            lock (Fields) fields = Fields.ToArray();
            Domain.RoomListSnapshot[] candidates = fields
                .Where(field => field.Searchable)
                .Select(field => field.CaptureRoomListSnapshot())
                .Where(field => query.Includes(field, session.CharLevel))
                .OrderBy(field => field.FieldId)
                .ToArray();
            Domain.RoomListSnapshot[] page = (query.Forward
                ? candidates.Where(field => field.FieldId > query.Cursor)
                : candidates.Where(field => field.FieldId < query.Cursor)
                    .OrderByDescending(field => field.FieldId))
                .Take(query.MaxCount)
                .ToArray();
            if (page.Length > 0 || query.Cursor == 0 || candidates.Length == 0) return page;
            return query.Forward
                ? candidates.TakeLast(query.MaxCount).ToArray()
                : candidates.Take(query.MaxCount).ToArray();
        }

        public bool TryQuickJoinField(ClientSession session, out Domain.Field? joinedField)
        {
            Domain.Field[] fields;
            lock (Fields) fields = Fields.ToArray();
            foreach (Domain.Field field in fields)
            {
                lock (field.SyncRoot)
                {
                    if (!field.Searchable || field.Mode == 0 || field.Master == session) continue;
                    if (TryJoinRoom(session, field, "") == RoomJoinStatus.Success)
                    {
                        joinedField = field;
                        return true;
                    }
                }
            }
            joinedField = null;
            return false;
        }

        /// <summary>
        /// Garante que a sessao tem um Field ativo com seat alocado (ponte da cadeia de entrada
        /// 0x3b/0x4b — proven-working — para o modelo de partida real). Solo: cria um field
        /// dedicado; multi: reusa o field ja associado (FieldId). Marca State=2 (em jogo) p/ o
        /// motor rodar e seta os campos de seat do user (FUN_0040b7b0).
        /// </summary>
        public Domain.Field EnsureFieldForSession(ClientSession s)
        {
            Domain.Field f = GetField(s.FieldId)
                ?? CreateField(new RoomCreationOptions
                {
                    Name = s.CharName.Length > 0 ? s.CharName : $"field{s.Slot}",
                    CapacityOverride = 8
                }, s);
            lock (f.SyncRoot)
            {
                f.State = 2;
                int seat = f.AssignSeat(s);
                if (seat >= 0) { s.FieldSeat = (byte)seat; s.FieldObjectIndex = (ushort)seat; }
                if (f.MasterSlot < 0 || f.MasterSlot >= 0x14) f.MasterSlot = seat;
            }
            return f;
        }

        /// <summary>Remove o usuario do field; se ficar vazio, libera o field.</summary>
        public void LeaveField(ClientSession s)
        {
            var f = GetField(s.FieldId);
            if (f == null) return;
            lock (f.SyncRoot)
            {
                RemoveFieldMember(f, s);
            }
        }

        public bool TryKickFieldMember(
            ClientSession requester, byte targetSeat, out ClientSession? victim)
        {
            victim = null;
            var field = GetField(requester.FieldId);
            if (field == null) return false;
            lock (field.SyncRoot)
            {
                if (field.Master != requester) return false;
                var record = field.RecAt(targetSeat);
                if (record?.Session == null || record.Session == requester || !record.Occupied)
                    return false;
                victim = record.Session;
                RemoveFieldMember(field, victim);
                return true;
            }
        }

        public bool TryRemoveFieldMember(
            ClientSession requester, byte targetSeat, out ClientSession? victim,
            out bool unauthorized)
        {
            victim = null;
            unauthorized = false;
            var field = GetField(requester.FieldId);
            if (field == null) return false;
            lock (field.SyncRoot)
            {
                var requesterRecord = field.FindRec(requester);
                if (requesterRecord == null) return false;
                if (requester.SubStatus != Domain.UserSubStatus.Gm &&
                    requester.SubStatus != Domain.UserSubStatus.Special &&
                    requesterRecord.Slot != field.MasterSlot)
                {
                    unauthorized = true;
                    return false;
                }
                var record = field.RecAt(targetSeat);
                if (record?.Session == null || !record.Occupied ||
                    record.Session.SubStatus == Domain.UserSubStatus.Special)
                    return false;
                victim = record.Session;
                RemoveFieldMember(field, victim);
                return true;
            }
        }

        private void RemoveFieldMember(Domain.Field field, ClientSession session)
        {
            if (field.State == 1 && !field.Settled) _ = SettleMatchAsync(field);
            byte departedSeat = session.FieldSeat;
            bool wasMaster = field.Master == session;
            var voteFinal = field.CancelVoteForDeparture(departedSeat);
            if (voteFinal != null)
                field.BroadcastFieldPlaying(0x5f, FieldVoteFrames.ResultBody(voteFinal), session);
            bool roundEnded = field.ApplyPlayerDeparture(departedSeat);
            bool tunnelingDisabled =
                field.UnregisterTunnelingPresence(departedSeat) == Domain.TunnelingPresenceChange.Disabled;
            field.Remove(session);
            ClearFieldState(session);
            if (field.Count == 0)
            {
                lock (Fields) Fields.Remove(field);
                Log.Info("field", "field {0} '{1}' liberado (vazio)", field.Id, field.Name);
                return;
            }

            field.BroadcastField(0x3a, new[] { departedSeat });
            if (tunnelingDisabled) field.BroadcastField(0x55, Array.Empty<byte>());
            if (roundEnded) field.BroadcastFieldPlaying(0x4a, field.Build0x4a());
            if (!wasMaster) return;
            var replacement = FindReplacementMaster(field);
            if (replacement?.Session == null) return;
            field.Master = replacement.Session;
            field.MasterSlot = replacement.Slot;
            field.BroadcastField(0x3c, new[] { (byte)replacement.Slot });
            Log.Info("room", "field {0}: master transferido para sessão {1}",
                field.Id, replacement.Session.Slot);
        }

        private static Domain.PlayerRec? FindReplacementMaster(Domain.Field field)
        {
            foreach (byte state in new byte[] { 4, 3 })
            {
                var preferred = Array.Find(field.Slots,
                    record => record.State == state && record.Session != null);
                if (preferred != null) return preferred;
            }
            return Array.Find(field.Slots,
                record => record.Occupied && record.Session != null);
        }

        public bool TryCloseField(ClientSession requester, out ClientSession[] members)
        {
            members = Array.Empty<ClientSession>();
            var field = GetField(requester.FieldId);
            if (field == null) return false;
            lock (field.SyncRoot)
            {
                if (field.Master != requester) return false;
                members = field.Players.ToArray();
                foreach (var member in members)
                {
                    field.Remove(member);
                    ClearFieldState(member);
                }
                field.Master = null;
                field.MasterSlot = -1;
                field.State = 0;
                lock (Fields) Fields.Remove(field);
                Log.Info("room", "field {0} '{1}' fechado pelo host", field.Id, field.Name);
                return true;
            }
        }

        private static void ClearFieldState(ClientSession session)
        {
            session.FieldId = -1;
            session.FieldSeat = Domain.Field.NoSeat;
            session.FieldObjectIndex = Domain.Field.NoSeat;
        }

        public WorldServer(
            WorldConfig cfg, WorldDatabase db, ICharacterDeleteNotifier? characterDeleteNotifier = null)
        {
            _cfg = cfg;
            _db = db;
            _chatModeration = BuildChatModeration();
            _characterDeleteNotifier = characterDeleteNotifier ??
                new CharacterDeletePickupNotifier(cfg.CharacterDelete);
        }

        /// <summary>Subsistema de bots (peers sintéticos server-side). Ver <see cref="BotManager"/>.</summary>
        public BotManager Bots { get; } = new();

        /// <summary>Mensagem de sistema para UMA sessão (feedback de comando), via chat de canal.</summary>
        public void WhisperSystem(ClientSession target, string message)
        {
            byte slot = target.ChannelSlot == byte.MaxValue ? (byte)0 : target.ChannelSlot;
            try { target.SendLobby(Network.LobbyFrames.ChannelChat(slot, message)); } catch { }
        }

        public void ResolveBotMeleeAttack(
            ClientSession attacker, System.Action<byte[]> publishHit, int damage = 34)
        {
            var field = GetField(attacker.FieldId);
            if (field == null || field.State != 2 || field.BotCount == 0) return;
            lock (field.SyncRoot)
            {
                var attackerRec = field.FindRec(attacker);
                if (attackerRec == null) return;
                long now = Environment.TickCount64;
                if (!Domain.BotCombat.TryResolveMeleeAttack(
                    field, attackerRec, now, damage, out var hit)) return;

                BotPlayer bot = hit.BotRecord.Bot!;
                bot.BeginHitReaction(now);
                byte botSeat = (byte)hit.BotRecord.Slot;
                publishHit(Network.BotMovement.SynthesizeMove(
                    botSeat, bot.Position, bot.Heading, ++bot.MoveSeq));
                publishHit(Network.BotMovement.SynthesizeDamage(botSeat, ++bot.MoveSeq));
                Log.Debug("bot", "melee validado: humano seat {0} -> bot seat {1}: hp={2}/{3}",
                    attackerRec.Slot, botSeat, bot.Health, bot.MaxHealth);
                if (!hit.Died) return;

                if (hit.BotRecord.State != 4) hit.BotRecord.State = 4;
                var death = field.ApplyReportedDeath(botSeat, attackerRec.Slot, 0);
                hit.BotRecord.Dead = true;
                bot.ScheduleRespawn(now, BotRespawnPolicy.DelayMs(field.Mode));
                Bots.PublishBotLifecycles(field);
                if (death.Processed)
                    field.BroadcastFieldPlaying(0x4f,
                        new byte[] { botSeat, 0, (byte)attackerRec.Slot, death.ScoreA, death.ScoreB });
                Log.Ok("bot", "bot seat {0} morto por humano seat {1} (field {2}); respawn={3}ms",
                    botSeat, attackerRec.Slot, field.Id, BotRespawnPolicy.DelayMs(field.Mode));
            }
        }

        public bool Locked { get; private set; }                 // this+0x50 (servidor fechado p/ GM)
        public PuConfig PuConfig { get; private set; } = new();   // pu_config: preço/bônus/multiplicadores do PU (lida no boot)
        public EnchantConfig EnchantConfig { get; private set; } = new();   // enchant_*: coeficientes do refino (lida no boot)
        public int MaxUser => _cfg.MaxUser;                       // this+0x536c
        public int CurrentUsers => Volatile.Read(ref _currentUsers);

        /// <summary>Sessoes ativas (iteracao thread-safe sobre o dicionario).</summary>
        public IEnumerable<ClientSession> Sessions => _sessions.Values;

        /// <summary>Sessao por slot (indice do pacote UDP de gameplay).</summary>
        public ClientSession? GetSession(ushort slot) => _sessions.TryGetValue(slot, out var s) ? s : null;

        public ClientSession? GetSessionByUdpEndpoint(IPEndPoint endpoint)
        {
            foreach (var s in _sessions.Values)
                if (s.Connected && s.SlotActive &&
                    ((s.UdpEndpoint1?.Equals(endpoint) ?? false) || (s.UdpEndpoint2?.Equals(endpoint) ?? false)))
                    return s;
            return null;
        }

        private byte[]? AcceptUdpPort1Handshake(IPEndPoint endpoint, byte[] packet) =>
            AcceptUdpHandshake(endpoint, packet, GameplayUdpHandshake.Port1Type, 0);

        public byte[]? AcceptUdpPort2Handshake(IPEndPoint endpoint, byte[] packet) =>
            AcceptUdpHandshake(endpoint, packet, GameplayUdpHandshake.Port2Type, 1);

        private byte[]? AcceptUdpHandshake(IPEndPoint endpoint, byte[] packet, ushort type, byte endpointIndex)
        {
            if (!GameplayUdpHandshake.TryParse(packet, type, out var handshake)) return null;
            ClientSession? session = GetSession(handshake.Slot);
            if (session == null || !session.Connected || !session.SlotActive) return null;
            if (!IPAddress.TryParse(session.RemoteIp, out var tcpAddress) || !tcpAddress.Equals(endpoint.Address))
            {
                Log.Warn("udp", "[{0}] handshake rejeitado: IP UDP {1} difere do TCP {2}",
                    handshake.Slot, endpoint.Address, session.RemoteIp);
                return null;
            }
            if (handshake.SessionKey != session.UdpKey)
            {
                Log.Warn("udp", "[{0}] handshake rejeitado: chave {1:X8} inválida", handshake.Slot, handshake.SessionKey);
                return null;
            }

            session.NotifyUdpReady(endpoint, handshake.AdvertisedEndpoint, endpointIndex);
            if (endpointIndex == 0)
                _ = _db.UpdateConnectionRealIpAsync(
                    session.ConnectionLogId, handshake.AdvertisedEndpoint.Address.ToString());
            Log.Info("udp", "[{0}] endpoint UDP{1} autenticado em {2}; P2P anunciado {3}",
                handshake.Slot, endpointIndex + 1, endpoint, handshake.AdvertisedEndpoint);
            return handshake.BuildEcho(endpointIndex);
        }

        /// <summary>Envia um tick de gameplay (1583 + SEQ) ao endpoint UDP do jogador.</summary>
        public void SendGameplayTick(System.Net.IPEndPoint to, byte seq) => _udpGame?.SendTick(to, seq);

        /// <summary>
        /// Motor da partida por-field (FUN_00409940 + FUN_004069a0): roda a maquina de estado
        /// de cada field ativo (Pre->Playing->RoundEnd->proximo-round/fim) e dispara os
        /// broadcasts (0x48/0x49/0x4a/0x44). Roda em loop unico (~200ms), NAO por-sessao.
        /// Tambem mantem o tick 1583 (UDP) idle a cada ~150ms p/ os players in-field.
        /// </summary>
        private async Task FieldEngineLoopAsync(CancellationToken ct)
        {
            Log.Ok("field", "motor da partida (FieldEngine) iniciado");
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    Domain.Field[] snapshot;
                    lock (Fields) snapshot = Fields.ToArray();
                    foreach (var f in snapshot)
                    {
                        if (f.State == 2) MatchTick(f);
                        else if (f.State == 1 && !f.Settled) await SettleMatchAsync(f);
                    }

                }
                catch (Exception ex) { Log.Debug("field", "engine tick: {0}", ex.Message); }
                await Task.Delay(100, ct).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }

        /// <summary>
        /// Relogio de gameplay 1583 (150ms) APENAS p/ salas BATTLE/PvP (Mode != 0): GameSeq
        /// INCREMENTA a cada tick — e' o frame/clock da partida; seq fixo congela o personagem e
        /// cadencia errada deixa o cliente congelado ate o seq alinhar (~2min observados a 200ms).
        /// Solo stage (Mode 0) e' client-side: sem tick (eco/timer no solo interrompia combos).
        /// Loop dedicado p/ manter os 150ms (o engine loop dorme 100ms -> tick efetivo de 200ms).
        /// </summary>
        private async Task GameClockLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    Domain.Field[] snapshot;
                    lock (Fields) snapshot = Fields.ToArray();
                    foreach (var f in snapshot)
                    {
                        lock (f.SyncRoot)
                        {
                            if (f.State != 2) continue;
                            foreach (var r in f.Slots)
                            {
                                var s = r.Session;
                                if (s == null || !r.Occupied || s.UdpEndpoint == null) continue;
                                unchecked { s.GameSeq++; }
                                _udpGame?.SendTick(s.UdpEndpoint, s.GameSeq);
                            }
                        }
                        // Tick de IA/movimento dos bots (fora do lock do clock; TickField trava o field).
                        if (f.BotCount > 0 && _udpGame != null)
                            Bots.TickField(f, 0.15f, _udpGame.SendBotGameplay);
                    }
                }
                catch (Exception ex) { Log.Debug("field", "game clock: {0}", ex.Message); }
                await Task.Delay(150, ct).ContinueWith(_ => { }, TaskScheduler.Default);
            }
        }

        // escrito pelo engine loop E pelos handlers de sessao (NotifyPlayerReady) -> concurrent
        private readonly ConcurrentDictionary<int, long> _fieldStatusBeat = new();

        /// <summary>
        /// Um tick do motor de um field (FUN_00409940). Avanca as fases pelo deadline (field+0x2b8),
        /// re-broadcasta 0x48 (FieldStatus) a cada ~1s, e dispara fim-de-round / fim-de-match.
        /// </summary>
        private void MatchTick(Domain.Field f)
        {
            long now = Environment.TickCount64;
            lock (f.SyncRoot)
            {
                Domain.FieldVoteFinal? voteFinal = f.TickVote(now);
                if (voteFinal != null)
                {
                    f.BroadcastFieldPlaying(0x5f, Network.FieldVoteFrames.ResultBody(voteFinal));
                    Log.Info("vote", "field {0}: voto expirou/finalizou yes={1} no={2} abstain={3} penalidade={4}",
                        f.Id, voteFinal.Yes, voteFinal.No, voteFinal.Abstain, voteFinal.PenaltyApplied);
                }
                if (f.Phase == Domain.MatchPhase.Playing && f.Mode != 0 &&
                    f.Warned30 == 0 && f.DeadlineMs - now <= 30000)
                {
                    f.Warned30 = 1;
                    Log.Info("field", "field {0} round {1}: 30s restantes", f.Id, f.Round);
                }

                Domain.MatchLifecycleTransition transition = f.AdvanceLifecycle(now);
                switch (transition.Event)
                {
                    case Domain.MatchLifecycleEvent.EngageStarted:
                        f.BroadcastLobby(f.Build0x48());
                        break;
                    case Domain.MatchLifecycleEvent.RoundTimedOut:
                        f.BroadcastFieldPlaying(0x4a, f.Build0x4a());
                        break;
                    case Domain.MatchLifecycleEvent.NextRoundStarted:
                        f.BroadcastLobby(f.Build0x49());
                        Log.Ok("field", "field {0} -> round {1}/{2} (w0={3} w1={4})",
                            f.Id, f.Round, f.MaxRounds, f.Wins0, f.Wins1);
                        break;
                    case Domain.MatchLifecycleEvent.MatchEnded:
                        f.BroadcastLobby(f.BuildMatchEnd(transition.Reason));
                        Bots.PublishBotLifecycles(f);
                        _fieldStatusBeat.TryRemove(f.Id, out _);
                        break;
                }
            }
        }

        /// <summary>
        /// Liquida o resultado do MATCH no DB (roda 1x apos EndMatch, field+8==1): incrementa
        /// win/lose/draw do characterinfo de cada jogador conforme o time vencedor (Wins0 vs
        /// Wins1; empate = draw p/ todos) e atualiza o overlay em memoria (CharWin/Lose/Draw).
        /// Mode 0 (solo PvE) nao liquida — o resultado vem do cliente pelos 0x50/0x53.
        /// </summary>
        private async Task SettleMatchAsync(Domain.Field f)
        {
            MatchSettlementSnapshot? snapshot;
            lock (f.SyncRoot)
            {
                if (f.Settled) return;
                if (f.Mode == 0) { f.Settled = true; return; }
                if (f.MatchId == Guid.Empty)
                {
                    Log.Error("field", "field {0}: match sem identidade; settle adiado", f.Id);
                    return;
                }
                snapshot = CaptureSettlement(f);
                if (snapshot.Players.Count == 0) { f.Settled = true; return; }
            }

            if (!_settlementsInFlight.TryAdd(snapshot.MatchId, 0)) return;
            try
            {
                Database.MatchSettlementEntry[] pending = snapshot.Players
                    .Select(player => new Database.MatchSettlementEntry(
                        player.CharacterId, player.Win, player.Lose, player.Draw))
                    .ToArray();
                if (!await _db.SettleMatchAsync(snapshot.MatchId, pending)) return;

                if (_settlementsApplied.TryAdd(snapshot.MatchId, 0))
                {
                    foreach (MatchSettlementPlayer player in snapshot.Players)
                    {
                        player.Session.CharWin += (uint)player.Win;
                        player.Session.CharLose += (uint)player.Lose;
                        player.Session.CharDraw += (uint)player.Draw;
                        Log.Ok("field", "field {0} settle: char {1} seat {2} -> {3} (score {4})",
                            snapshot.FieldId, player.CharacterId, player.Seat,
                            player.Win != 0 ? "WIN" : player.Lose != 0 ? "LOSE" : "DRAW",
                            player.ResultPoints);
                    }
                }

                lock (f.SyncRoot)
                    if (f.MatchId == snapshot.MatchId && f.State == 1) f.Settled = true;
            }
            finally
            {
                _settlementsInFlight.TryRemove(snapshot.MatchId, out _);
            }
        }

        internal Task SettleEndedMatchAsync(Domain.Field field) => SettleMatchAsync(field);

        private static MatchSettlementSnapshot CaptureSettlement(Domain.Field field)
        {
            byte winner = field.Wins0 > field.Wins1 ? (byte)0 :
                field.Wins1 > field.Wins0 ? (byte)1 : (byte)2;
            var players = new List<MatchSettlementPlayer>();
            foreach (Domain.PlayerRec record in field.Slots)
            {
                ClientSession? session = record.Session;
                if (session == null || !record.Occupied || session.ActiveCharId <= 0) continue;
                (int win, int lose, int draw) = MatchOutcome(record.Team, winner);
                players.Add(new(session, session.ActiveCharId, record.Slot, record.ResultPoints,
                    win, lose, draw));
            }
            return new(field.MatchId, field.Id, players);
        }

        private sealed record MatchSettlementSnapshot(
            Guid MatchId, int FieldId, IReadOnlyList<MatchSettlementPlayer> Players);

        private sealed record MatchSettlementPlayer(
            ClientSession Session, int CharacterId, int Seat, uint ResultPoints,
            int Win, int Lose, int Draw);

        private static (int Win, int Lose, int Draw) MatchOutcome(byte team, byte winner) =>
            winner == 2 ? (0, 0, 1) : team == winner ? (1, 0, 0) : (0, 1, 0);

        /// <summary>
        /// Dispara o 0x48 FieldStatus de inicio (handler 0x48 / FUN_00408440): o player marcou ready.
        /// Se a partida (re)iniciou, broadcasta o 0x48 a todos. Usado pelos handlers de campo.
        /// </summary>
        public void NotifyPlayerReady(Domain.Field f, ClientSession s)
        {
            lock (f.SyncRoot)
            {
                long now = Environment.TickCount64;
                Domain.PlayerReadyTransition transition = f.OnPlayerReady(s, now);
                switch (transition)
                {
                    case Domain.PlayerReadyTransition.Started:
                        _fieldStatusBeat[f.Id] = now;
                        f.BroadcastLobby(f.Build0x48());
                        int replayed = 0;
                        foreach (Domain.PlayerRec record in f.Slots)
                            if (record.Playing && record.Session != null)
                                replayed += f.ReplayInitialMovementsTo(record.Session);
                        Log.Ok("field", "[{0}] partida iniciada no field {1} (0x48 a {2} player(s), replay 0x4B={3})",
                            s.Slot, f.Id, f.CountPlaying(), replayed);
                        break;
                    case Domain.PlayerReadyTransition.JoinedPlaying:
                        s.SendEncryptedFrame(f.Build0x48());
                        int lateReplay = f.ReplayInitialMovementsTo(s);
                        Log.Info("field", "[{0}] entrou no round em andamento do field {1} (replay 0x4B={2})",
                            s.Slot, f.Id, lateReplay);
                        break;
                    case Domain.PlayerReadyTransition.JoinedRoundEnd:
                        s.SendMessage(0x4a, f.Build0x4a());
                        Log.Info("field", "[{0}] sincronizou intermissão do field {1}", s.Slot, f.Id);
                        break;
                    case Domain.PlayerReadyTransition.Waiting:
                        Log.Info("field", "[{0}] field {1}: carregamento confirmado; aguardando {2} player(s)",
                            s.Slot, f.Id, f.CountReady());
                        break;
                    default:
                        string playerState = f.FindRec(s)?.State.ToString() ?? "ausente";
                        Log.Info("field", "[{0}] field {1}: 0x48 ignorado para state={2} fase={3}",
                            s.Slot, f.Id, playerState, f.Phase);
                        break;
                }
            }
        }

        /// <summary>Define o estado "servidor fechado" (GM open/close, opcode 0x03).</summary>
        public void SetLocked(bool locked) => Locked = locked;

        /// <summary>Desconecta todos os usuários sem authority GM.</summary>
        public void DisconnectNonGm(byte reason)
        {
            foreach (var s in _sessions.Values)
                if (s.Status != 0 && !s.IsGm)
                    s.Disconnect(reason);
        }
        public WorldConfig Config => _cfg;

        public async Task StartAsync()
        {
            _cts = new CancellationTokenSource();

            // TCP do jogo
            _listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            _listener.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listener.Bind(new IPEndPoint(IPAddress.Any, _cfg.Port));
            _listener.Listen(128);
            Log.Ok("world", "TCP do jogo ouvindo na porta {0}", _cfg.Port);

            // canal IPC com o broker — fica dono da porta UDP de IPC (= _cfg.Port,
            // a mesma usada pelo gameplay no original; aqui o BrokerLink a possui).
            _broker = new BrokerLink(_cfg, GetStats,
                AcceptUdpPort1Handshake, _cfg.BrokerCode);
            _broker.Start();

            // UDP de gameplay (recv + relay aos peers do field). Roda na porta de gameplay
            // que nao colide com o IPC do broker (Port1 == _cfg.Port e do broker; usa Port2).
            int gamePort = _cfg.UdpPort2 != _cfg.Port ? _cfg.UdpPort2 : _cfg.UdpPort1;
            if (gamePort != _cfg.Port)
            {
                _udpGame = new UdpGameplay(
                    this, gamePort, _cfg.UdpRelayCompatibilityEnabled,
                    _cfg.UdpRelayPacketsPerSecond, _cfg.UdpRelayBurst);
                _udpGame.Start();
            }

            _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
            _ = Task.Run(() => FieldEngineLoopAsync(_cts.Token)); // motor da partida por-field (FUN_00409940)
            _ = Task.Run(() => GameClockLoopAsync(_cts.Token));   // relogio 1583 (150ms) das salas Battle/PvP

            await _db.PingAsync();
            await _db.EnsureSchemaAsync();     // migra inventário canônico + provisiona economia/config
            PuConfig = await _db.LoadPuConfigAsync();
            Log.Ok("shop", "pu_config: preço={0}/{1} bônus={2} {3}d  xp×{4} gold×{5}{6}",
                PuConfig.Price, PuConfig.RenewalPrice, PuConfig.BonusPoints,
                PuConfig.DurationDays, PuConfig.ExpMult, PuConfig.GoldMult,
                PuConfig.PromoActive ? " (promo ON)" : "");
            EnchantConfig = await _db.LoadEnchantConfigAsync();
            Log.Ok("enchant", "config: {0} catalisador(es)  evento×{1} PU×{2}",
                EnchantConfig.CatalyzerCount, EnchantConfig.EventMult, EnchantConfig.PuMult);
            await LoadItemDefsCacheAsync();   // catalogo de itens (iteminfo) p/ a compra 0x2e
            _levelCurve = await _db.LoadLevelCurveAsync(); // curva de exp por classe (level-up 0x50)
            Log.Ok("level", "curva de level carregada: {0} entradas (classlevelinfo)", _levelCurve.Count);
            _cellLevelCurve = await _db.LoadCellLevelCurveAsync();
            _cellLevel99Cap = CellLevelExp(0, 99) ?? 0;
            Log.Ok("level", "curva de cell carregada: {0} entradas (npcinfo), teto99={1}",
                _cellLevelCurve.Count, _cellLevel99Cap);
            IReadOnlyList<StageContentDefinition> stageContent = StageContentLoader.LoadEmbedded();
            _stageCatalog = new Domain.StageCatalog(
                await _db.LoadStageCatalogAsync(), stageContent);
            int inconsistentThresholds = stageContent.Count(
                stage => !stage.RankThresholdsConsistent);
            int inconsistentFlows = stageContent.Count(
                stage => !stage.FlowReferencesConsistent);
            int duplicateFlowNames = stageContent.Count(
                stage => !stage.FlowNamesUnique);
            Log.Ok("stage", "catálogo carregado: {0} stages (stageinfo + LevelData v258)",
                _stageCatalog.Count);
            if (inconsistentThresholds > 0)
                Log.Warn("stage", "{0} stages possuem thresholds de rank inconsistentes; " +
                    "cálculo autoritativo de rank permanece desativado neles", inconsistentThresholds);
            if (inconsistentFlows > 0)
                Log.Warn("stage", "{0} stages possuem referências de fluxo sem destino nos " +
                    "assets v258; o cliente deixa o bloco afetado parcialmente inicializado",
                    inconsistentFlows);
            if (duplicateFlowNames > 0)
                Log.Warn("stage", "{0} stages possuem nomes de fluxo duplicados nos assets " +
                    "v258; o cliente resolve para a primeira declaração", duplicateFlowNames);
            if (_stageCatalog.Count != 48)
                Log.Warn("stage", "catálogo incompleto: esperado=48 atual={0}", _stageCatalog.Count);
            _ = Task.Run(() => ConfigReloadLoopAsync(_cts.Token));   // reload a quente de pu_config/enchant_* (admin sem restart)
            _ = Task.Run(() => InventoryExpirationLoopAsync(_cts.Token));
            _ = Task.Run(() => PowerUserExpirationLoopAsync(_cts.Token));
            PublishServerStatus(true);
            _ = Task.Run(() => ServerStatusLoopAsync(_cts.Token));
            Log.Ok("world", "World Server pronto (ServerId={0})", _cfg.ServerId);
        }

        private async Task ServerStatusLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken); }
                catch (OperationCanceledException) { break; }
                PublishServerStatus(true);
            }
        }

        private void PublishServerStatus(bool online)
        {
            try
            {
                ServerStatusSnapshotStore.Write(new ServerStatusSnapshot(
                    online, online ? CurrentUsers : 0, MaxUser, DateTimeOffset.UtcNow));
                string[] accounts = online
                    ? _sessions.Values
                        .Where(session => session.Authenticated)
                        .Select(session => ActiveAccountSnapshotStore.Hash(session.UserId))
                        .Distinct(StringComparer.Ordinal)
                        .ToArray()
                    : Array.Empty<string>();
                ActiveAccountSnapshotStore.Write(new ActiveAccountSnapshot(
                    online, accounts, DateTimeOffset.UtcNow));
            }
            catch (Exception ex)
            {
                Log.Warn("world", "falha ao publicar status: {0}", ex.Message);
            }
        }

        /// <summary>Reload a quente das configs editáveis pelo painel admin (pu_config + enchant_*): relê a cada 15s
        /// e troca a referência. Cada config é imutável após o load, então a troca é atômica e segura p/ os leitores
        /// (roll do refino, bônus de PU) sem lock. Deixa ligar/desligar evento SEM reiniciar o World; um load vazio
        /// (hiccup do DB) é ignorado p/ não derrubar um evento ativo.</summary>
        private async Task ConfigReloadLoopAsync(System.Threading.CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try { await Task.Delay(TimeSpan.FromSeconds(15), ct); }
                catch (OperationCanceledException) { break; }
                try
                {
                    var ench = await _db.LoadEnchantConfigAsync();
                    if (ench.CatalyzerCount == 0) continue;   // DB indisponível -> mantém a config atual
                    bool changed = ench.EventMult != EnchantConfig.EventMult || ench.PuMult != EnchantConfig.PuMult;
                    EnchantConfig = ench;
                    PuConfig = await _db.LoadPuConfigAsync();   // DB confirmado OK pelo load acima
                    if (changed) Log.Ok("enchant", "config recarregada: evento×{0} PU×{1}", ench.EventMult, ench.PuMult);
                }
                catch (Exception ex) { Log.Warn("db", "reload de config: {0}", ex.Message); }
            }
        }

        /// <summary>Acesso ao DB para os handlers (compra 0x2e).</summary>
        public WorldDatabase Db => _db;

        private System.Collections.Generic.Dictionary<(byte Cls, byte Level), int> _levelCurve = new();

        /// <summary>Exp TOTAL p/ avancar do nivel atual (classlevelinfo). 0 = sem proximo nivel.</summary>
        public int NextLevelExp(byte cls, byte level) => _levelCurve.TryGetValue((cls, level), out var e) ? e : 0;

        /// <summary>Sobe níveis pendentes pela curva durante o load do personagem.</summary>
        public int SettleLevels(ClientSession s)
        {
            if (s.ActiveCharId <= 0) return 0;
            var before = new CharacterProgressionState(
                s.CharExp, s.CharLevel, (byte)Math.Min(s.CharLevelPoint, byte.MaxValue));
            CharacterProgressionState after = CharacterProgression.Project(
                before, 0, level => NextLevelExp(s.CharClass, level));
            int ups = after.Level - before.Level;
            if (ups > 0)
            {
                s.CharLevel = after.Level;
                s.CharLevelPoint = after.LevelPoint;
                _ = _db.UpdateCharacterLevelAsync(s.ActiveCharId, after.Level, after.LevelPoint);
                Log.Ok("level", "[{0}] char {1} LEVEL UP -> {2} (+{3} nivel(is), exp total {4})",
                    s.Slot, s.ActiveCharId, after.Level, ups, s.CharExp);
            }
            return ups;
        }

        private System.Collections.Generic.Dictionary<int, Database.ItemDef> _itemDefs = new();
        /// <summary>Catalogo de itens (iteminfo) carregado no boot. Preco Gold/Cash por itemId.</summary>
        public Database.ItemDef? FindItemDef(int itemId) => _itemDefs.TryGetValue(itemId, out var d) ? d : null;

        /// <summary>
        /// O item pode ser DESENHADO no grid do armazem (box)? Só "gear" = tipos 0-5 (slots de equipamento,
        /// Class bitmask 1-16, ids 1xxx-5xxx). Tipos 6-14 (transform=8, lotto=11, especial=13, etc., todos
        /// Class 31) NÃO têm ícone de box no cliente GG-removido -> renderizam invisíveis e crasham o painel
        /// ao reconstruir (botão "Previous"). Esses ainda são comprados/persistidos no itembox, só não pintam
        /// no grid. Catálogo do cliente: o box-visual (FUN_004774e0) só trata especial o tipo 0x0c, e o gear
        /// tipo 0 foi confirmado em jogo; tipos 8/13 foram confirmados invisíveis+crash.
        /// </summary>
        public bool IsBoxDisplayable(int itemId)
        {
            var d = FindItemDef(itemId);
            if (d == null) return false;
            // Gear (0-5) + materiais/consumiveis/cash (6,7,9-14, ex: Mithril 13001 type 13) têm ícone de
            // box e pintam normalmente. O crash do painel no Previous que motivava o filtro antigo (só
            // type<=5) JÁ foi resolvido (acks 0x2c/0x2d fiéis ao original). Só o type 8 (transform) fica
            // fora — não tem ícone de box no cliente GG-removido (renderiza invisível).
            return d.Type != 8;
        }

        /// <summary>Item é um SET (type 10) — um BUNDLE de peças, não uma peça equipável direta.</summary>
        public bool IsSet(int itemId) => FindItemDef(itemId)?.Type == 10;

        /// <summary>Composição de um SET (type 10): as colunas hit1-4/chit/ap do iteminfo guardam os itemIds
        /// dos membros (1 por slot de gear, faixa 0-5) — confirmado: 9012 -> 1009/1109/1209/1309/1409/1509.
        /// Fonte ÚNICA da composição. Só retorna membros que são itens válidos do catálogo (um valor que não
        /// resolve em item é stat, não membro -> filtrado). Vazio se não for set ou sem membros válidos.</summary>
        public System.Collections.Generic.IReadOnlyList<int> ExpandSetMembers(int setItemId)
        {
            var d = FindItemDef(setItemId);
            if (d == null || d.Type != 10) return System.Array.Empty<int>();
            var members = new System.Collections.Generic.List<int>(6);
            foreach (var m in new[] { d.Hit1, d.Hit2, d.Hit3, d.Hit4, d.CHit, d.Ap })
                if (m > 0 && FindItemDef(m) != null) members.Add(m);
            return members;
        }

        /// <summary>Carrega o catalogo de itens uma vez (iteminfo).</summary>
        public async Task LoadItemDefsCacheAsync()
        {
            var list = await _db.LoadItemDefsAsync();
            var map = new System.Collections.Generic.Dictionary<int, Database.ItemDef>(list.Count);
            foreach (var d in list) map[d.Id] = d;
            _itemDefs = map;
            Log.Ok("shop", "catalogo de itens carregado: {0} definicoes", map.Count);
        }

        public void Stop()
        {
            try { _cts?.Cancel(); } catch { }
            _broker?.Stop();
            try { _listener?.Close(); } catch { }
            _udpGame?.Stop();
            PublishServerStatus(false);
        }


        private async Task AcceptLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested && _listener != null)
            {
                Socket sock;
                try { sock = await _listener.AcceptAsync(ct); }
                catch (OperationCanceledException) { break; }
                catch (ObjectDisposedException) { break; }
                catch (Exception ex) { Log.Error("world", "accept: {0}", ex.Message); continue; }

                if (!TryAllocateSlot(out ushort slot))
                {
                    Log.Warn("world", "sem slots livres (max {0}) — recusando {1}", MaxUser,
                        (sock.RemoteEndPoint as IPEndPoint)?.Address?.ToString() ?? "?");
                    try { sock.Close(); } catch { }
                    continue;
                }

                var session = new ClientSession(sock, slot, this);
                _sessions[slot] = session;
                session.Start();
            }
        }

        private bool TryAllocateSlot(out ushort slot)
        {
            for (int i = 0; i < MaxUser; i++)
            {
                ushort cand = (ushort)((_nextSlotHint + i) % MaxUser);
                if (!_sessions.ContainsKey(cand))
                {
                    _nextSlotHint = (cand + 1) % MaxUser;
                    slot = cand;
                    return true;
                }
            }
            slot = 0;
            return false;
        }

        /// <summary>
        /// Sucesso do login (FUN_0041f6c0): promove a sessao e envia o LoginComplete
        /// imediatamente (o handler original responde ali mesmo). A carga do jogo e o
        /// log de conexao no DB rodam em background para nao atrasar a resposta.
        /// </summary>
        public async Task OnLoginSuccessAsync(
            ClientSession s, string account, string password, ushort tail)
        {
            if (_cfg.AuthType == 0)
            {
                WorldDatabase.Account? identity = await _db.AuthenticateCredentialAsync(
                    account, password, _cfg.AllowPasswordLogin, _cfg.RequiredClientBuild);
                if (identity == null)
                {
                    Log.Warn("auth", "[{0}] credencial recusada para '{1}'", s.Slot, account);
                    s.SendLoginError(Protocol.LoginError.SubInvalidCredential);
                    return;
                }
                s.Authority = identity.Authority;
                s.Country = identity.Country;
                s.SubStatus = identity.Authority > 0
                    ? Domain.UserSubStatus.Gm
                    : Domain.UserSubStatus.Normal;
            }
            lock (_sessions)
            {
                bool accountInUse = _sessions.Values.Any(other =>
                    other != s && other.Authenticated &&
                    string.Equals(other.UserId, account, StringComparison.OrdinalIgnoreCase));
                if (accountInUse)
                {
                    Log.Warn("auth", "[{0}] conta '{1}' já conectada", s.Slot, account);
                    s.SendLoginError(Protocol.LoginError.SubAccountInUse);
                    return;
                }
                s.SlotActive = true;
                s.Authenticated = true;
                s.UserId = account;
                s.Status = Domain.UserStatus.LoggedIn;
                s.CharName = account;
                s.GroupId = Groups.Count > 0 ? Groups[0].Id : 0;
                Interlocked.Increment(ref _currentUsers);
            }
            PublishServerStatus(true);

            // Carrega gold/cash/level/itens do DB ANTES do 0x0C: a síntese do 0x0C serializa gold/cash do
            // estado vivo (o display reflete a compra). Sincrono p/ garantir s.Gold/s.Cash setados no 0x0C.
            await LoadAndLogAsync(s, s.UserId);
            s.SendLoginResponse();   // 0x0C + 0x0D + 0x10; o challenge não depende do handshake UDP
        }

        private async Task LoadAndLogAsync(ClientSession s, string userId)
        {
            var gi = await _db.LoadGameInfoAsync(userId);
            if (gi == null)
            {
                Log.Warn("login", "[{0}] '{1}' logado mas sem usergameinfo (DB indisponivel?)", s.Slot, userId);
                return;
            }
            s.Game = new WorldDatabaseInfo
            {
                UserId = gi.Id,
                Name = gi.Name,
                CharName = gi.CharName,
                Gold = gi.Gold,
                Bag = gi.Bag,
                CharacterSlots = gi.CharacterSlots
            };
            s.GameInfoId = gi.Id;                                       // usergameinfo.id (debito gold + useriteminfo.userid)
            s.BuddyName = gi.BuddyName;
            s.TutorialClear = gi.TutorialClear;
            s.Gold = (uint)(gi.Gold < 0 ? 0 : gi.Gold);
            int cash = await _db.GetCashAsync(userId);                  // cash keyed por account-name
            s.Cash = (uint)(cash < 0 ? 0 : cash);
            if (_cfg.Chat.Enabled)
            {
                ChatPersistenceState chatState = await _db.LoadChatStateAsync(userId);
                s.ChatState.Load(chatState.MutedUntilUtc, chatState.BlockedAccounts);
            }
            s.BagCount = gi.Bag;
            s.CharacterSlotCount = gi.CharacterSlots;
            s.StageLevelFreeMarker = gi.StageLevelFreeMarker;
            s.ServerTimeMarker = gi.CurrentMinuteMarker;
            s.PowerTimeMarker = gi.PowerTimeMarker;
            s.ClanId = gi.ClanId;
            s.PowerLevelPoint = (uint)(gi.PowerLevelPoint < 0 ? 0 : gi.PowerLevelPoint); // PU Bonus Points -> 0x0C @48
            s.PuActive = gi.PuActive;                                   // powertimedate > now -> bônus de XP/gold
            s.ExpBonusActive = gi.PuActive;                            // flag original do bônus de XP (user+0x236c)
            s.PuExpiresAt = gi.PuExpiresAt;
            int expiredItems = await _db.PurgeExpiredInventoryAsync(gi.Id);
            if (expiredItems < 0)
                Log.Warn("login", "[{0}] limpeza de itens expirados falhou; loads manterão o filtro", s.Slot);
            if (gi.PuActive) Log.Info("shop", "[{0}] PU ATIVO -> bônus xp×{1} gold×{2}", s.Slot,
                PuConfig.EffectiveExpMult(DateTime.Now), PuConfig.EffectiveGoldMult(DateTime.Now));
            var ch = await _db.LoadActiveCharacterAsync(gi.Id);
            if (ch != null)
            {
                // O original mantém user+0x14A4 zerado até o request 0x14. O personagem
                // marcado como used serve apenas para montar o preview inicial do 0x0C.
                s.PreviewCharId = ch.Id;
                s.CharClass = ch.Class;                                 // classe -> curva de level (0x50)
                s.CharExp = ch.Exp < 0 ? 0 : ch.Exp;                    // exp acumulado (level-up server-side)
                s.CharLevel = ch.Level == 0 ? (byte)1 : ch.Level;       // overlay 0x0C @96 (nivel na tela)
                s.CharWin = (uint)(ch.Win < 0 ? 0 : ch.Win);            // overlay 0x0C @73
                s.CharLose = (uint)(ch.Lose < 0 ? 0 : ch.Lose);         // overlay 0x0C @77
                s.CharDraw = (uint)(ch.Draw < 0 ? 0 : ch.Draw);         // overlay 0x0C @81
                s.CharLevelPoint = (uint)(ch.LevelPoint);               // pontos de level -> overlay 0x0C @101
                s.PotionSlotCount = ch.PotionSlots;
                SettleLevels(s);                                        // upa niveis pendentes JÁ no load (barra cheia do relog)
                // stats alocados (hit1..maxcp) -> Stats[0..9], p/ a alocacao 0x33 partir do valor real salvo
                s.Stats[0] = ch.Hit1; s.Stats[1] = ch.Hit2; s.Stats[2] = ch.Hit3; s.Stats[3] = ch.Hit4;
                s.Stats[4] = ch.Chit; s.Stats[5] = ch.Hp; s.Stats[6] = ch.Ap; s.Stats[7] = ch.AttackSpeed;
                s.Stats[8] = ch.Speed; s.Stats[9] = ch.Maxcp;
                if (s.CharName.Length == 0) s.CharName = ch.Name;
                InventoryHydration inventory = await HydrateInventoryAsync(s, ch, "login");
                s.StageRanks = await _db.LoadStageRanksAsync(ch.Id);       // ranks de stage -> overlay 0x0C@333 (RANK X CLEAR na seleção)
                Log.Ok("login", "[{0}] char ativo='{1}' id={2} class={3} lvl={4} itens={5} box={6}{7}",
                    s.Slot, ch.Name, ch.Id, ch.Class, ch.Level, inventory.ActiveItems,
                    inventory.VisibleStorageItems,
                    inventory.HiddenStorageItems > 0
                        ? $" (+{inventory.HiddenStorageItems} não-gear ocultos)"
                        : "");
            }
            else { Log.Warn("login", "[{0}] '{1}' sem char ativo (characterinfo.used=1 ausente)", s.Slot, userId); }
            await RefreshCharacterSelectIdentityAsync(s);
            s.ConnectionLogId = await _db.LogUserConnectAsync(new ConnectionLogStart(
                gi.Id, userId, _cfg.ServerId, s.RemoteIp, s.Country));
            Log.Ok("login", "[{0}] '{1}' logado (char='{2}', gold={3}, cash={4}) — {5}/{6} online",
                s.Slot, userId, gi.CharName, s.Gold, s.Cash, CurrentUsers, MaxUser);
        }

        /// <summary>Monta a lista de chars do char-select (0x0C) a partir do DB — síntese de raiz, sem replay.</summary>
        private async Task<CharList> BuildLoginCharListAsync(
            ClientSession s, IReadOnlyList<CharacterInfo>? loadedCharacters = null)
        {
            IReadOnlyList<CharacterInfo> chars = loadedCharacters ??
                await _db.LoadCharactersAsync(s.GameInfoId);
            int previewCharacterId = s.ActiveCharId > 0 ? s.ActiveCharId : s.PreviewCharId;
            var quickslot = previewCharacterId > 0
                ? await _db.LoadQuickslotAsync(s.GameInfoId, previewCharacterId)
                : new List<(int Cell, int ItemId, int Count)>();
            var summaries = new List<CharSummary>(chars.Count);
            foreach (var ch in chars)
            {
                var ranks = await _db.LoadStageRanksAsync(ch.Id);
                var items = await _db.LoadItemsAsync(ch.Id);
                summaries.Add(BuildCharSummary(
                    ch, items, ranks, ch.Id == previewCharacterId ? quickslot : null));
            }
            return new CharList
            {
                DisplayName = string.IsNullOrEmpty(s.BuddyName) ? s.CharName : s.BuddyName,
                Clan = _cfg.Clan.Enabled
                    ? await _db.LoadClanLoginSnapshotAsync(s.GameInfoId, s.ClanId)
                    : Domain.ClanLoginSnapshot.Empty,
                NetworkSlot = s.Slot,
                Country = checked((ushort)Math.Clamp(s.Country, 0, ushort.MaxValue)),
                SlotCount = s.CharacterSlotCount,
                Gold = s.Gold,
                Cash = s.Cash,
                ServerTimeMarker = s.ServerTimeMarker,
                PowerTimeMarker = s.PowerTimeMarker,
                PowerLevelPoint = (ushort)Math.Min(s.PowerLevelPoint, (uint)ushort.MaxValue),
                Chars = summaries,
            };
        }

        private async Task RefreshCharacterSelectIdentityAsync(ClientSession session)
        {
            IReadOnlyList<CharacterInfo> characters =
                await _db.LoadCharactersAsync(session.GameInfoId);
            CharacterInfo? first = characters.Count > 0 ? characters[0] : null;
            if (first != null && !string.Equals(
                    session.BuddyName, first.Name, StringComparison.Ordinal))
            {
                Database.BuddyNameChangeResult result =
                    await ChangeBuddyNameAsync(session, first.Name);
                if (result != Database.BuddyNameChangeResult.Success)
                    Log.Warn("character", "[{0}] não sincronizou buddyname com primeiro char='{1}': {2}",
                        session.Slot, first.Name, result);
            }
            session.LoginCharList = await BuildLoginCharListAsync(session, characters);
        }

        private CharSummary BuildCharSummary(CharacterInfo ch, IReadOnlyList<UserItem> items, byte[] ranks,
            List<(int Cell, int ItemId, int Count)>? quickslot)
        {
            CharacterPreview preview = CharacterPreviewProjection.Build(ch, items, FindItemDef);
            var qs = new ushort[6];
            if (quickslot != null)
                foreach (var (cell, itemId, _) in quickslot)
                    if (cell <= 18 && InventoryEntitlementRules.IsPotionCellUnlocked(cell, ch.PotionSlots))
                        qs[cell - 13] = (ushort)itemId;
            return new CharSummary
            {
                CharacterId = ch.Id,
                Name = ch.Name,
                Slot = ch.Slot,
                Auth = ch.Auth,
                RankGrade = ch.RankGrade,
                TotalRank = ch.TotalRank,
                ClassRank = ch.ClassRank,
                Class = ch.Class,
                Level = ch.Level == 0 ? (byte)1 : ch.Level,
                Exp = (uint)Math.Max(0, ch.Exp),
                LevelPoint = ch.LevelPoint,
                Win = (uint)Math.Max(0, ch.Win),
                Lose = (uint)Math.Max(0, ch.Lose),
                Draw = (uint)Math.Max(0, ch.Draw),
                Stats = new ushort[] { ch.Hit1, ch.Hit2, ch.Hit3, ch.Hit4, ch.Chit, ch.Hp, ch.Ap, ch.AttackSpeed, ch.Speed, ch.Maxcp },
                Equip = preview.Equipment,
                Enhance = preview.Enhancement,
                Quickslot = qs,
                StageRanks = ranks ?? System.Array.Empty<byte>(),
            };
        }

        private bool IsActiveItemValid(CharacterInfo character, UserItem item)
        {
            ItemDef? definition = FindItemDef(item.ItemId);
            if (definition == null ||
                !EquipmentRules.CanPlace(definition, item.Slot, character.Class, character.Level))
                return false;
            return item.Slot < 13 ||
                   InventoryEntitlementRules.IsPotionCellUnlocked(item.Slot, character.PotionSlots);
        }

        public async Task<CharacterSelectStatus> SelectCharacterAsync(ClientSession s, int characterId)
        {
            if (!CharacterLifecycleRules.CanSelect(
                    s.GameInfoId, s.ActiveCharId, characterId)) return CharacterSelectStatus.SystemError;
            var gate = _characterLocks.GetOrAdd(s.GameInfoId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                if (s.ActiveCharId != 0) return CharacterSelectStatus.SystemError;
                CharacterSelectResult result = await _db.SelectCharacterAsync(s.GameInfoId, characterId);
                if (result.Status != CharacterSelectStatus.Success) return result.Status;
                CharacterInfo ch = result.Character!;

                s.ActiveCharId = ch.Id;
                s.PreviewCharId = ch.Id;
                s.CharName = ch.Name;
                s.CharClass = ch.Class;
                s.CharLevel = ch.Level == 0 ? (byte)1 : ch.Level;
                s.CharExp = Math.Max(0, ch.Exp);
                s.CharWin = (uint)Math.Max(0, ch.Win);
                s.CharLose = (uint)Math.Max(0, ch.Lose);
                s.CharDraw = (uint)Math.Max(0, ch.Draw);
                s.CharLevelPoint = ch.LevelPoint;
                s.PotionSlotCount = ch.PotionSlots;
                s.Stats[0] = ch.Hit1; s.Stats[1] = ch.Hit2; s.Stats[2] = ch.Hit3; s.Stats[3] = ch.Hit4;
                s.Stats[4] = ch.Chit; s.Stats[5] = ch.Hp; s.Stats[6] = ch.Ap; s.Stats[7] = ch.AttackSpeed;
                s.Stats[8] = ch.Speed; s.Stats[9] = ch.Maxcp;
                InventoryHydration inventory = await HydrateInventoryAsync(s, ch, "character");
                s.StageRanks = await _db.LoadStageRanksAsync(ch.Id);
                await RefreshCharacterSelectIdentityAsync(s);
                Log.Ok("character", "[{0}] selecionou char '{1}' id={2} class={3} lvl={4} itens={5} box={6}",
                    s.Slot, ch.Name, ch.Id, ch.Class, ch.Level,
                    inventory.ActiveItems,
                    inventory.VisibleStorageItems + inventory.HiddenStorageItems);
                return CharacterSelectStatus.Success;
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task<Database.CharacterDeleteResult> DeleteCharacterAsync(
            ClientSession s, int characterId, string deleteKey)
        {
            if (s.GameInfoId <= 0 || characterId <= 0) return Database.CharacterDeleteResult.NotFound;
            var gate = _characterLocks.GetOrAdd(s.GameInfoId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                var outcome = await _db.DeleteCharacterAsync(s.GameInfoId, characterId, deleteKey);
                var result = outcome.Result;
                if (result == Database.CharacterDeleteResult.DeleteKeySent)
                {
                    return await CharacterDeleteKeyDelivery.CompleteAsync(
                        outcome, _characterDeleteNotifier,
                        key => _db.RevokeCharacterDeleteKeyAsync(
                            s.GameInfoId, characterId, key));
                }
                if (result != Database.CharacterDeleteResult.Success) return result;
                await RefreshCharacterSelectIdentityAsync(s);
                Log.Ok("character", "[{0}] excluiu char id={1}", s.Slot, characterId);
                return result;
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task<Database.BuddyNameChangeResult> ChangeBuddyNameAsync(ClientSession s, string buddyName)
        {
            if (!Domain.LegacyIdentity.IsValidBuddyName(buddyName))
                return Database.BuddyNameChangeResult.Failed;
            await _buddyIdentityLock.WaitAsync();
            try
            {
                var result = await _db.ChangeBuddyNameAsync(s.GameInfoId, buddyName);
                if (result == Database.BuddyNameChangeResult.Success)
                {
                    s.BuddyName = buddyName;
                    Log.Ok("character", "[{0}] buddyname alterado para '{1}'", s.Slot, buddyName);
                }
                return result;
            }
            finally
            {
                _buddyIdentityLock.Release();
            }
        }

        public async Task MarkTutorialClearAsync(ClientSession s)
        {
            if (s.GameInfoId <= 0 || s.TutorialClear) return;
            var gate = _characterLocks.GetOrAdd(s.GameInfoId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                if (s.TutorialClear) return;
                if (!await _db.MarkTutorialClearAsync(s.GameInfoId)) return;
                s.TutorialClear = true;
                Log.Ok("character", "[{0}] tutorial concluído", s.Slot);
            }
            finally
            {
                gate.Release();
            }
        }

        public async Task RemoveSessionAsync(ClientSession s)
        {
            if (_sessions.TryRemove(s.Slot, out _))
            {
                LeaveField(s);
                LeaveChannel(s, true);
                if (s.Authenticated)
                    Interlocked.Decrement(ref _currentUsers);
                PublishServerStatus(true);
                await _db.CloseConnectionLogAsync(s.ConnectionLogId, s.DisconnectReason);
                Log.Info("world", "[{0}] sessao encerrada ('{1}') — {2}/{3} online",
                    s.Slot, s.UserId, CurrentUsers, MaxUser);
            }
        }

        // ---- broker -----------------------------------------------------------

        private BrokerLink.Stats GetStats()
            => new BrokerLink.Stats(MaxUser, CurrentUsers, _cfg.MaxField, 0);

    }
}
