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
        public static byte[] SynthesizeMove(byte seat, BotVector position, float heading, uint sequence, ushort deltaMs = 150)
        {
            byte[] p = new byte[MoveSize];
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(0), MoveType);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(2), sequence);
            p[6] = seat;
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(7), deltaMs);
            p[9] = 0;   // state Normal + echo 0
            p[10] = 0;  // actionCode
            BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(11), ToWire(position.X));
            BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(13), ToWire(position.Y));
            BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(15), ToWire(position.Z));
            BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(17), HeadingToWire(heading));
            p[19] = 0;
            // view rotation = rumo (o cliente usa p/ orientar o modelo do peer)
            BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(20), HeadingToWire(heading));
            BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(22), 0);
            BinaryPrimitives.WriteInt16LittleEndian(p.AsSpan(24), 0);
            return p;
        }

        public const ushort AttackType = 0x0311;
        public const int AttackSize = 10;

        /// <summary>Sintetiza a animação de ataque do bot (0x0311, kind=Attack). Cosmético: o cliente
        /// vê o bot golpear; o dano bot→humano é client-authoritative (teto RE), não server-side.</summary>
        public static byte[] SynthesizeAttack(byte seat, uint sequence)
        {
            byte[] p = new byte[AttackSize];
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(0), AttackType);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(2), sequence);
            p[6] = seat;
            p[7] = 0;   // sourceEcho
            p[8] = 1;   // kind = Attack
            p[9] = 0;   // arg0
            return p;
        }

        /// <summary>Extrai a posição (i16 x/y/z) de um 0x030A humano recebido, p/ a IA do bot mirar.</summary>
        public static bool TryReadPosition(ReadOnlySpan<byte> packet, out BotVector position)
        {
            position = default;
            if (packet.Length < MoveSize ||
                BinaryPrimitives.ReadUInt16LittleEndian(packet) != MoveType) return false;
            position = new BotVector(
                BinaryPrimitives.ReadInt16LittleEndian(packet[11..]),
                BinaryPrimitives.ReadInt16LittleEndian(packet[13..]),
                BinaryPrimitives.ReadInt16LittleEndian(packet[15..]));
            return true;
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
