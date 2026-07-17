using System;

namespace RakionServer.World.Domain
{
    /// <summary>Vetor 3D leve para a IA do bot (posições/velocidades no espaço do `.wld`).</summary>
    public readonly record struct BotVector(float X, float Y, float Z)
    {
        public static readonly BotVector Zero = new(0, 0, 0);

        public static BotVector operator +(BotVector a, BotVector b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static BotVector operator -(BotVector a, BotVector b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static BotVector operator *(BotVector a, float s) => new(a.X * s, a.Y * s, a.Z * s);

        public float LengthSquared => X * X + Y * Y + Z * Z;
        public float Length => MathF.Sqrt(LengthSquared);

        public BotVector Normalized()
        {
            float len = Length;
            return len <= 1e-6f ? Zero : this * (1f / len);
        }

        /// <summary>Distância horizontal (plano X/Z), ignorando altura — o que importa no melee.</summary>
        public float HorizontalDistanceTo(BotVector other)
        {
            float dx = X - other.X, dz = Z - other.Z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Ângulo de rumo (yaw) no plano X/Z, em radianos.</summary>
        public float HeadingTo(BotVector other) => MathF.Atan2(other.X - X, other.Z - Z);
    }
}
