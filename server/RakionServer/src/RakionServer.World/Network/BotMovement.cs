using System;
using System.Buffers.Binary;
using RakionServer.World.Domain;

namespace RakionServer.World.Network
{
    /// <summary>IDs do switch ExecNormalAnim do cliente v258, transportados em 0x0311 kind=Normal.</summary>
    public enum PlayerNormalAnimation : byte
    {
        Stand = 1,
        Idle01 = 2,
        Idle02 = 3,
        MoveForward = 4,
        MoveBackward = 5,
        MoveLeft = 6,
        MoveRight = 7,
        MoveForwardLeft = 8,
        MoveForwardRight = 9,
        MoveBackwardLeft = 10,
        MoveBackwardRight = 11,
        Jump = 12,
        Rise = 14,
        RollFront = 15,
        RollBack = 16,
        Guard = 17,
        StruckGuard = 18,
        GuardForward = 19,
        GuardBackward = 20,
        GuardLeft = 21,
        GuardRight = 22,
        GuardForwardLeft = 23,
        GuardForwardRight = 24,
        GuardBackwardLeft = 25,
        GuardBackwardRight = 26,
        ChangeToWeapon1 = 27,
        ChangeToWeapon2 = 28,
        TryHold = 29,
        TurnLeft = 30,
        TurnRight = 31
    }

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
        public const float PositionScale = 100f;

        /// <summary>Monta o 0x030A do bot: origem = <paramref name="seat"/>, posição/rumo do estado da IA.</summary>
        public static byte[] SynthesizeMove(
            byte seat, BotVector position, float heading, uint sequence,
            PlayerActionState state = PlayerActionState.Normal,
            ushort deltaMs = 150)
        {
            byte[] p = new byte[MoveSize];
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(0), MoveType);
            BinaryPrimitives.WriteUInt32LittleEndian(p.AsSpan(2), sequence);
            p[6] = seat;
            BinaryPrimitives.WriteUInt16LittleEndian(p.AsSpan(7), deltaMs);
            p[9] = (byte)(seat | ((byte)state << 5));
            p[10] = 0; // o produtor v258 capturado sempre serializa zero no 0x030A
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
            packet[12] = 0;
            packet[13] = moving ? (byte)1 : (byte)3;
            return packet;
        }

        public const ushort AttackType = 0x0311;
        public const int AttackSize = 10;
        private const byte DamageReactionTerminator = 0x01;

        public static byte[] SynthesizeNormalAnimation(
            byte seat, uint sequence, PlayerNormalAnimation animation)
        {
            byte[] packet = CreateAnimationHeader(seat, sequence);
            packet[8] = (byte)PlayerAnimationKind.Normal;
            packet[9] = (byte)animation;
            return packet;
        }

        public static PlayerNormalAnimation AnimationForControls(BotControls controls)
        {
            if ((controls & BotControls.Space) != 0)
                return PlayerNormalAnimation.Jump;

            BotControls movement = controls &
                (BotControls.W | BotControls.A | BotControls.S | BotControls.D);
            return movement switch
            {
                BotControls.W => PlayerNormalAnimation.MoveForward,
                BotControls.S => PlayerNormalAnimation.MoveBackward,
                BotControls.A => PlayerNormalAnimation.MoveLeft,
                BotControls.D => PlayerNormalAnimation.MoveRight,
                BotControls.W | BotControls.A => PlayerNormalAnimation.MoveForwardLeft,
                BotControls.W | BotControls.D => PlayerNormalAnimation.MoveForwardRight,
                BotControls.S | BotControls.A => PlayerNormalAnimation.MoveBackwardLeft,
                BotControls.S | BotControls.D => PlayerNormalAnimation.MoveBackwardRight,
                _ => PlayerNormalAnimation.Stand
            };
        }

        /// <summary>
        /// Animação de ataque do bot (0x0311, kind=Attack). Os IDs são os que o cliente original
        /// emite ao golpear — medidos no fio de uma partida real (`0x19`, `0x18` e `0x0C` são os
        /// mais frequentes). Animação de ataque é específica de arma/classe: um ID fora do conjunto
        /// que o modelo sabe tocar não desenha nada na tela do outro jogador.
        /// </summary>
        public static byte[] SynthesizeAttack(
            byte seat, uint sequence, BotAttackVariant variant = BotAttackVariant.VariantA)
        {
            byte[] p = CreateAnimationHeader(seat, sequence);
            p[8] = 1;   // kind = Attack
            p[9] = variant switch
            {
                BotAttackVariant.VariantA => 0x19,
                BotAttackVariant.VariantB => 0x18,
                _ => 0x0c
            };
            return p;
        }

        /// <summary>
        /// Reação visual de dano do bot (`0x0311 kind=Damage`, shape estendido de 12 bytes). A
        /// vítima publica a reação sobre SI MESMA, pareada com o `RemainHP` no mesmo instante — é
        /// assim que o cliente original desenha a queda de um jogador remoto. Os dois IDs e o
        /// terminador saem da captura humano×humano; o terminador é `01` em todos os frames
        /// medidos, nunca o assento de quem golpeou.
        /// </summary>
        public static byte[] SynthesizeDamage(
            byte seat, uint sequence, BotDamageReaction reaction)
        {
            byte[] packet = new byte[GameplayActionDatagram.ExtendedAnimationSize];
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0), AttackType);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(2), sequence);
            packet[6] = seat;
            packet[7] = seat;
            packet[8] = (byte)PlayerAnimationKind.Damage;
            (packet[9], packet[10]) = reaction switch
            {
                BotDamageReaction.StaggerA => ((byte)0x01, (byte)0x02),
                BotDamageReaction.StaggerB => ((byte)0x02, (byte)0x01),
                _ => ((byte)0x0f, (byte)0x07)
            };
            packet[11] = DamageReactionTerminator;
            return packet;
        }

        private static byte[] CreateAnimationHeader(byte seat, uint sequence)
        {
            byte[] packet = new byte[AttackSize];
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(0), AttackType);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(2), sequence);
            packet[6] = seat;
            packet[7] = seat;
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
                FromWire(packet[11..]),
                FromWire(packet[13..]),
                FromWire(packet[15..]));
            short degrees = BinaryPrimitives.ReadInt16LittleEndian(packet[17..]);
            heading = NormalizeRadians(degrees * MathF.PI / 180f + MathF.PI);
            return true;
        }

        /// <summary>Extrai apenas a posição do 0x030A.</summary>
        public static bool TryReadPosition(ReadOnlySpan<byte> packet, out BotVector position)
        {
            return TryReadPose(packet, out position, out _);
        }

        private static short ToWire(float value) =>
            (short)Math.Clamp(
                MathF.Round(value * PositionScale),
                short.MinValue,
                short.MaxValue);

        private static float FromWire(ReadOnlySpan<byte> value) =>
            BinaryPrimitives.ReadInt16LittleEndian(value) / PositionScale;

        private static short HeadingToWire(float radians)
        {
            float degrees = (radians + MathF.PI) * 180f / MathF.PI;
            while (degrees > 180f) degrees -= 360f;
            while (degrees < -180f) degrees += 360f;
            return (short)MathF.Round(degrees);
        }

        private static float NormalizeRadians(float radians)
        {
            while (radians > MathF.PI) radians -= 2f * MathF.PI;
            while (radians < -MathF.PI) radians += 2f * MathF.PI;
            return radians;
        }
    }
}
