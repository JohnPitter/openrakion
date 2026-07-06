using System;
using RakionServer.World.Domain;
using RakionServer.World.Network;
using Xunit;

namespace RakionServer.World.Tests
{
    /// <summary>
    /// Trava byte-a-byte os datagramas de gameplay UDP do bot (move+ação 0x30a, keystate 0x030f, golpe
    /// 0x0311) DECODIFICADOS da captura real (udp_gameplay_decode.out.txt §2/§3/§4): corpo 0x30a de 19B com
    /// dt=100/actState=0x0020|seat, posições packed por SCALE=0.01 (coord×100), heading em graus, subFrame
    /// nonce; origem = seat do dono no srcSlot do header E nos bits baixos do actState (captura: seat 0x00→
    /// actState 0x0020, seat 0x0a→0x002a). É a fonte da SÍNTESE do movimento do bot (nunca relay).
    /// </summary>
    public class BotMovementTests
    {
        [Fact]
        public void EncodeActionBody_FixesDecodedLayout()
        {
            var bot = new BotPlayer(1, "Rok", level: 5, charClass: 1, team: 1)
            { X = 1.5f, Y = 2.0f, Z = -3.0f, Yaw = 90f, AimX = 0f, AimY = 0f, AimZ = 0f };

            byte[] b = BotMovement.EncodeActionBody(bot, seat: 0x0a, subFrame: 0xA5, moving: true);

            Assert.Equal(19, b.Length);
            Assert.Equal(100, BitConverter.ToUInt16(b, 0));         // +00 [u16 dt] = 100 (NÃO 0)
            Assert.Equal(0x002a, BitConverter.ToUInt16(b, 2));      // +02 [u16 actState] = 0x0020|seat(0x0a)

            // PARADO: o bit 0x20 (andando) desliga; sobra o seat puro — captura só tem 4 valores de actState
            // (0x2a/0x0a joiner, 0x20/0x00 host). Bit fixo = avatar "anda parado"/desliza.
            byte[] idle = BotMovement.EncodeActionBody(bot, seat: 0x0a, subFrame: 0xA5, moving: false);
            Assert.Equal(0x000a, BitConverter.ToUInt16(idle, 2));
            Assert.Equal((short)150, BitConverter.ToInt16(b, 4));   // +04 x = 1.5 / 0.01
            Assert.Equal((short)200, BitConverter.ToInt16(b, 6));   // +06 y = 2.0 / 0.01
            Assert.Equal((short)-300, BitConverter.ToInt16(b, 8));  // +08 z = -3.0 / 0.01
            Assert.Equal((short)-90, BitConverter.ToInt16(b, 10));  // +0a heading = Yaw(90)+180 -> -90 (convenção do wire encara o alvo)
            Assert.Equal(0xA5, b[12]);                              // +0c subFrame nonce
            Assert.Equal((short)0, BitConverter.ToInt16(b, 13));    // +0d aim x
            Assert.Equal((short)0, BitConverter.ToInt16(b, 15));    // +0f aim y
            Assert.Equal((short)0, BitConverter.ToInt16(b, 17));    // +11 aim z
        }

        [Fact]
        public void EncodeActionBody_NormalizesHeadingToSignedDegrees()
        {
            var bot = new BotPlayer(2, "Ares", 5, 1, team: 0) { Yaw = 270f };
            byte[] b = BotMovement.EncodeActionBody(bot, seat: 0x0a, subFrame: 0, moving: true);
            Assert.Equal((short)90, BitConverter.ToInt16(b, 10));   // (270+180)=450 -> 90 (faixa [-180..180])
        }

        [Fact]
        public void ActionDatagram_IsKnown_AndWrapsBody()
        {
            var bot = new BotPlayer(3, "Vyl", 5, 1, team: 1);
            Assert.True(BotMovement.UdpFramingKnown);

            byte[] d = BotMovement.BuildActionDatagram(bot, seat: 0x0a, moving: true);
            Assert.Equal(26, d.Length);                  // [u16 type][u32 seq][u8 srcSlot][19B corpo]
            Assert.Equal(0x0a, d[0]); Assert.Equal(0x03, d[1]);  // msgType 0x030a (little-endian)
            Assert.Equal(0x0a, d[6]);                    // srcSlot = seat do bot no header
            Assert.Equal(0x002a, BitConverter.ToUInt16(d, 9));   // actState do corpo (+02) ECOA o mesmo slot

            // outro seat: srcSlot E actState acompanham (captura: srcSlot=0x00 -> actState=0x0020)
            byte[] red = BotMovement.BuildActionDatagram(bot, seat: 0x00, moving: true);
            Assert.Equal(0x00, red[6]);
            Assert.Equal(0x0020, BitConverter.ToUInt16(red, 9));
        }

