using System;
using RakionServer.Common;
using RakionServer.World.Domain;
using RakionServer.World.Network;

namespace RakionServer.World
{
    /// <summary>
    /// Tick de IA/movimento do bot. Chamado pelo motor de partida quando o field está EM JOGO. Para
    /// cada bot: mira o humano inimigo vivo mais próximo (posição rastreada do 0x030A dele), avança a
    /// IA e SINTETIZA o 0x030A do bot, injetando-o aos peers humanos via <paramref name="send"/>. O
    /// servidor é a FONTE do tráfego do bot — nunca sequestra o canal humano↔humano (aprendizado do RE).
    /// O cliente recebe também o keystate 0x030F que seleciona a animação de caminhada/parada.
    /// </summary>
    public sealed partial class BotManager
    {
        public void TickField(Field field, float dt, Action<PlayerRec, byte[]> send)
        {
            long now = Environment.TickCount64;
            lock (field.SyncRoot)
            {
                if (field.State != 2) return; // só durante a partida
                foreach (PlayerRec botRec in field.BotSlots)
                {
                    BotPlayer bot = botRec.Bot!;
                    if (!bot.Alive && !TryRespawn(field, botRec, bot, now)) continue;
                    if (botRec.State == 3) botRec.State = 4;   // ready -> playing (vítima válida do 0x4f)
                    if (now < bot.HitReactionUntilMs) continue;
                    if (bot.TryFinishHitReaction(now))
                    {
                        Broadcast(field, send, BotMovement.SynthesizeNormalAnimation(
                            (byte)botRec.Slot, ++bot.MoveSeq, PlayerNormalAnimation.Rise));
                        continue;
                    }

                    if (!TryFindEnemyTarget(field, bot, out BotVector target, out byte targetSeat))
                        continue;
                    bot.TargetSeat = targetSeat;
                    bool inMelee = bot.Tick(target, dt);
                    botRec.Position = bot.Position;
                    bool moving = MathF.Abs(bot.Velocity.X) + MathF.Abs(bot.Velocity.Z) > 1f;

                    byte[] move = BotMovement.SynthesizeMove(
                        (byte)botRec.Slot, bot.Position, bot.Heading, ++bot.MoveSeq);
                    Broadcast(field, send, move);
                    Broadcast(field, send, BotMovement.SynthesizeKeystate(
                        (byte)botRec.Slot, ++bot.MoveSeq, moving));
                    if (bot.TryChangeLocomotion(moving, out bool locomotionMoving))
                    {
                        PlayerNormalAnimation animation = locomotionMoving
                            ? PlayerNormalAnimation.MoveForward
                            : PlayerNormalAnimation.Stand;
                        Broadcast(field, send, BotMovement.SynthesizeNormalAnimation(
                            (byte)botRec.Slot, ++bot.MoveSeq, animation));
                    }

                    // Ataque do bot: sintetiza a animação 0x0311 (cosmético — o dano bot->humano é
                    // client-authoritative, teto RE). Cooldown p/ não floodar.
                    if (inMelee && now >= bot.NextAttackReadyMs)
                    {
                        bot.NextAttackReadyMs = now + bot.Profile.AttackCooldownMs;
                        Broadcast(field, send, BotMovement.SynthesizeAttack(
                            (byte)botRec.Slot, ++bot.MoveSeq, bot.NextAttackVariant()));
                    }
                }
            }
        }

        private bool TryRespawn(Field field, PlayerRec record, BotPlayer bot, long now)
        {
            if (!bot.TryRespawn(now)) return false;
            record.Dead = false;
            record.State = 4;
            PublishBotLifecycles(field);
            Log.Ok("bot", "bot seat {0} respawnou com hp={1}/{2} (field {3})",
                record.Slot, bot.Health, bot.MaxHealth, field.Id);
            return true;
        }

        private static void Broadcast(Field field, Action<PlayerRec, byte[]> send, byte[] datagram)
        {
            foreach (PlayerRec human in field.Slots)
            {
                if (human.Session != null && human.Occupied) send(human, datagram);
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
