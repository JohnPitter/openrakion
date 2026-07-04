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
        /// Member-join 0x38 ao host: faz o cliente desenhar o bot no slot. RE (FUN_00406f40 @0x40735a, offsets
        /// confirmados pelas anotações Stack[] do disassembly): [38 00][status][slot][state][uid:u16][slotFlag]
        /// [registro], len = registroLen + 8.
        /// </summary>
        private void NotifyBotJoinedRoom(Domain.Field f, BotPlayer bot, int seat, ClientSession host)
        {
            try { host.SendLobby(BuildRoomMemberJoin(bot, seat)); }
            catch (Exception ex) { Log.Debug("bot", "roster 0x38 falhou: {0}", ex.Message); return; }
            Log.Ok("bot", "roster: 0x38 member-join '{0}' seat {1} (cls {2} lvl {3}) -> host [{4}]",
                bot.Name, seat, bot.CharClass, bot.Level, host.Slot);
        }

        /// <summary>Member-leave 0x3a ao host: esvazia o slot do bot. RE FUN_004091e0 @0x409403: [3a 00][slot], len 3.</summary>
        private void NotifyBotLeftRoom(Domain.Field f, int seat, ClientSession host)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x3a);
            w.WriteByte((byte)seat);
            try { host.SendLobby(w.ToArray()); } catch { }
            Log.Debug("bot", "roster: 0x3a member-leave seat {0} -> host [{1}]", seat, host.Slot);
        }

        /// <summary>
        /// Spawn 3D do bot no STAGE — emite o 0x4b AddPlayer (canal FIELD via SendMessage; NÃO o LOBBY) que
        /// INSTANCIA o avatar no host. O corpo (decode byte-a-byte da captura MITM) é sintetizado pelo codec
        /// <see cref="BotMovement.BuildStageAddPlayer"/>; aqui só se traduz estado→chamada e serializa.
        /// </summary>
        private void NotifyBotAddPlayer(Domain.Field f, int seat, BotPlayer bot)
        {
            var host = f.Master;
            if (host == null) return;
            try { host.SendMessage(0x4b, BotMovement.BuildStageAddPlayer(bot, seat)); } catch { return; }
            Log.Ok("bot", "stage: 0x4b AddPlayer seat {0} (cls {1} lvl {2}) -> host [{3}]",
                seat, bot.CharClass, bot.Level, host.Slot);
        }

        /// <summary>
        /// Spawna os bots no STAGE no load do host (chamado do StartGameClock/0x4b). Sequência CRAVADA da
        /// captura MITM real (stage_capture.txt): o servidor manda 0x48 FieldGameStart UMA vez e, em seguida,
        /// 0x4b AddPlayer por bot — no canal FIELD. O 0x4b é o que INSTANCIA o avatar 3D do bot no mundo do
        /// host. Gated por ClientFramesEnabled.
        /// </summary>
        public void SpawnFieldBotsInStage(Domain.Field f)
        {
            if (!BotMovement.ClientFramesEnabled || f.BotCount == 0) return;
            var host = f.Master;
            if (host == null) return;
            try { host.SendMessage(0x48, BotMovement.BuildFieldGameStart()); } catch { }   // round-load (1x)
            foreach (var rec in f.BotRecs())
            {
                var bot = rec.Bot!;
                bot.SpawnedThisRound = true;
                bot.SpawnedMs = Environment.TickCount64;
                bot.InitStagePosition();
                rec.State = 4; rec.Dead = false; bot.Dead = false;
                bot.SpawnGen++;                                  // nova entidade na engine -> a ponte invalida o cache e re-acha (anti-crash)
                if (BotMovement.UseNpcAvatar)
                {
                    SpawnBotAsNpc(f, rec, bot);                  // 0x307 NPC (descartado: classes auto-vivas não registram)
                }
                else
                {
                    NotifyBotAddPlayer(f, rec.Slot, bot);        // 0x45 fantasma CPlayer (RENDERIZA) — movido pela ponte de injeção
                    EnsureBotPeerConnected(f, rec, bot);
                }
            }
        }

        /// <summary>0x38 member-join do BOT: monta o registro genérico com o userid sintético do bot (faixa alta).
        /// Delega ao golden source <see cref="WorldServer.BuildMemberJoin"/>.</summary>
        private static byte[] BuildRoomMemberJoin(BotPlayer bot, int seat) =>
            WorldServer.BuildMemberJoin(bot.Name, (byte)bot.CharClass, (byte)bot.Level, WorldServer.BotUserId(seat), seat);
    }
}
