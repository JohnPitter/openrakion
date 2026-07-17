using System;
using System.Net;
using RakionServer.World.Domain;
using RakionServer.World.Network;

namespace RakionServer.World
{
    /// <summary>
    /// Tick de IA/movimento do bot. Chamado pelo motor de partida quando o field está EM JOGO. Para
    /// cada bot: mira o humano inimigo vivo mais próximo (posição rastreada do 0x030A dele), avança a
    /// IA e SINTETIZA o 0x030A do bot, injetando-o aos peers humanos via <paramref name="send"/>. O
    /// servidor é a FONTE do tráfego do bot — nunca sequestra o canal humano↔humano (aprendizado do RE).
    /// Teto conhecido: entrega movimento FUNCIONAL; o número cosmético HIT×N exige peer de sessão real.
    /// </summary>
    public sealed partial class BotManager
    {
        public void TickField(Field field, float dt, Action<IPEndPoint, byte[]> send)
        {
            lock (field.SyncRoot)
            {
                if (field.State != 2) return; // só durante a partida
                foreach (PlayerRec botRec in field.BotSlots)
                {
                    BotPlayer bot = botRec.Bot!;
                    if (!bot.Alive) continue;

                    if (!TryFindEnemyTarget(field, bot, out BotVector target, out byte targetSeat))
                        continue;
                    bot.TargetSeat = targetSeat;
                    bot.Tick(target, dt);
                    botRec.Position = bot.Position;

                    byte[] move = BotMovement.SynthesizeMove(
                        (byte)botRec.Slot, bot.Position, bot.Heading, ++bot.MoveSeq);

                    foreach (PlayerRec human in field.Slots)
                    {
                        IPEndPoint? endpoint = human.Session?.UdpEndpoint;
                        if (endpoint != null && human.Occupied) send(endpoint, move);
                    }
                }
            }
        }

        /// <summary>Humano vivo do time OPOSTO mais próximo do bot (posição rastreada). False se não houver.</summary>
        private static bool TryFindEnemyTarget(Field field, BotPlayer bot, out BotVector target, out byte seat)
        {
            target = default;
            seat = Field.NoSeat;
            float best = float.MaxValue;
            foreach (PlayerRec rec in field.Slots)
            {
                if (rec.Session == null || !rec.Occupied || rec.Dead) continue;
                if (rec.Team == bot.Team) continue;   // só inimigos
                float d = bot.Position.HorizontalDistanceTo(rec.Position);
                if (d < best) { best = d; target = rec.Position; seat = (byte)rec.Slot; }
            }
            return seat != Field.NoSeat;
        }
    }
}
