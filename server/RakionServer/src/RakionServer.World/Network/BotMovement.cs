using System;
using System.Buffers.Binary;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    /// <summary>
    /// Sintetiza o datagrama de movimento 0x030A (26 bytes) DO BOT, no MESMO formato do peer humano
    /// (<see cref="GameplayActionDatagram"/>): o servidor é a FONTE — o bot não tem endpoint. O pacote
    /// é injetado no relay do <see cref="UdpGameplay"/> aos peers humanos do field, com o assento do
    /// bot na posição de origem. Nunca sequestra o canal humano↔humano (aprendizado do RE): o servidor
    /// só emite o tráfego DO bot.
    /// </summary>
    public static class BotMovement
    {
        public const ushort MoveType = 0x030a;
        public const int MoveSize = 26;

        /// <summary>Monta o 0x030A do bot: origem = <paramref name="seat"/>, posição/rumo do estado da IA.</summary>
        public static byte[] SynthesizeMove(
            byte seat, BotVector position, float heading, uint sequence,
            bool moving = false, ushort deltaMs = 150)
        {
            byte[] p = new byte[MoveSize];
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(0), MoveType);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(2), sequence);
            p[6] = seat;
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(7), deltaMs);
            p[9] = (byte)(seat | (moving ? 0x20 : 0));
            p[10] = moving ? (byte)4 : (byte)1; // Forward / Stand
            BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(11), ToWire(position.X));
            BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(13), ToWire(position.Y));
            BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(15), ToWire(position.Z));
            BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(17), HeadingToWire(heading));
            p[19] = 0;
            // 20..25 são deltas acumuláveis de câmera, não rumo absoluto. Reenviar heading aqui
            // faz o cliente somar a mesma rotação a cada tick e o avatar girar no próprio eixo.
            BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(22), 0);
            BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(24), 0);
            return p;
        }

        public static byte[] SynthesizeKeystate(byte seat, uint sequence, bool moving)
        {
            byte[] packet = new byte[GameplayActionDatagram.SyncSize];
            BinaryPrimitives.WriteUInt16LittleEndian(
                packet.AsSpan(0), GameplayActionDatagram.SyncType);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(2), sequence);
            packet[6] = seat;
            packet[7] = seat;
            packet[8] = 0x08;
            packet[10] = 0x01;
            packet[12] = moving ? (byte)0 : (byte)3;
            packet[13] = moving ? (byte)1 : (byte)0;
            return packet;
        }

        public const ushort AttackType = 0x0311;
        public const int AttackSize = 10;

        /// <summary>Sintetiza a animação de ataque do bot (0x0311, kind=Attack). Cosmético: o cliente
        /// vê o bot golpear; o dano bot→humano é client-authoritative (teto RE), não server-side.</summary>
        public static byte[] SynthesizeAttack(
            byte seat, uint sequence, BotAttackVariant variant = BotAttackVariant.VariantA)
        {
            byte[] p = new byte[AttackSize];
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(0), AttackType);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(2), sequence);
            p[6] = seat;
            p[7] = seat;
            p[8] = 1;   // kind = Attack
            p[9] = variant switch
            {
                BotAttackVariant.VariantA => 0x1b,
                BotAttackVariant.VariantB => 0x1a,
                _ => 0x12
            };
            return p;
        }

        /// <summary>Reação visual de dano do bot (0x0311 kind=Damage, shape estendido de 12 bytes).</summary>
        public static byte[] SynthesizeDamage(byte seat, uint sequence)
        {
            byte[] packet = new byte[GameplayActionDatagram.ExtendedAnimationSize];
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0), AttackType);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(2), sequence);
            packet[6] = seat;
            packet[7] = seat;
            packet[8] = (byte)PlayerAnimationKind.Damage;
            packet[9] = 1;
            return packet;
        }

        /// <summary>Extrai posição e rumo de um 0x030A humano recebido.</summary>
        public static bool TryReadPose(
            ReadOnlySpan<byte> packet, out BotVector position, out float heading)
        {
            position = default;
            heading = 0;
            if (packet.Length < MoveSize ||
                BinaryPrimitives.ReadUInt16LittleEndian(packet) != MoveType) return false;
            position = new BotVector(
                BinaryPrimitives.ReadInt16LittleEndian(packet[11..]),
                BinaryPrimitives.ReadInt16LittleEndian(packet[13..]),
                BinaryPrimitives.ReadInt16LittleEndian(packet[15..]));
            float normalized = Math.Clamp(
                BinaryPrimitives.ReadInt16LittleEndian(packet[17..]) / (float)short.MaxValue,
                -1f, 1f);
            heading = normalized * MathF.PI;
            return true;
        }

        /// <summary>Extrai apenas a posição do 0x030A.</summary>
        public static bool TryReadPosition(ReadOnlySpan<byte> packet, out BotVector position)
        {
            return TryReadPose(packet, out position, out _);
        }

        private static short ToWire(float v) =>
            (short)Math.Clamp(MathF.Round(v), short.MinValue, short.MaxValue);

        private static short HeadingToWire(float radians)
        {
            // rumo (-π..π) -> i16 (-32767..32767)
            float norm = radians / MathF.PI;
            return (short)Math.Clamp(MathF.Round(norm * short.MaxValue), short.MinValue, short.MaxValue);
        }
    }
}
