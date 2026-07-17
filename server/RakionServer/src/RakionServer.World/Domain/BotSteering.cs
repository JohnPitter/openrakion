using System;

namespace RakionServer.World.Domain
{
    /// <summary>Resultado de um passo de IA: nova posição, velocidade e rumo do bot.</summary>
    public readonly record struct BotStep(BotVector Position, BotVector Velocity, float Heading, bool InMelee);

    /// <summary>
    /// Motor de IA do bot — funções PURAS (sem I/O, testáveis). Reproduz o comportamento humano
    /// aprendido no RE: avançar até o alvo com velocidade/aceleração próprias, orbitar (strafe)
    /// quando entra no alcance de melee, e antecipar o movimento do alvo (EMA da velocidade).
    /// O resultado alimenta a síntese do datagrama de movimento 0x030A server-side.
    /// </summary>
    public static class BotSteering
    {
        /// <summary>
        /// Avança um passo de <paramref name="dt"/> segundos. Se estiver fora do alcance de melee,
        /// persegue o alvo antecipado; dentro do alcance, orbita mantendo a distância. A velocidade
        /// é suavizada por <see cref="BotProfile.Acceleration"/> (steering, não teleporte).
        /// </summary>
        public static BotStep Step(
            BotVector position, BotVector velocity, BotProfile profile,
            BotVector targetPosition, BotVector targetVelocity, float dt)
        {
            BotVector aimed = targetPosition + targetVelocity * profile.Anticipation;
            float distance = position.HorizontalDistanceTo(aimed);
            BotVector desired;
            bool melee = distance <= profile.MeleeRange;

            if (melee)
            {
                // Orbita: tangente ao raio alvo→bot, girando a StrafeSpeed. Mantém pressão sem colar.
                BotVector radial = (position - aimed).Normalized();
                float ang = profile.StrafeSpeed * dt;
                var tangent = new BotVector(
                    radial.X * MathF.Cos(ang) - radial.Z * MathF.Sin(ang),
                    0,
                    radial.X * MathF.Sin(ang) + radial.Z * MathF.Cos(ang));
                desired = tangent * profile.MoveSpeed;
            }
            else
            {
                desired = (aimed - position).Normalized() * profile.MoveSpeed;
            }

            // Aceleração = interpolação da velocidade atual para a desejada (0..1).
            float a = Math.Clamp(profile.Acceleration, 0f, 1f);
            BotVector newVelocity = velocity + (desired - velocity) * a;
            BotVector newPosition = position + newVelocity * dt;
            float heading = newPosition.HeadingTo(aimed);
            return new BotStep(newPosition, newVelocity, heading, melee);
        }

        /// <summary>Média móvel exponencial da velocidade observada do alvo (para antecipação).</summary>
        public static BotVector SmoothVelocity(BotVector previousEma, BotVector sample, float weight)
        {
            float w = Math.Clamp(weight, 0f, 1f);
            return previousEma + (sample - previousEma) * w;
        }
    }
}
