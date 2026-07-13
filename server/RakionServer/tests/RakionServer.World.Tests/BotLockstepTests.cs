using System;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    /// <summary>
    /// Golden tests do codec do lockstep de sessão P2P (<see cref="BotLockstep"/>) contra a captura REAL de
    /// 2 humanos (docs/p2p-handshake-groundtruth.txt) e contra o push real do host ao bot (worldserver.log
    /// 2026-07-06). Cada vetor cita a linha da captura — se um teste quebrar, o fio mudou, não o teste.
    /// Só o lado de RELAY/ACK (humano↔humano) sobrou; os builders open/push do BOT foram removidos 2026-07-10.
    /// </summary>
    public class BotLockstepTests
    {
        [Fact]
        public void Ack_reliable_bate_com_a_captura_humano_ao_bot()
        {
            Assert.Equal("00402C0C00000172080000", Convert.ToHexString(
                BotLockstep.BuildReliableAck(0x0c2c, 0x01, 0x0872)));
        }

        [Fact]
        public void Ack_do_host_ao_push_do_joiner_bate_com_a_captura_l17()
        {
            // l.17: o host (seat 0) ackeia o push da l.16 — bytes 6/7 = seat do ACKER, token+payload ecoados
            byte[] push = Convert.FromHexString("0403070000000A00F927F8020A");
            Assert.Equal("0503070000000000F927F8020A", Convert.ToHexString(BotLockstep.BuildAck(push, 0x00)));
        }

        [Fact]
        public void Ack_do_joiner_ao_push_do_host_bate_com_a_captura_l19()
        {
            // l.18/19: push do host (seat 0 -> 0x0a) ackeado pelo joiner (seat 0x0a) com 0a 0a
            byte[] push = Convert.FromHexString("040307000000000A0228F80200");
            Assert.Equal("0503070000000A0A0228F80200", Convert.ToHexString(BotLockstep.BuildAck(push, 0x0a)));
        }

        [Fact]
        public void Ack_do_host_ao_open_bate_com_a_captura_l13()
        {
            // l.12/13: open (12B, sem payload) ackeado pelo host com 00 00
            byte[] open = Convert.FromHexString("040304000000FF00E706F802");
            Assert.Equal("0503040000000000E706F802", Convert.ToHexString(BotLockstep.BuildAck(open, 0x00)));
        }

        [Fact]
        public void Ack_ao_push_real_do_host_para_o_bot()
        {
            // worldserver.log 2026-07-06 16:00:53: push do host (seat 0) ao BOT (seat 0x0a) no socket do
            // servidor — o frame que o eco-clone antigo ackeava errado (mantinha 00 0A em vez de 0A 0A).
            byte[] hostPush = Convert.FromHexString("04031D000000000A1371201300");
            Assert.True(BotLockstep.IsPush(hostPush));
            Assert.Equal(0x0a, BotLockstep.DstSeat(hostPush));
            Assert.Equal("05031D0000000A0A1371201300", Convert.ToHexString(BotLockstep.BuildAck(hostPush, 0x0a)));
        }
    }
}
