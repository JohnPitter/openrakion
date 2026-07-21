using System;
using RakionServer.Common;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    public sealed partial class ClientSession
    {
        internal void BeginFieldList(byte[] data) => HandleRoomList(data);

        internal void BeginFieldEnter(byte[] data) => HandleRoomJoin(data);

        internal void BeginFieldQuickEnter() => HandleRoomQuickJoin();

        internal void BeginFieldCreate(byte[] data)
        {
            System.Threading.Interlocked.Exchange(ref _gameClockStarted, 0);
            HandleRoomCreate(data);
        }

        private void HandleRoomList(byte[] data)
        {
            var reader = new PacketReader(data);
            if (!reader.CanRead(10)) { Disconnect(0x48); return; }
            byte maxCount = reader.Byte();
            ushort cursor = reader.UInt16();
            if (maxCount > 10) { Disconnect(0x48); return; }
            bool forward = reader.Byte() != 0;
            byte modeMask = 0;
            for (byte mode = 0; mode < 5; mode++)
                if (reader.Byte() != 0) modeMask |= (byte)(1 << mode);
            bool bypassEligibility = reader.Byte() != 0;
            var query = new RoomListQuery(maxCount, cursor, forward, modeMask, bypassEligibility);
            var fields = _server.ListJoinableFields(this, query);
            SendEncryptedFrame(LobbyFrames.GameList(fields));
            Log.Info("room", "[{0}] lista cursor={1} dir={2} max={3} modos=0x{4:x2} -> {5} sala(s)",
                Slot, cursor, forward ? "next" : "prev", maxCount, modeMask, fields.Length);
        }

        private void HandleRoomCreate(byte[] data)
        {
            if (!TryParseRoomCreation(data, out RoomCreationOptions options) ||
                !ValidateRoomCreation(options)) return;
            RoomCreationOptions runtimeOptions = NormalizeRuntimeRoomCreation(options);
            ApplyPendingRoomCreation(runtimeOptions);
            if (runtimeOptions.Mode == 0) HandleSoloRoomCreate(runtimeOptions);
            else HandleCompetitiveRoomCreate(runtimeOptions);
        }

        private bool TryParseRoomCreation(byte[] data, out RoomCreationOptions options)
        {
            options = null!;
            var reader = new PacketReader(data);
            if (!reader.TryCString(0x28, out string name)) { Disconnect(0x54); return false; }
            if (!reader.TryCString(8, out string password)) { Disconnect(0x55); return false; }
            if (!reader.TryCString(0xc8, out string description) || !reader.CanRead(9))
            {
                Disconnect(0x56);
                return false;
            }
            options = new RoomCreationOptions
            {
                Name = name,
                Password = password,
                Description = description,
                MapId = reader.Byte(),
                Mode = reader.Byte(),
                Rounds = reader.Byte(),
                DurationSeconds = reader.UInt16(),
                FragLimit = reader.Byte(),
                MinLevel = reader.Byte(),
                MaxLevel = reader.Byte(),
                LevelRangeCode = reader.Byte()
            };
            return true;
        }

        private void HandleSoloRoomCreate(RoomCreationOptions options)
        {
            Field field;
            try
            {
                field = _server.CreateField(options with { Searchable = false }, this);
            }
            catch (InvalidOperationException ex)
            {
                Log.Warn("room", "[{0}] falhou ao preparar stage solo: {1}", Slot, ex.Message);
                SendEncryptedFrame(LobbyFrames.RoomCreateAck(0, 1));
                return;
            }
            SendEncryptedFrame(LobbyFrames.RoomCreateAck((ushort)field.Id, 0));
            Log.Ok("room", "[{0}] criou stage solo field={1} stage={2}",
                Slot, field.Id, options.MapId);
        }

        private void HandleCompetitiveRoomCreate(RoomCreationOptions options)
        {
            Field field;
            try
            {
                field = _server.CreateField(options, this);
            }
            catch (InvalidOperationException ex)
            {
                Log.Warn("room", "[{0}] falhou ao criar sala: {1}", Slot, ex.Message);
                SendEncryptedFrame(LobbyFrames.RoomCreateAck(0, 1));
                return;
            }
            SendEncryptedFrame(LobbyFrames.RoomCreateAck((ushort)field.Id, 0));
            Log.Ok("room", "[{0}] criou sala {1} '{2}' mode={3} map={4} cap={5}",
                Slot, field.Id, field.Name, field.Mode, field.MapId, field.MaxPlayers);
        }

        private bool ValidateRoomCreation(RoomCreationOptions options)
        {
            if (options.Mode == 0)
            {
                if (options.MapId >= 100) { Disconnect(0x57); return false; }
                return true;
            }
            if (options.Mode > 4) { Disconnect(0x5b); return false; }
            if (options.Rounds >= 0x16) { Disconnect(0xca); return false; }
            if (options.DurationSeconds is < 0x122 or > 0x4ba) { Disconnect(0xcb); return false; }
            if ((options.Mode == 2 && options.FragLimit is < 0x0d or > 0x1e) ||
                (options.Mode == 3 && options.FragLimit is <= 0x13 or >= 0x33))
            {
                Disconnect(0xcc);
                return false;
            }
            if (options.MinLevel == 0 || options.MaxLevel > 99) { Disconnect(0x59); return false; }
            if (CharLevel < options.MinLevel || CharLevel > options.MaxLevel)
            {
                Disconnect(0x5a);
                return false;
            }
            return true;
        }

        private void ApplyPendingRoomCreation(RoomCreationOptions options)
        {
            PendingRoomName = options.Name;
            PendingRoomMap = options.MapId;
            PendingRoomMode = options.Mode;
            PendingRoomRounds = options.Rounds;
            PendingRoomDurationSec = options.DurationSeconds;
        }

        private static RoomCreationOptions NormalizeRuntimeRoomCreation(RoomCreationOptions options)
        {
            if (options.Mode != 0) return options;
            return options with
            {
                Rounds = options.Rounds is >= 1 and < 0x16 ? options.Rounds : (byte)1,
                DurationSeconds = options.DurationSeconds is >= 30 and <= 3600
                    ? options.DurationSeconds
                    : (ushort)432
            };
        }

        private void HandleRoomJoin(byte[] data)
        {
            var reader = new PacketReader(data);
            if (!reader.CanRead(2)) { Disconnect(0x4c); return; }
            ushort fieldId = reader.UInt16();
            if (!reader.TryCString(8, out string password)) { Disconnect(0x4d); return; }
            var field = _server.GetField(fieldId);
            if (field == null) { SendEncryptedFrame(LobbyFrames.RoomJoinResult(1)); return; }
            RoomJoinStatus status = _server.TryJoinRoom(this, field, password);
            if (status != RoomJoinStatus.Success)
            {
                SendEncryptedFrame(LobbyFrames.RoomJoinResult((byte)status));
                return;
            }
            ApplyRoomConfiguration(field);
            SendRoomJoinDetail(field, password, field.Mode == 0);
        }

        private void HandleRoomQuickJoin()
        {
            if (!_server.TryQuickJoinField(this, out var field) || field == null)
            {
                SendEncryptedFrame(LobbyFrames.QuickJoinEmpty());
                return;
            }
            ApplyRoomConfiguration(field);
            SendRoomJoinDetail(field, string.Empty, false);
        }

        private void HandleRoomReady(byte[] data)
        {
            if (data.Length < 1) { Disconnect(0x4f); return; }
            var field = _server.GetField(FieldId);
            if (field == null || Status != UserStatus.FieldLobby) return;
            byte ready = data[0] == 0 ? (byte)0 : (byte)1;
            lock (field.SyncRoot)
            {
                var record = field.FindRec(this);
                if (record == null) return;
                if (ready == 0)
                {
                    if (record.State != 2) return;
                    record.State = 1;
                }
                else
                {
                    if (record.State != 1) return;
                    record.State = 2;
                }
                field.BroadcastField(0x3d, new[] { (byte)record.Slot, ready });
            }
            Log.Info("room", "[{0}] sala {1}: ready={2}", Slot, field.Id, ready);
        }

        private void HandleRoomChangeMaster()
        {
            var field = _server.GetField(FieldId);
            if (field == null) return;
            byte oldSeat;
            byte newSeat;
            lock (field.SyncRoot)
            {
                if (!field.TryRotateMaster(this, out oldSeat, out newSeat)) return;
                field.ResetLobbyReady();
                field.BroadcastField(0x3c, new[] { newSeat });
            }
            Log.Ok("room", "[{0}] sala {1}: host {2}->{3}", Slot, field.Id, oldSeat, newSeat);
        }

        private void HandleRoomChangeTeam()
        {
            var field = _server.GetField(FieldId);
            if (field == null) return;
            byte oldSeat;
            byte newSeat;
            lock (field.SyncRoot)
            {
                if (!field.TryChangeTeam(this, out oldSeat, out newSeat))
                {
                    SendMessage(0x3e, new byte[] { 2 });
                    return;
                }
                field.BroadcastField(0x3e, new byte[] { 0, oldSeat, newSeat });
            }
            Log.Ok("room", "[{0}] sala {1}: time/seat {2}->{3}", Slot, field.Id, oldSeat, newSeat);
        }

        private void HandleRoomClose()
        {
            var field = _server.GetField(FieldId);
            if (field == null || field.Master != this) return;
            if (!_server.TryCloseField(this, out var members)) return;
            foreach (var member in members) member.SendRoomListState();
        }

        private void HandleRoomKick(byte[] data)
        {
            if (data.Length < 1) { Disconnect(0x67); return; }
            byte targetSeat = data[0];
            var field = _server.GetField(FieldId);
            if (field == null) return;
            if (!_server.TryKickFieldMember(this, targetSeat, out var victim) || victim == null)
                return;
            victim.SendRoomListState();
            Log.Ok("room", "[{0}] sala {1}: expulsou seat {2} sessão {3}",
                Slot, field.Id, targetSeat, victim.Slot);
        }

        private void HandleRoomRule(byte[] data)
        {
            var field = _server.GetField(FieldId);
            if (field == null || field.Master != this) return;
            var reader = new PacketReader(data);
            string name = reader.CString(0x29);
            string password = reader.CString(9);
            string description = reader.CString(0xc9);
            if (!reader.CanRead(6) || string.IsNullOrWhiteSpace(name)) { Disconnect(0x6f); return; }
            byte map = reader.Byte();
            byte mode = reader.Byte();
            ushort duration = reader.UInt16();
            byte minLevel = reader.Byte();
            byte maxLevel = reader.Byte();
            if (mode > 4 || duration is < 30 or > 3600 || minLevel > maxLevel)
            {
                Disconnect(0x72);
                return;
            }
            ClientSession[] members;
            lock (field.SyncRoot)
            {
                field.Name = name;
                field.Password = password;
                field.Description = description;
                field.MapId = map;
                field.Mode = mode;
                field.RoundDurationSec = duration;
                field.MinLevel = minLevel;
                field.MaxLevel = maxLevel;
                field.ResetLobbyReady();
                field.BroadcastField(0x41, data);
                members = field.Players.ToArray();
            }
            foreach (var member in members) member.ApplyRoomConfiguration(field);
            Log.Ok("room", "[{0}] sala {1}: regra '{2}' map={3} mode={4} dur={5}",
                Slot, field.Id, field.Name, map, mode, duration);
        }

        private void HandleRoomSlotStatus(byte[] data)
        {
            if (data.Length < 2) { Disconnect(0x73); return; }
            byte seat = data[0];
            bool unlocked = data[1] != 0;
            var field = _server.GetField(FieldId);
            if (field == null) return;
            lock (field.SyncRoot)
            {
                if (!field.TrySetSlotLock(this, seat, !unlocked)) return;
                field.ResetLobbyReady();
                field.BroadcastField(0x42, new[] { seat, data[1] });
            }
            Log.Info("room", "[{0}] sala {1}: slot {2} {3}",
                Slot, field.Id, seat, unlocked ? "aberto" : "fechado");
        }

        internal void SendRoomListState()
        {
            InField = true;
            FieldSecondary = true;
            SecondActive = true;
            Status = UserStatus.FieldLobby;
            _server.SendChannelState(this, includeSelfPresence: true);
            SendEncryptedFrame(LobbyFrames.GameList(_server.ListJoinableFields(0, 10)));
        }

        private void HandleMatchStart()
        {
            var field = _server.GetField(FieldId);
            if (field == null || Status != UserStatus.FieldLobby)
            {
                SendEncryptedFrame(LobbyFrames.MatchStartAck(3));
                return;
            }
            lock (field.SyncRoot)
            {
                if (field.Master != this)
                {
                    SendEncryptedFrame(LobbyFrames.MatchStartAck(1));
                    return;
                }
                foreach (var record in field.Slots)
                {
                    if (!record.Occupied || record.Session == this) continue;
                    if (!record.LobbyReady)
                    {
                        SendEncryptedFrame(LobbyFrames.MatchStartAck(3));
                        return;
                    }
                }
                field.ArmMatch(Environment.TickCount64);
                foreach (var record in field.Slots)
                {
                    if (!record.Occupied || record.Session == null) continue;
                    record.Session.PrepareRoomMatch();
                }
                field.BroadcastLobby(LobbyFrames.MatchStartAck());
            }
            Log.Ok("room", "[{0}] iniciou sala {1} com {2} jogador(es)", Slot, field.Id, field.Count);
        }

        private void PrepareRoomMatch()
        {
            System.Threading.Interlocked.Exchange(ref _gameClockStarted, 0);
            Status = UserStatus.InField;
            ResetFieldPotionUsage();
        }

        private void ApplyRoomConfiguration(Field field)
        {
            PendingRoomName = field.Name;
            PendingRoomMap = field.MapId;
            PendingRoomMode = field.Mode;
            PendingRoomDurationSec = field.RoundDurationSec;
            PendingRoomRounds = field.MaxRounds;
        }

        private void SendRoomJoinDetail(Field field, string echoedValue, bool includeModeZeroAck)
        {
            if (includeModeZeroAck)
            {
                using var body = new PacketWriter();
                body.WriteUInt32((uint)GameInfoId)
                    .WriteWord(field.Id)
                    .WriteCString(echoedValue);
                SendMessage(0x26, body.ToArray());
            }

            lock (field.SyncRoot)
            {
                var record = field.FindRec(this);
                if (record == null) return;
                field.BroadcastLobby(RoomRosterFrames.PlayerJoined(record));
                SendMessage(0x37, RoomRosterFrames.SnapshotBody(field));
            }
            Log.Ok("room", "[{0}] entrou na sala {1} seat={2}", Slot, field.Id, FieldSeat);
        }
    }
}