        [Fact]
        public void PeerRegister_0x319_LocksWireLayout()
        {
            // O 0x319 destrava o gate de movimento: o cliente grava playerRec[slot].addr/port = origem do
            // datagrama (engine.dll @0x361005e5). Layout lido pelo handler: opcode@0 + slot@7.
            byte[] d = BotMovement.BuildPeerRegister(seat: 0x0a, seq: 7);
            Assert.Equal(8, d.Length);
            Assert.Equal(0x19, d[0]); Assert.Equal(0x03, d[1]);     // msgType 0x0319 (LE) — unreliable (bit 0x8000 limpo)
            Assert.Equal(7u, BitConverter.ToUInt32(d, 2));          // +02 seq (ignorado pelo handler)
            Assert.Equal(0x0a, d[6]);                               // +06 slot (eco)
            Assert.Equal(0x0a, d[7]);                               // +07 slot LIDO ([esp+0x3f]) -> playerRec[slot]

            byte[] red = BotMovement.BuildPeerRegister(seat: 0x00, seq: 0);
            Assert.Equal(0x00, red[7]);                             // slot acompanha o seat
        }

        [Fact]
        public void KeystateDatagram_LocomotionStateMachine()
        {
            // Máquina de estados do tail do 0x30f (captura): parado (03,00) — os frames de abertura da partida;
            // andando (01,00) — janelas de corrida; golpe recente (00, high-byte do 0x0311) — janelas de ataque.
            var bot = new BotPlayer(4, "Kor", 5, 1, team: 1);
            const long now = 100_000;

            byte[] idle = BotMovement.BuildKeystateDatagram(bot, seat: 0x0a, moving: false, nowMs: now);
            Assert.Equal(14, idle.Length);
            Assert.Equal(0x0f, idle[0]); Assert.Equal(0x03, idle[1]);         // msgType 0x030f
            Assert.Equal(0x0a, idle[6]); Assert.Equal(0x0a, idle[7]);         // srcSlot E srcSlotEcho = seat
            Assert.Equal(0x03, idle[12]); Assert.Equal(0x00, idle[13]);       // (03,00) parado

            byte[] run = BotMovement.BuildKeystateDatagram(bot, seat: 0x0a, moving: true, nowMs: now);
            Assert.Equal(0x01, run[12]); Assert.Equal(0x00, run[13]);         // (01,00) andando

            bot.LastActionHigh = 0x0c;                                        // golpeou com action 0x0C00
            bot.LastActionMs = now;
            byte[] swing = BotMovement.BuildKeystateDatagram(bot, seat: 0x0a, moving: true, nowMs: now + 100);
            Assert.Equal(0x00, swing[12]); Assert.Equal(0x0c, swing[13]);     // (00,0c) — o golpe anima

            byte[] after = BotMovement.BuildKeystateDatagram(bot, seat: 0x0a, moving: true,
                nowMs: now + BotMovement.SwingHoldMs + 1);
            Assert.Equal(0x01, after[12]); Assert.Equal(0x00, after[13]);     // swing expirou -> locomoção
        }

        [Fact]
        public void AliveFlagDatagram_ReproducesCapturedAnnounce()
        {
            // GOLDEN p2p-handshake-groundtruth l.285 (joiner seat 0x0a anuncia a si com flag=1, seq 0x81):
            // 0c83810000000a0a010a002a0091010400000001000000
            var bot = new BotPlayer(7, "Gav", 5, 1, team: 1);
            bot.UdpSeq = 0x81;
            byte[] up = BotMovement.BuildAliveFlagDatagram(bot, seat: 0x0a, alive: true);
            Assert.Equal("0C83810000000A0A010A002A0091010400000001000000", Convert.ToHexString(up));

            // morte: payload 0 (round end na captura, ex. l.2389)
            byte[] down = BotMovement.BuildAliveFlagDatagram(bot, seat: 0x0a, alive: false);
            Assert.Equal(23, down.Length);
            Assert.Equal(0u, BitConverter.ToUInt32(down, 19));
        }

        [Fact]
        public void AttackDatagram_CarriesActionId()
        {
            var bot = new BotPlayer(5, "Zed", 5, 1, team: 1);
            byte[] a = BotMovement.BuildAttackDatagram(bot, seat: 0x0a, actionId: 0x0001);

            Assert.Equal(10, a.Length);                  // [u16 type][u32 seq][u8 srcSlot][u8 sub][u16 actionId]
            Assert.Equal(0x11, a[0]); Assert.Equal(0x03, a[1]);     // msgType 0x0311
            Assert.Equal(0x0a, a[6]); Assert.Equal(0x0a, a[7]);     // srcSlot E sub = seat do bot
            Assert.Equal(0x0001, BitConverter.ToUInt16(a, 8));      // actionId no tail
        }

