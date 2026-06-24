using System;
using System.Text;
using RakionServer.Common;
using RakionServer.World.Domain;
using RakionServer.World.Network;

namespace RakionServer.World
{
    /// <summary>
    /// Renderização do bot no ROSTER da sala (os slots RED/BLUE do game room). SÍNTESE do member-join do
    /// worldserv ORIGINAL (RE em rakion-work/ghidra-proj/joinflow*.out.txt + joinasm.out.txt):
    ///   - broadcast 0x38 montado em FUN_00406f40 @0x40735a (header 8B + registro);
    ///   - registro de jogador serializado por FUN_0040b7f0 @0x40b7f0 ([nome\0][tag\0] + cauda fixa 0x49B).
    /// O cliente JÁ tem o caminho de RECEBER 0x38 (quando um humano entra na sala), então o frame sintetizado
    /// do estado do bot o desenha no slot — não é replay de captura.
    /// </summary>
    public sealed partial class WorldServer
    {
        /// <summary>userid sintético do bot no frame (faixa alta p/ não colidir com usuários reais 1..MaxUser).</summary>
        private static ushort BotUserId(int seat) => (ushort)(0xB000 + seat);

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
        /// "Entrou/carregou no STAGE" 0x45 ao host: o STAGE não tem frame de spawn próprio — o cliente
        /// spawna o jogador a partir do roster que JÁ tem (0x38) ao receber este sinal. RE FUN_00407c70
        /// @0x407d44: broadcast de 4 bytes [45 00 00 seat] (FUN_004061f0) + seta o slot p/ state 3 ("loaded").
        /// O servidor original ainda dispara 0x54 ("begin") quando todos carregam; aqui o relógio do .NET
        /// (StartGameClock) já conduz a partida, então só o 0x45 por bot.
        /// </summary>
        private void NotifyBotEnteredStage(Domain.Field f, int seat)
        {
            var host = f.Master;
            if (host == null) return;
            using var w = new PacketWriter();
            w.WriteWord(0x45);          // +0 opcode (u16)
            w.WriteByte(0);             // +2 pad
            w.WriteByte((byte)seat);    // +3 seat
            try { host.SendLobby(w.ToArray()); } catch { }
            Log.Ok("bot", "stage: 0x45 entered seat {0} -> host [{1}]", seat, host.Slot);
        }

