using System;

namespace RakionServer.World.Domain
{
    /// <summary>
    /// Combate server-side do bot (o servidor é a autoridade do HP do bot). Reproduz o que o RE
    /// permite: o bot leva dano de humanos e MORRE (dá kill/pontos ao humano). O cliente informa o
    /// início do ataque; o domínio escolhe exatamente um bot inimigo vivo, dentro do alcance e do
    /// cone frontal, e impede emissões repetidas do mesmo golpe.
    /// </summary>
    public static class BotCombat
    {
        /// <summary>Alcance de melee em unidades wire (i16 do 0x030A). Aproxima a hitbox do golpe.</summary>
        public const float MeleeRangeWire = 600f;
        public const int MeleeAttackCooldownMs = 250;
        private const float FrontalDotThreshold = 0.258819f; // cos(75°)

        public readonly record struct BotHit(PlayerRec BotRecord, bool Died);

        /// <summary>
        /// Resolve um golpe contra o bot inimigo mais próximo no cone frontal do atacante.
        /// </summary>
        public static bool TryResolveMeleeAttack(
            Field field, PlayerRec attacker, long nowMs, int damage, out BotHit hit)
        {
            hit = default;
            if (attacker.Dead || attacker.State != 4 || damage <= 0 ||
                nowMs < attacker.NextBotMeleeAttackMs)
                return false;
            attacker.NextBotMeleeAttackMs = nowMs + MeleeAttackCooldownMs;

            PlayerRec? target = FindNearestTarget(field, attacker);
            if (target?.Bot == null) return false;

            uint hitSequence = ++attacker.BotHitSequence;
            hit = new BotHit(target, target.Bot.TakeDamage(
                damage, (byte)attacker.Slot, hitSequence));
            return true;
        }

        private static PlayerRec? FindNearestTarget(Field field, PlayerRec attacker)
        {
            PlayerRec? nearest = null;
            float nearestDistance = float.MaxValue;
            foreach (PlayerRec candidate in field.BotSlots)
            {
                BotPlayer bot = candidate.Bot!;
                if (!bot.Alive || bot.Team == attacker.Team) continue;

                float distance = candidate.Position.HorizontalDistanceTo(attacker.Position);
                if (distance > MeleeRangeWire || distance >= nearestDistance ||
                    !IsInsideFrontalCone(attacker, candidate.Position, distance)) continue;
                nearest = candidate;
                nearestDistance = distance;
            }
            return nearest;
        }

        private static bool IsInsideFrontalCone(
            PlayerRec attacker, BotVector targetPosition, float distance)
        {
            if (distance <= 1f) return true;
            float dx = (targetPosition.X - attacker.Position.X) / distance;
            float dz = (targetPosition.Z - attacker.Position.Z) / distance;
            float dot = MathF.Sin(attacker.Heading) * dx + MathF.Cos(attacker.Heading) * dz;
            return dot >= FrontalDotThreshold;
        }
    }
}