        [Fact]
        public void Datagrams_ShareMonotonicSeqPerBot()
        {
            var bot = new BotPlayer(6, "Nyx", 5, 1, team: 1);
            byte[] a = BotMovement.BuildActionDatagram(bot, 0x0a, true);              // seq N
            byte[] b = BotMovement.BuildKeystateDatagram(bot, 0x0a, true, 1000);      // seq N+1
            byte[] c = BotMovement.BuildAttackDatagram(bot, 0x0a, 1);         // seq N+2
            uint s0 = BitConverter.ToUInt32(a, 2);
            Assert.Equal(s0 + 1, BitConverter.ToUInt32(b, 2));   // contador único, ++ por pacote do sender
            Assert.Equal(s0 + 2, BitConverter.ToUInt32(c, 2));
        }

        // ---- NPC (0x307 CreateNpc / 0x30b move): entidade de colisão real, fora do muro do type-7.
        //      Estrutura GOLDEN cravada de 6 invocações REAIS de Cell (0x8307 reliable, log worldserver
        //      2026-07-01): header 28B + blob 15B (HP cur/max + tail constante).

        [Fact]
        public void EncodeCreateNpcBody_RealGoldenStructure_43Bytes()
        {
            // owner=3 = seat do dono (chave owner*9+sub + time herdado). Blob real = HP cur/max + tail.
            var bot = new BotPlayer(1, "Rok", 5, 1, team: 1) { X = 1.5f, Y = 2.0f, Z = -3.0f, Yaw = 90f };
            byte[] b = BotMovement.EncodeCreateNpcBody(bot, owner: 3, sub: 2, classId: BotMovement.NpcClassCrossBow);

            Assert.Equal(43, b.Length);                            // 28 header + 15 blob
            Assert.Equal(3, b[0]);                                 // +00 owner (seat do dono)
            Assert.Equal(2, b[1]);                                 // +01 sub
            Assert.Equal(BotMovement.NpcClassCrossBow, BitConverter.ToUInt16(b, 2));     // +02 classId
            Assert.Equal(1.5f, BitConverter.ToSingle(b, 4));       // +04 pos.x
            Assert.Equal(-3.0f, BitConverter.ToSingle(b, 12));     // +0c pos.z
            Assert.Equal((float)bot.MaxHp, BitConverter.ToSingle(b, 28)); // +1c HP cur
            Assert.Equal((float)bot.MaxHp, BitConverter.ToSingle(b, 32)); // +20 HP max
            // +24 tail CONSTANTE 01 00 01 00 00 00 00 (da captura real)
            Assert.Equal(1, b[36]); Assert.Equal(0, b[37]); Assert.Equal(1, b[38]);
            Assert.Equal(0, b[39]); Assert.Equal(0, b[40]); Assert.Equal(0, b[41]); Assert.Equal(0, b[42]);
        }

        [Fact]
        public void EncodeNpcMoveBody_FixesType2Layout_PackedPos()
        {
            var bot = new BotPlayer(1, "Rok", 5, 1, team: 1) { X = 1.5f, Y = 2.0f, Z = -3.0f, Yaw = 90f };
            byte[] b = BotMovement.EncodeNpcMoveBody(bot, owner: 3, sub: 2);

            Assert.Equal(13, b.Length);
            Assert.Equal((short)0, BitConverter.ToInt16(b, 0));    // +00 A (aim/view → entity+0x354; neutro)
            Assert.Equal(BotMovement.NpcMoveType, b[2]);           // +02 type=2 → tabela NPC +0x1d70
            Assert.Equal(3, b[3]);                                 // +03 owner (chave owner*9+sub)
            Assert.Equal(2, b[4]);                                 // +04 sub
            Assert.Equal((short)150, BitConverter.ToInt16(b, 5));  // +05 x = 1.5/0.01 (mesmo packing do 0x30a)
            Assert.Equal((short)200, BitConverter.ToInt16(b, 7));  // +07 y = 2.0/0.01
            Assert.Equal((short)-300, BitConverter.ToInt16(b, 9)); // +09 z = -3.0/0.01
            Assert.Equal((short)90, BitConverter.ToInt16(b, 11));  // +0b heading (graus assinados)
        }

