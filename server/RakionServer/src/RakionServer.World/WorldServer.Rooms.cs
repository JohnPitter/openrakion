using System;
using System.Collections.Generic;
using RakionServer.Common;
using RakionServer.World.Network;

namespace RakionServer.World
{
    /// <summary>
    /// Sistema de SALAS multiplayer (Parte 1c): registra as salas criadas (0x3b), publica na game list
    /// (0x36, formato do worldserv FUN_00405790) e trata o join (0x38, FUN_00423100). Domínio isolado —
    /// o handler de rede só traduz bytes↔chamada. Ver docs/known-issues.md #2/#5.
    /// </summary>
    public sealed partial class WorldServer
    {
        /// <summary>Registra a sala do host (do 0x3b) como Field DESCOBRÍVEL: reusa o field da sala e o decora com
        /// os metadados (mapSlot/senha/nível). IsRoom=true = publicável no 0x36.</summary>
        public Domain.Field? RegisterRoom(ClientSession host)
        {
            var f = GetOrCreateRoomField(host);
            if (f == null) return null;
            f.IsRoom = true;
            f.MapSlot = host.PendingRoomSlot;
            f.Password = host.PendingRoomPass ?? "";
            if (host.PendingRoomRounds != 0) f.MaxRounds = host.PendingRoomRounds;
            if (f.MinLevel == 0) f.MinLevel = 1;
            if (f.MaxLevel == 0) f.MaxLevel = 99;
            Log.Ok("room", "[{0}] sala '{1}' REGISTRADA na game list (idx={2} map={3} mode={4} slot=0x{5:x4})",
                host.Slot, f.Name, f.Id, f.MapId, f.Mode, f.MapSlot);
            return f;
        }

        /// <summary>Spawn MÚTUO de humanos no STAGE: cada humano já no stage recebe o 0x4b AddPlayer do seat do novo e
        /// vice-versa, p/ um ver o avatar do outro como REMOTO (<see cref="BotMovement.BuildStageAddPlayer"/>, byte0=seat
        /// 0-19). O movimento (0x30a) é relayado à parte (UdpGameplay). NOTA: o "modo observação" do 2º humano NÃO está
        /// neste 0x4b — o probe de sessão prova que o joiner registra o PRÓPRIO player local (AddPlayer slot=seat, call
        /// 0x361037e2) independentemente do eco, e ecoar o blob de spawn do cliente CRASHA o cliente (formato de
        /// recebimento [seat][blobLen][blob] ≠ o upload [0x41][00][blob]). Causa provável = o joiner não recebe o estado
        /// de mundo do host (golem/objetivo) pelo canal reliable P2P 0x0304. Ver docs/pvp-stage-re.md §10.</summary>
        public void SpawnStageForHumans(Domain.Field f, ClientSession joiner)
        {
            if (f == null || joiner.FieldSeat >= 0x14) return;
            ClientSession[] members;
            lock (f.Players) members = f.Players.ToArray();
            foreach (var other in members)
            {
                if (other == joiner || !other.Connected || other.Status != 3 || other.FieldSeat >= 0x14) continue;   // Status 3 = stage
                // relay do blob REAL de cada peer (posição/stats que ele subiu), não o spawn constante
                try { other.SendMessage(0x4b, Network.BotMovement.BuildStageAddPlayerFromUpload(joiner.FieldSeat, joiner.StageSpawnUpload)); } catch { }
                try { joiner.SendMessage(0x4b, Network.BotMovement.BuildStageAddPlayerFromUpload(other.FieldSeat, other.StageSpawnUpload)); } catch { }
                Log.Ok("field", "spawn mútuo no stage: seat {0} <-> {1} (field {2})", joiner.FieldSeat, other.FieldSeat, f.Id);
            }
        }

        /// <summary>Snapshot das salas publicáveis (IsRoom, host vivo) p/ o frame 0x36 — DTO de borda.</summary>
        public IReadOnlyList<RoomListEntry> SnapshotRooms()
        {
            var list = new List<RoomListEntry>();
            lock (Fields)
            {
                foreach (var f in Fields)
                {
                    if (!f.IsRoom || f.Master == null || !f.Master.Connected) continue;
                    var ep = f.Master.UdpEndpoint;
                    uint ip = ep != null ? BitConverter.ToUInt32(ep.Address.MapToIPv4().GetAddressBytes(), 0) : 0u;
                    ushort port = (ushort)(ep?.Port ?? 0);
                    list.Add(new RoomListEntry(
                        (ushort)f.Id, f.MapId, f.Mode, f.MinLevel, f.MaxLevel, f.MaxRounds,
                        (byte)Math.Min(f.Count, 255), f.MaxPlayers, ip, port, f.Name,
                        f.MapSlot, f.State == 2, !string.IsNullOrEmpty(f.Password)));
                }
            }
            return list;
        }

        /// <summary>Join 0x38: adiciona o <paramref name="joiner"/> à sala <paramref name="fieldIndex"/>, aloca
        /// assento e vincula estado (room lobby). Devolve o Field + assento; err != 0 = falha (0x4c=inexistente,
        /// 0x4b=cheia, 0x53=senha, 0x06=partida em andamento, 0x07=level fora da faixa — os dois últimos
        /// cravados do worldserv FUN_00406f40: al=6 se field+8==2; al=7 se FUN_00405920 reprova o level
        /// (faixa INCLUSIVE field+0x111..+0x112) — reply [0x38][err]).</summary>
        public Domain.Field? TryJoinRoom(ClientSession joiner, ushort fieldIndex, string pass, out int seat, out byte err)
        {
            seat = -1; err = 0;
            Domain.Field? f;
            lock (Fields) f = Fields.Find(x => x.Id == fieldIndex && x.IsRoom);
            if (f == null || f.Master == null) { err = 0x4c; return null; }
            if (!string.IsNullOrEmpty(f.Password) && f.Password != (pass ?? "")) { err = 0x53; return null; }
            if (joiner.CharLevel < f.MinLevel || joiner.CharLevel > f.MaxLevel) { err = 0x07; return null; }
            if (f.State == 2) { err = 0x06; return null; }   // partida em andamento
            seat = f.AssignSeatBalanced(joiner);   // 2º jogador cai no time vazio (1-1) -> Ready destravado
            if (seat < 0) { err = 0x4b; return null; }
            f.Add(joiner);
            joiner.FieldId = f.Id;
            joiner.FieldSeat = (byte)seat;
            joiner.FieldObjectIndex = (ushort)seat;
            joiner.InField = true;
            joiner.FieldSecondary = true;
            joiner.Status = Domain.UserStatus.FieldLobby;   // room lobby (Status=2)
            return f;
        }
    }

    /// <summary>DTO de borda de UMA entrada da game list (0x36). Serializado por <see cref="LobbyFrames.GameList"/>
    /// no layout do worldserv FUN_00405790.</summary>
    public readonly record struct RoomListEntry(
        ushort FieldIndex, byte Map, byte Mode, byte MinLevel, byte MaxLevel, byte MaxRounds,
        byte CurPlayers, byte MaxPlayers, uint HostIp, ushort HostPort, string Name,
        ushort MapSlot, bool Playing, bool HasPassword);
}
