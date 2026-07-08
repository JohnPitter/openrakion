using System;
using RakionServer.Common;
using RakionServer.World.Domain;
using RakionServer.World.Network;

namespace RakionServer.World
{
    /// <summary>
    /// Render do BOT no ROSTER da sala e no STAGE. Traduz estado do bot -> chamada aos frame builders GERAIS
    /// (<see cref="WorldServer.BuildMemberJoin"/> etc., em WorldServer.RoomFrames) + codecs de stage
    /// (<see cref="BotMovement"/>). O cliente JÁ tem o caminho de RECEBER 0x38/0x4b (quando um humano entra/spawna),
    /// então o frame sintetizado do estado do bot o desenha no slot/mundo — não é replay de captura.
    /// </summary>
    public sealed partial class BotManager
    {
        /// <summary>
        /// Member-join 0x38 do bot: broadcast a TODOS os humanos da sala — o MESMO trilho do join de um humano
        /// (<see cref="Network.WorldHandlers"/> Op_RoomJoin). RE (FUN_00406f40 @0x40735a): [38 00][status][slot]
        /// [state][uid:u16][slotFlag][registro], len = registroLen + 8.
        /// </summary>
        private void NotifyBotJoinedRoom(Domain.Field f, BotPlayer bot, int seat)
        {
            var join = BuildRoomMemberJoin(bot, seat);
            int n = BroadcastLobbyToHumans(f, join);
            Log.Ok("bot", "roster: 0x38 member-join '{0}' seat {1} (cls {2} lvl {3}) -> {4} humano(s)",
                bot.Name, seat, bot.CharClass, bot.Level, n);
        }

        /// <summary>Member-leave 0x3a do bot a TODOS os humanos: esvazia o slot em cada cliente.
        /// RE FUN_004091e0 @0x409403: [3a 00][slot], len 3.</summary>
        private void NotifyBotLeftRoom(Domain.Field f, int seat)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x3a);
            w.WriteByte((byte)seat);
            int n = BroadcastLobbyToHumans(f, w.ToArray());
            Log.Debug("bot", "roster: 0x3a member-leave seat {0} -> {1} humano(s)", seat, n);
        }

        /// <summary>Envia um frame de lobby a todos os humanos conectados do field (trilho comum do roster).</summary>
        private static int BroadcastLobbyToHumans(Domain.Field f, byte[] frame)
        {
            ClientSession[] humans;
            lock (f.Players) humans = f.Players.ToArray();
            int n = 0;
            foreach (var h in humans)
            {
                if (!h.Connected) continue;
                try { h.SendLobby(frame); n++; } catch { }
            }
            return n;
        }

        /// <summary>
        /// Spawn 3D do bot no STAGE — o 0x4b AddPlayer (canal FIELD) vai a TODOS os humanos já no stage, o
        /// MESMO modelo do spawn mútuo humano↔humano (<see cref="WorldServer.SpawnStageForHumans"/>): cada
        /// cliente registra o bot como player, sem roster divergente entre peers (a assimetria "só o master"
        /// era a suspeita do lockstep travado — §22.10 revisto). Quem carrega DEPOIS recebe o create na
        /// chegada, pelo SpawnStageForHumans. Corpo sintetizado por <see cref="BotMovement.BuildStageAddPlayer"/>.
        /// </summary>
        private void NotifyBotAddPlayer(Domain.Field f, int seat, BotPlayer bot)
        {
            var body = BotMovement.BuildStageAddPlayer(bot, seat);
            ClientSession[] humans;
            lock (f.Players) humans = f.Players.ToArray();
            int n = 0;
            foreach (var h in humans)
            {
                if (!h.Connected || h.Status != 3) continue;   // só cliente já no stage aceita o create
                try { h.SendMessage(0x4b, body); n++; } catch { }
            }
            Log.Ok("bot", "stage: 0x4b AddPlayer seat {0} (cls {1} lvl {2}) -> {3} humano(s)",
                seat, bot.CharClass, bot.Level, n);
        }

        /// <summary>
        /// Spawna os bots no STAGE no load de um humano (chamado do StartGameClock/0x4b, por sessão).
        /// Sequência CRAVADA da captura MITM real (stage_capture.txt): o servidor manda 0x48 FieldGameStart
        /// ao cliente que carrega e, em seguida, 0x4b AddPlayer por bot — no canal FIELD. Bots já spawnados
        /// neste round NÃO re-spawnam (o loader os recebe pelo spawn mútuo na chegada) — re-create duplicado
        /// no outro cliente era corrupção. Gated por ClientFramesEnabled.
        /// </summary>
        public void SpawnFieldBotsInStage(Domain.Field f, ClientSession loader)
        {
            if (!BotMovement.ClientFramesEnabled || f.BotCount == 0) return;
            try { loader.SendMessage(0x48, BotMovement.BuildFieldGameStart()); } catch { }   // round-load (1x por cliente)
            long now = Environment.TickCount64;
            foreach (var rec in f.BotRecs())
                if (rec.Bot is { SpawnedThisRound: false } bot)
                    SpawnBotIntoRound(f, rec, bot, now);   // golden source do spawn (mesmo caminho do BotTick)
        }

        /// <summary>0x38 member-join do BOT: registro genérico com o userid sintético (faixa alta) e o endereço
        /// P2P = socket do servidor (<see cref="WorldServer.BotPeerEndpoint"/> — zeros deixavam a entidade
        /// "fantasma" p/ combate). Delega ao golden source <see cref="WorldServer.BuildMemberJoin"/>.</summary>
        private static byte[] BuildRoomMemberJoin(BotPlayer bot, int seat) =>
            WorldServer.BuildMemberJoin(bot.Name, (byte)bot.CharClass, (byte)bot.Level, WorldServer.BotUserId(seat), seat,
                                        peerEp: WorldServer.BotPeerEndpoint);
    }
}