        [Fact]
        public void BuildCreateNpcDatagram_WrapsBody_WithTransportHeader()
        {
            var bot = new BotPlayer(1, "Rok", 5, 1, team: 1) { X = 1.5f, Y = 0f, Z = -3.0f, Yaw = 90f,
                NpcOwner = 0x0a, NpcSub = 0 };
            bot.UdpSeq = 7;
            byte[] d = BotMovement.BuildCreateNpcDatagram(bot, BotMovement.NpcClassPlayer, bot.UdpSeq++, srcSlot: 0x0a);

            // [u16 0x0307 UNRELIABLE][u32 seq][u8 srcSlot][corpo: 28B header + 15B blob] = 50B. O CORPO é a captura
            // real; só o transporte é unreliable (dispatch por-opcode mascara 0x8000; sem depender do canal reliable).
            Assert.Equal(50, d.Length);
            Assert.Equal(0x07, d[0]); Assert.Equal(0x03, d[1]);    // msgType 0x0307 (LE) — UNRELIABLE (bit 0x8000 limpo)
            Assert.Equal(7u, BitConverter.ToUInt32(d, 2));         // +02 seq
            Assert.Equal(0x0a, d[6]);                              // +06 srcSlot (seat do dono — FALTAVA antes)
            Assert.Equal(0x0a, d[7]);                              // +07 owner (corpo = NpcOwner)
            Assert.Equal(0, d[8]);                                 // +08 sub
            Assert.Equal(BotMovement.NpcClassPlayer, BitConverter.ToUInt16(d, 9)); // +09 classId Player
            Assert.Equal(1.5f, BitConverter.ToSingle(d, 11));      // +0b pos.x (cru)
        }

        [Fact]
        public void BuildNpcMoveDatagram_WrapsBody_OwnerKeyAddressesNpc()
        {
            var bot = new BotPlayer(1, "Rok", 5, 1, team: 1) { X = 1.5f, Y = 0f, Z = -3.0f, Yaw = 90f,
                NpcOwner = 0x0a, NpcSub = 3 };
            bot.UdpSeq = 0;
            byte[] d = BotMovement.BuildNpcMoveDatagram(bot, bot.UdpSeq++);

            Assert.Equal(19, d.Length);                            // [u16 type][u32 seq][13B corpo]
            Assert.Equal(0x0b, d[0]); Assert.Equal(0x03, d[1]);    // msgType 0x030b (LE)
            Assert.Equal(BotMovement.NpcMoveType, d[8]);           // corpo+02 (=d[8]) type=2
            Assert.Equal(0x0a, d[9]);                              // corpo+03 owner (chave owner*9+sub)
            Assert.Equal(3, d[10]);                                // corpo+04 sub
            Assert.Equal((short)150, BitConverter.ToInt16(d, 11)); // corpo+05 x = 1.5/0.01
        }

        // ---- SPAWN no STAGE: golden byte-a-byte contra a captura MITM real (stage_capture.txt). O frame
        //      on-wire é [u16 0x4b] + o corpo do builder; os 2 W->C 0x4b da captura (RED seat 0x00, BLUE seat
        //      0x0a) só diferem no byte de seat. Guarda da lição-mestra (só mandar forma que o cliente já viu).
        private const string CapturedSpawn4bSeat0a =
            "4b000a4300080000c2420000c24200000000000000001f00000001000000000000c2420000c24248e17a3f5a000000640000006e000000640000000f000000000000000000000000";

        [Fact]
        public void BuildStageAddPlayer_ReproducesCapturedBlob()
        {
            var bot = new BotPlayer(1, "Rok", 5, 1, team: 1);
            byte[] body = BotMovement.BuildStageAddPlayer(bot, seat: 0x0a);
            // a captura é o frame inteiro [u16 0x4b]+corpo; o builder devolve o corpo (sem o opcode 4b00).
            Assert.Equal(70, body.Length);                       // [u8 seat][u16 67][blob 67]
            Assert.Equal(CapturedSpawn4bSeat0a.Substring(4), Convert.ToHexString(body).ToLowerInvariant());
        }

        [Fact]
        public void BuildStageAddPlayer_OnlySeatByteVariesBetweenPlayers()
        {
            var bot = new BotPlayer(1, "Rok", 5, 1, team: 0);
            byte[] red = BotMovement.BuildStageAddPlayer(bot, seat: 0x00);
            byte[] blue = BotMovement.BuildStageAddPlayer(bot, seat: 0x0a);
            Assert.Equal(0x00, red[0]);
            Assert.Equal(0x0a, blue[0]);
            Assert.Equal(blue[1..], red[1..]);                   // blob idêntico — só o seat distingue (= captura)
        }

        [Fact]
        public void BuildFieldGameStart_ReproducesCapturedRoundLoad()
        {
            // 0x48 da captura = 4800 012f0100001414000000; o builder devolve o corpo (sem o opcode 4800).
            byte[] body = BotMovement.BuildFieldGameStart();
            Assert.Equal("012f0100001414000000", Convert.ToHexString(body).ToLowerInvariant());
        }
    }
}
