using System;
using RakionServer.Common;
using RakionServer.World.Domain;
using RakionServer.World.Navigation;
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
        private static readonly IBotNavigationSurface NavigationSurface =
            BattleMapNavigationSurface.Instance;
        private readonly record struct BotTickContext(
            Field Field,
            float DeltaTime,
            long Now,
            Action<PlayerRec, byte[]> Send);

        public void TickField(Field field, float dt, Action<PlayerRec, byte[]> send)
        {
            long now = Environment.TickCount64;
            lock (field.SyncRoot)
            {
                if (field.State != 2) return; // só durante a partida
                var context = new BotTickContext(field, dt, now, send);
                foreach (PlayerRec botRec in field.BotSlots)
                    TickBot(context, botRec);
            }
        }

        private void TickBot(BotTickContext context, PlayerRec record)
        {
            BotPlayer bot = record.Bot!;
            if (!bot.Alive && !TryRespawn(
                    context.Field, record, bot, context.Now)) return;
            if (record.State == 3) record.State = 4;
            if (context.Now < bot.HitReactionUntilMs) return;
            if (bot.TryFinishHitReaction(context.Now))
            {
                Broadcast(context.Field, context.Send,
                    BotMovement.SynthesizeNormalAnimation(
                        (byte)record.Slot,
                        ++bot.MoveSeq,
                        PlayerNormalAnimation.Rise));
                return;
            }
            if (!TryFindEnemyTarget(
                    context.Field, bot, out BotVector target, out byte targetSeat))
                return;

            BotNavigationMode previousMode = bot.NavigationMode;
            bot.TargetSeat = targetSeat;
            BotNavigationAction action = bot.TickNavigated(
                target,
                targetSeat,
                context.Field.MapId,
                context.Now,
                context.DeltaTime,
                NavigationSurface);
            record.Position = bot.Position;
            PublishMovement(context, record, bot, action);
            LogNavigationTransition(context.Field, record, action, previousMode);
            TryAttack(context, record, bot, action);
        }

        private void PublishMovement(
            BotTickContext context,
            PlayerRec record,
            BotPlayer bot,
            BotNavigationAction action)
        {
            bool moving = action.IsMoving &&
                MathF.Abs(bot.Velocity.X) + MathF.Abs(bot.Velocity.Z) > 1f;
            Broadcast(context.Field, context.Send, BotMovement.SynthesizeMove(
                (byte)record.Slot, bot.Position, bot.Heading, ++bot.MoveSeq));
            Broadcast(context.Field, context.Send, BotMovement.SynthesizeKeystate(
                (byte)record.Slot, ++bot.MoveSeq, moving));
            if (!bot.ShouldPublishControls(action.Controls, context.Now)) return;

            Broadcast(context.Field, context.Send,
                BotMovement.SynthesizeNormalAnimation(
                    (byte)record.Slot,
                    ++bot.MoveSeq,
                    BotMovement.AnimationForControls(action.Controls)));
            PublishBotLifecycles(context.Field);
        }

        private static void LogNavigationTransition(
            Field field,
            PlayerRec record,
            BotNavigationAction action,
            BotNavigationMode previousMode)
        {
            if (action.Mode == previousMode) return;
            Log.Info("bot", "field {0} seat {1}: navegacao {2} -> {3}, controles={4}",
                field.Id, record.Slot, previousMode, action.Mode, action.Controls);
        }

        private static void TryAttack(
            BotTickContext context,
            PlayerRec record,
            BotPlayer bot,
            BotNavigationAction action)
        {
            if (!action.IsAttacking || context.Now < bot.NextAttackReadyMs) return;
            bot.NextAttackReadyMs = context.Now + bot.Profile.AttackCooldownMs;
            Broadcast(context.Field, context.Send, BotMovement.SynthesizeAttack(
                (byte)record.Slot, ++bot.MoveSeq, bot.NextAttackVariant()));
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