        /// <summary>
        /// "Begin" 0x54 ao host (RE FUN_004066c0 @0x4066c0): [54 00], 2 bytes, mandado a cada slot state 3/4
        /// QUANDO todos carregaram. O 0x45 só marca "loaded"; é o 0x54 que faz o cliente transicionar os
        /// avatares remotos (o bot) PARA o stage. Sem ele o bot não entra mesmo com o 0x45.
        /// </summary>
        private void NotifyStageBegin(ClientSession host)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x54);
            try { host.SendLobby(w.ToArray()); } catch { }
            Log.Ok("bot", "stage: 0x54 begin -> host [{0}]", host.Slot);
        }

        /// <summary>
        /// AddPlayer 0x4c — é o que REALMENTE spawna o avatar 3D do bot no stage (RE engine.dll: a vtable
        /// IScavengerWorldNet manda SendFieldGameAddPlayer/Reply → CSessionState::AddRemotePlayer; o 0x45/0x54
        /// só montam "carregou/begin"). Frame reliable do world: [4c 00][u8 seat][u16 blobLen][blob], onde o
        /// blob é o MESMO registro FUN_0040b7f0 do slot da sala (0x38) — por isso o bot já aparecia no slot,
        /// só faltava reusar o blob no opcode de campo. (Agente flagou 0x4b/0x4c como candidatos; 0x4c traz o
        /// seat explícito, casando com AddRemotePlayer(slot,blobLen,blob).)
        /// </summary>
        private void NotifyBotAddPlayer(Domain.Field f, int seat, BotPlayer bot)
        {
            var host = f.Master;
            if (host == null) return;
            byte[] blob = BuildBotPlayerRecord(bot);
            using var w = new PacketWriter();
            w.WriteWord(0x4c);              // +0 opcode (u16)
            w.WriteByte((byte)seat);       // +2 slot
            w.WriteWord(blob.Length);      // +3 blobLen (u16)
            w.WriteBytes(blob);            // +5 blob (FUN_0040b7f0)
            try { host.SendLobby(w.ToArray()); } catch { }
            Log.Ok("bot", "stage: 0x4c AddPlayer seat {0} ({1}B blob) -> host [{2}]", seat, blob.Length, host.Slot);
        }

        /// <summary>
        /// Spawna os bots no STAGE no MOMENTO do load do host (0x4b → StartGameClock): emite o 0x45 de cada
        /// bot (carregou) e então o 0x54 (begin, todos carregaram). Timing junto do load do host — o cliente
        /// processa o 0x45 dos remotos durante a própria entrada — + o begin que faltava (o BotTick só emitia
        /// o 0x45, ~60ms depois e sem 0x54, e o avatar nunca entrava). Gated por ClientFramesEnabled.
        /// </summary>
        public void SpawnFieldBotsInStage(Domain.Field f)
        {
            if (!BotMovement.ClientFramesEnabled || f.BotCount == 0) return;
            var host = f.Master;
            if (host == null) return;
            foreach (var rec in f.BotRecs())
            {
                var bot = rec.Bot!;
                bot.SpawnedThisRound = true;            // marca p/ o BotTick não re-emitir
                rec.State = 4; rec.Dead = false; bot.Dead = false;
                NotifyBotEnteredStage(f, rec.Slot);     // 0x45 [45 00 00 seat] (carregou)
                NotifyBotAddPlayer(f, rec.Slot, bot);   // 0x4c AddPlayer = SPAWN do avatar 3D (a peça que faltava)
            }
            NotifyStageBegin(host);                     // 0x54 [54 00] = todos carregaram → entra no stage
        }

        /// <summary>Header do 0x38 (8B) + registro do jogador. Veja o disassembly em joinasm.out.txt.</summary>
        private static byte[] BuildRoomMemberJoin(BotPlayer bot, int seat)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x38);              // +0  opcode (u16)
            w.WriteByte(0);                 // +2  status = 0 (sucesso)
            w.WriteByte((byte)seat);        // +3  slot (0..0x13; 10..0x13 = BLUE)
            w.WriteByte(3);                 // +4  state (3 = ocupado/ready, como AssignBotSeat)
            w.WriteWord(BotUserId(seat));   // +5  userid (u16 LE)
            w.WriteByte(0);                 // +7  slotFlag (game+0x127; 0 = não travado)
            w.WriteBytes(BuildBotPlayerRecord(bot));   // +8  registro (FUN_0040b7f0)
            return w.ToArray();
        }

        /// <summary>
        /// Registro de jogador — espelho EXATO de FUN_0040b7f0: [nome\0][tag\0] e, a seguir, a cauda fixa de
        /// 0x49 bytes. Os 2 blocos de equip (38B @+0x1da4 e 19B @+0x1dca) vão ZERADOS — o bot não tem gear, e
        /// item id 0 = slot de equip vazio (modelo base), evitando id inválido que crasharia o render 3D do slot.
        /// </summary>
        private static byte[] BuildBotPlayerRecord(BotPlayer bot)
        {
            using var w = new PacketWriter();
            w.WriteBytes(Encoding.ASCII.GetBytes(bot.Name)); w.WriteByte(0);  // nome + NUL  (+0x14a8)
            w.WriteByte(0);                     // tag/clan vazio + NUL        (+0x14c2)
            w.WriteByte(0);                     // +0x1478
            w.WriteUInt32(0);                   // +0x1450 (id/score)
            w.WriteWord(0);                     // +0x1458
            w.WriteUInt32(0);                   // +0x1454
            w.WriteWord(0);                     // +0x145a
            w.WriteByte((byte)bot.CharClass);   // +0x1530  CLASSE
            w.WriteByte((byte)bot.Level);       // +0x1531  LEVEL
            w.WriteByte(0);                     // +0x1473
            w.WriteBytes(new byte[0x26]);       // +0x1da4  equip/aparência (38B) — sem gear
            w.WriteBytes(new byte[0x13]);       // +0x1dca  equip2/stat (19B) — sem gear
            return w.ToArray();
        }
    }
}
