using System;
using RakionServer.Common;
using RakionServer.World.Domain;
using RakionServer.World.Network;

namespace RakionServer.World
{
    /// <summary>
    /// Subsistema de BOTS reconstruído sobre o motor de partida do golden. Regra de negócio pura de
    /// roster/lifecycle: só o HOST adiciona, só ANTES da partida, no time OPOSTO ao host; o bot é um
    /// peer sintético (<see cref="BotPlayer"/>) que entra no roster (0x38) como um jogador, é movido
    /// pela IA (<see cref="BotSteering"/>) e tem o movimento sintetizado no fio (0x030A, fase de rede).
    ///
    /// Teto do RE (respeitado): entrega oponente FUNCIONAL (roster, movimento, dano/morte server-side)
    /// mas NÃO o número cosmético HIT×N nativo (limite type-7; exige peer de sessão real). Nenhum
    /// pacote do bot fala direto com o cliente — só via o socket do <see cref="UdpGameplay"/>.
    /// Ciclo efêmero: bots somem no fim do match ou quando o último humano sai.
    /// </summary>
    public sealed partial class BotManager
    {
        private static readonly string[] BotNames =
            { "Rok", "Karion", "Ares", "Vyl", "Drak", "Nyx", "Zair", "Brutus", "Kael", "Thorn" };

        private int _nameSeq;

        public readonly record struct AddBotResult(bool Ok, string Message, int Seat, BotPlayer? Bot);

        /// <summary>
        /// Adiciona um bot ao field do <paramref name="host"/> no time oposto. Só o dono da sala, e só
        /// antes do início da partida. Sincroniza o roster do cliente humano com um member-join (0x38).
        /// </summary>
        public AddBotResult AddBotToField(Field field, ClientSession host, BotDifficulty difficulty)
        {
            lock (field.SyncRoot)
            {
                PlayerRec? hostRec = field.FindRec(host);
                if (hostRec == null) return new AddBotResult(false, "voce nao esta na sala", -1, null);
                if (field.MasterSlot != hostRec.Slot)
                    return new AddBotResult(false, "so o dono da sala adiciona bot", -1, null);
                if (field.Phase == MatchPhase.Playing || field.State == 2)
                    return new AddBotResult(false, "partida em andamento", -1, null);
                if (field.Mode == 0)
                    return new AddBotResult(false, "bots so em sala competitiva (PvP)", -1, null);

                byte team = (byte)(hostRec.Team ^ 1);   // time oposto ao host
                byte level = host.CharLevel == 0 ? (byte)1 : host.CharLevel;
                byte cls = host.CharClass == 0 ? (byte)1 : host.CharClass;
                var bot = new BotPlayer
                {
                    Name = NextName(),
                    Level = level,
                    CharClass = cls,
                    Team = team,
                    Difficulty = difficulty,
                    Profile = BotProfile.For(difficulty),
                };
                bot.InitHealth(level);

                int seat = field.AddBot(bot, team);
                if (seat < 0) return new AddBotResult(false, "time cheio", -1, null);

                Log.Ok("bot", "[{0}] bot '{1}' (lvl {2} cls {3} {4}) -> field {5} seat {6} time {7}",
                    host.Slot, bot.Name, level, cls, difficulty, field.Id, seat, team);

                // Espelha no roster do cliente humano: o bot aparece no slot da sala (member-join 0x38).
                field.BroadcastLobby(RoomRosterFrames.PlayerJoined(field.Slots[seat]));
                return new AddBotResult(true, "bot adicionado", seat, bot);
            }
        }

        /// <summary>Remove um bot pelo seat e avisa o roster (0x3a member-leave sintético).</summary>
        public bool RemoveBot(Field field, byte seat)
        {
            lock (field.SyncRoot)
            {
                if (!field.RemoveBot(seat)) return false;
                field.BroadcastLobby(BuildLeave(seat));
                return true;
            }
        }

        /// <summary>Limpa todos os bots do field (fim de match / último humano saiu).</summary>
        public int RemoveAllBots(Field field)
        {
            lock (field.SyncRoot)
            {
                int removed = 0;
                foreach (PlayerRec rec in field.Slots)
                {
                    if (rec.Bot == null) continue;
                    byte seat = (byte)rec.Slot;
                    if (field.RemoveBot(seat)) { field.BroadcastLobby(BuildLeave(seat)); removed++; }
                }
                if (removed > 0) Log.Info("bot", "field {0}: {1} bot(s) removido(s)", field.Id, removed);
                return removed;
            }
        }

        private static byte[] BuildLeave(byte seat)
        {
            using var w = new PacketWriter();
            w.WriteWord(0x3a).WriteByte(seat);   // member-leave: libera o slot no cliente
            return w.ToArray();
        }

        private string NextName()
        {
            string baseName = BotNames[_nameSeq % BotNames.Length];
            int cycle = _nameSeq / BotNames.Length;
            _nameSeq++;
            return cycle == 0 ? baseName : baseName + (cycle + 1);
        }
    }
}
