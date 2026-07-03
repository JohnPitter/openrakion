using System;
using System.Collections.Generic;
using System.Buffers.Binary;
using RakionServer.Peer;
using Xunit;

namespace RakionServer.World.Tests
{
    /// <summary>
    /// Golden tests P4/P5/P6 do mini-peer (RakionServer.Peer): round-trip do stream reliable (fragmentação por
    /// MTU + ACK por offset + reassembly em ordem), o CRC32 da SE1 byte-a-byte (poly 0xEDB88320, AddLONG
    /// big-endian, init/XOR 0xFFFFFFFF), e a SEQUÊNCIA do handshake Start_AtClient_t (S0..S10 + SEQ_ADDPLAYER).
    /// Síntese do estado/fonte SE1 (nunca replay): o payload reliable é OFF-WIRE (não há captura possível), a
    /// fonte é a spec — ver minipeer_blueprint §6/M1.
    /// </summary>
    public class PeerStreamTests
    {
        // ---------- P4: ReliableStream round-trip (dois peers cruzados em loopback) ----------

        /// <summary>Cruza A↔B: o 0x0304 de A entra em B (que ACKa) e o 0x0305 de B avança o ACK de A. Coleta as
        /// CNetworkMessages que B remonta. Verifica ordem, completude e fragmentação por MTU.</summary>
        [Fact]
        public void ReliableStream_RoundTrips_MultipleMessages_InOrder()
        {
            ReliableStream a = null!, b = null!;
            var received = new List<NetMessage>();

            a = new ReliableStream(pkt =>
            {
                // A -> host(B): 0x0304 push (B remonta + devolve ACK) / qualquer ACK de A->B é ignorado aqui.
                if (WireFrames.MsgTypeOf(pkt) == WireFrames.MsgPushReliable)
                    received.AddRange(b.OnPush(pkt));
            });
            b = new ReliableStream(pkt =>
            {
                // B -> A: só o ACK 0x0305 importa p/ avançar a janela de A.
                if (WireFrames.MsgTypeOf(pkt) == WireFrames.MsgAckReliable &&
                    WireFrames.TryReadReliable(pkt, out var h, out _))
                    a.OnAck(h.Offset);
            });

            byte[] m1 = Msg(NetworkMessageType.ReqCrcList, 0);          // pequena (1B corpo)
            byte[] m2 = Msg(NetworkMessageType.SeqAddPlayer, 50);       // média
            byte[] m3 = Msg(NetworkMessageType.GameStreamBlocks, 3000); // > MTU (1024) -> fragmenta em vários 0x0304

            a.SendMessage(m1);
            a.SendMessage(m2);
            a.SendMessage(m3);

            Assert.Equal(3, received.Count);
            Assert.Equal(m1, received[0].Body.ToArray());
            Assert.Equal(m2, received[1].Body.ToArray());
            Assert.Equal(m3, received[2].Body.ToArray());
            // ACK cumulativo chegou ao fim do byte-stream (todas as msgs confirmadas).
            Assert.Equal(a.SentOffset, a.AckedOffset);
            Assert.True(a.SentOffset > 3000);   // m3 sozinha já passa do MTU
        }

        /// <summary>Mensagem partida em 2 datagramas fora de ordem (offset maior chega antes): o reassembly
        /// segura o segundo até o primeiro, e só então surfa a CNetworkMessage completa.</summary>
        [Fact]
        public void ReliableStream_Reassembles_OutOfOrderSegments()
        {
            var pushes = new List<byte[]>();
            var src = new ReliableStream(pushes.Add);   // captura os 0x0304 sem entregar
            byte[] big = Msg(NetworkMessageType.RepStateDelta, 1500);  // 1 prefixo + 1500 -> 2 frames (MTU 1024)
            src.SendMessage(big);
            Assert.True(pushes.Count >= 2);

            var sink = new ReliableStream(_ => { });    // ACKs descartados
            var got = new List<NetMessage>();
            // entrega o SEGUNDO frame primeiro (offset maior): deve segurar; nada surfa ainda.
            got.AddRange(sink.OnPush(pushes[1]));
            Assert.Empty(got);
            // agora o primeiro (offset 0): completa o prefixo + corpo -> 1 mensagem.
            got.AddRange(sink.OnPush(pushes[0]));
            Assert.Single(got);
            Assert.Equal(big, got[0].Body.ToArray());
        }

        /// <summary>OnPush a um 0x0304 devolve um 0x0305 que é o ECO VERBATIM do frame com só o msgType trocado
        /// 0x0304→0x0305 (cravado da captura real: seq/role/sub/token/cauda preservados byte-a-byte).</summary>
        [Fact]
        public void ReliableStream_OnPush_EmitsVerbatimEchoAck()
        {
            byte[] payload = { 1, 2, 3, 4 };
            byte[] push = WireFrames.BuildPush(seq: 7, role: WireFrames.RolePeerStream, sub: 0x0a, offset: 0x100, payload);

            byte[]? ack = null;
            var s = new ReliableStream(pkt => { if (WireFrames.MsgTypeOf(pkt) == WireFrames.MsgAckReliable) ack = pkt; });
            s.OnPush(push);

            Assert.NotNull(ack);
            Assert.Equal(WireFrames.MsgAckReliable, WireFrames.MsgTypeOf(ack));   // 0x0305
            Assert.Equal(push.Length, ack!.Length);                              // mesmo tamanho (eco)
            Assert.True(WireFrames.TryReadReliable(ack, out var h, out _));
            Assert.Equal(0x100u, h.Offset);                                      // offset ECOADO (start), não start+len
            Assert.Equal(WireFrames.RolePeerStream, ack[WireFrames.RoleOffset]); // role preservado (NÃO zerado)
            Assert.Equal(0x0a, ack[WireFrames.SubOffset]);                       // sub preservado
            // o eco difere do push só no byte 0 (0x04→0x05):
            byte[] expected = (byte[])push.Clone();
            expected[0] = 0x05;
            Assert.Equal(expected, ack);
        }

        // ---------- P4: WireFrames L1 ----------

        [Fact]
        public void WireFrames_Push_LayoutAndTokenOffset()
        {
            byte[] payload = { 0xAA, 0xBB };
            byte[] f = WireFrames.BuildPush(seq: 0x11223344, role: 0x0a, sub: 0x00, offset: 0x100, payload);

            Assert.Equal(WireFrames.MsgPushReliable, BinaryPrimitives.ReadUInt16LittleEndian(f));      // 0x0304
            Assert.Equal(0x11223344u, BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(2)));            // seq
            Assert.Equal(0x0a, f[WireFrames.RoleOffset]);
            uint token = BinaryPrimitives.ReadUInt32LittleEndian(f.AsSpan(WireFrames.TokenOffset));
            Assert.Equal(0x100u, token);                                  // token É o offset CRU (sem marcador 0xc0)
            Assert.Equal(0x100u, WireFrames.OffsetFromToken(token));      // round-trip
            Assert.Equal(payload, f.AsSpan(WireFrames.PayloadOffset).ToArray());
        }

        /// <summary>Golden contra a CAPTURA real (stage_udp_capture.txt): o engine ACKa um 0x0304 de 13B ecoando
        /// o frame inteiro com só o msgType trocado 0x0304→0x0305 (push 040305…→ ack 050305…). E o 13º byte é uma
        /// CAUDA de controle (sub-stream), não payload de mensagem.</summary>
        [Fact]
        public void WireFrames_Ack_MatchesCaptureVerbatim_AndTailIsNotPayload()
        {
            byte[] push = Convert.FromHexString("040305000000000a7c2ec00000");   // W->C real (13B)
            byte[] expectedAck = Convert.FromHexString("050305000000000a7c2ec00000"); // C->W real (13B)

            byte[]? ack = WireFrames.BuildAck(push);
            Assert.NotNull(ack);
            Assert.Equal(expectedAck, ack);                                      // eco verbatim, só o byte 0 muda

            // o frame de 13B do engine NÃO carrega payload de mensagem (o 13º byte é cauda 0x00) -> payload vazio.
            Assert.True(WireFrames.TryReadReliable(push, out var h, out var payload));
            Assert.Equal(0u, (uint)payload.Length);
            Assert.Equal((byte)0x00, h.Role);
            Assert.Equal((byte)0x0a, h.Sub);
        }

        [Fact]
        public void WireFrames_KeepAlive_Is7Bytes()
        {
            byte[] k = WireFrames.BuildKeepAlive(0x00000005);
            Assert.Equal(7, k.Length);
            Assert.Equal(WireFrames.MsgKeepAlive, BinaryPrimitives.ReadUInt16LittleEndian(k));   // 0x030d
            Assert.Equal(0x03, k[2]);
            Assert.Equal(5u, BinaryPrimitives.ReadUInt32LittleEndian(k.AsSpan(3)));
        }

        [Fact]
        public void WireFrames_IsReliableFrame_DiscriminatesByType()
        {
            Assert.True(WireFrames.IsReliableFrame(new byte[] { 0x04, 0x03 }));   // 0x0304
            Assert.True(WireFrames.IsReliableFrame(new byte[] { 0x05, 0x03 }));   // 0x0305
            Assert.True(WireFrames.IsReliableFrame(new byte[] { 0x19, 0x03 }));   // 0x0319
            Assert.False(WireFrames.IsReliableFrame(new byte[] { 0x0a, 0x03 }));  // 0x030a = gameplay, não reliable
            Assert.False(WireFrames.IsReliableFrame(new byte[] { 0x04 }));        // curto
        }

        // ---------- P5: CrcEngine (CRC32 da SE1 byte-a-byte) ----------

        /// <summary>O CRC32 de bytes da SE1 é o CRC-32 padrão (poly 0xEDB88320). Vetor canônico "123456789" =
        /// 0xCBF43926.</summary>
        [Fact]
        public void CrcEngine_OfBytes_MatchesStandardCrc32Vector()
        {
            byte[] data = System.Text.Encoding.ASCII.GetBytes("123456789");
            Assert.Equal(0xCBF43926u, CrcEngine.OfBytes(data));
        }

        /// <summary>CRC_AddLONG alimenta os 4 bytes em BIG-ENDIAN (fonte CRC.h). Verifica contra a composição
        /// manual byte-a-byte (Start -> 4×AddByte MSB-first -> Finish).</summary>
        [Fact]
        public void CrcEngine_AddLong_IsBigEndian()
        {
            uint crc = CrcEngine.Start;
            crc = CrcEngine.AddLong(crc, 0x01020304);
            uint expected = CrcEngine.Start;
            expected = CrcEngine.AddByte(expected, 0x01);
            expected = CrcEngine.AddByte(expected, 0x02);
            expected = CrcEngine.AddByte(expected, 0x03);
            expected = CrcEngine.AddByte(expected, 0x04);
            Assert.Equal(expected, crc);
        }

        /// <summary>CRCT_MakeCRCForFiles_t combina os CRCs por-arquivo na ordem da lista (Start -> AddLONG por
        /// arquivo -> Finish). A ordem importa (AddLONG não é comutativo).</summary>
        [Fact]
        public void CrcEngine_CombineFileList_IsOrderSensitive_AndDeterministic()
        {
            var files1 = new[] { "a.bin", "b.bin" };
            var files2 = new[] { "b.bin", "a.bin" };
            uint Crc(string n) => n == "a.bin" ? 0x11111111u : 0x22222222u;

            uint c1 = CrcEngine.CombineFileList(files1, Crc);
            uint c2 = CrcEngine.CombineFileList(files2, Crc);
            Assert.NotEqual(c1, c2);                                  // ordem sensível (AddLONG)
            Assert.Equal(c1, CrcEngine.CombineFileList(files1, Crc)); // determinístico

            // composição manual = Finish(AddLONG(AddLONG(Start, crcA), crcB))
            uint man = CrcEngine.Finish(CrcEngine.AddLong(CrcEngine.AddLong(CrcEngine.Start, 0x11111111), 0x22222222));
            Assert.Equal(man, c1);
        }

        // ---------- P5: SessionHandshake (Start_AtClient_t — sequência S0..S10) ----------

        [Fact]
        public void Handshake_DrivesFullSequence_ToEstablished_SendingConnectPlayer()
        {
            var reliableOut = new List<NetMessage>();
            int keepAlives = 0;
            var gs = new GameStream();
            var character = BotCharacterFactory.ForBot("Rok", charClass: 1, level: 7, team: 0);
            var identity = new PeerIdentity(character, PlayerIndex: 10);
            uint FileCrc(string _) => 0x12345678;

            var hs = new SessionHandshake(
                sendReliable: m => reliableOut.Add(NetMessage.From(m)),
                sendKeepAlive: () => keepAlives++,
                gameStream: gs,
                identity: identity,
                connect: SessionHandshake.ConnectParams.WithCrc(""),   // exercita o caminho COM CRC
                fileCrcOf: FileCrc);

            // S0+S1: keepalive + REQ_CONNECTREMOTE(7).
            hs.Begin();
            Assert.Equal(1, keepAlives);
            Assert.Single(reliableOut);
            Assert.Equal(NetworkMessageType.ReqConnectRemoteSessionState, reliableOut[0].Type);
            Assert.Equal(SessionHandshake.State.WaitConnectReply, hs.Current);

            // host -> REP_CONNECTREMOTE(8): [MOTD][world][flags][props]. O peer drena e manda S3+S4.
            hs.OnMessage(NetMessage.From(BuildConnectReply()));
            Assert.Equal(2, keepAlives);                                   // S3
            Assert.Equal(NetworkMessageType.ReqStateDelta, reliableOut[1].Type);  // S4 (type 9)
            Assert.Equal(SessionHandshake.State.WaitStateDelta, hs.Current);

            // host -> REP_STATEDELTA(10): blob (não-zlib aqui; o peer drena/descarta). S6+S7.
            hs.OnMessage(NetMessage.From(BuildStateDelta()));
            Assert.Equal(3, keepAlives);                                   // S6
            Assert.Equal(NetworkMessageType.ReqCrcList, reliableOut[2].Type);     // S7 (type 11)
            Assert.Equal(SessionHandshake.State.WaitCrcChallenge, hs.Current);

            // host -> REQ_CRCCHECK(12): 2 arquivos. O peer responde REP_CRCCHECK(13) + S10 + REQ_CONNECTPLAYER(14).
            var names = new[] { "Bin/Engine.dll", "Data/world.wld" };
            hs.OnMessage(NetMessage.From(BuildCrcChallenge(names)));
            Assert.Equal(4, keepAlives);                                   // S10

            // S9: REP_CRCCHECK com o CRC combinado dos arquivos + lastSeq (=-1, nada processado).
            var crcReply = reliableOut[3];
            Assert.Equal(NetworkMessageType.RepCrcCheck, crcReply.Type);
            var rr = crcReply.BeginRead();
            uint expectedCrc = CrcEngine.CombineFileList(names, FileCrc);
            Assert.Equal(expectedCrc, rr.ReadU32());
            Assert.Equal(-1, rr.ReadI32());                               // ses_iLastProcessedSequence

            // S10: REQ_CONNECTPLAYER(14) reliable com o CPlayerCharacter — o HOST é quem gera o SEQ_ADDPLAYER.
            var connectPlayer = reliableOut[4];
            Assert.Equal(NetworkMessageType.ReqConnectPlayer, connectPlayer.Type);
            var pr = connectPlayer.BeginRead();
            var pc = PlayerCharacter.ReadFrom(pr);
            Assert.False(pr.Overflowed);                                   // corpo completo (sem over-read)
            Assert.Equal("Rok", pc.Name);
            Assert.Equal("0", pc.Team);
            Assert.Equal(PlayerCharacter.GuidSize, pc.Guid.Length);
            Assert.Equal(PlayerCharacter.AppearanceSize, pc.Appearance.Length);
            Assert.Equal(SessionHandshake.State.WaitPlayerReply, hs.Current);

            // host -> REP_CONNECTPLAYER(15): [INDEX iPlayer]. O bot está registrado; o gate do 0x30a abre.
            hs.OnMessage(NetMessage.From(BuildConnectPlayerReply(10)));
            Assert.Equal(10, hs.AssignedPlayerIndex);
            Assert.Equal(SessionHandshake.State.Established, hs.Current);
            Assert.True(hs.IsEstablished);
        }

        [Fact]
        public void Handshake_SkipCrc_GoesDirectToConnectPlayer()
        {
            var reliableOut = new List<NetMessage>();
            var hs = new SessionHandshake(
                sendReliable: m => reliableOut.Add(NetMessage.From(m)),
                sendKeepAlive: () => { },
                gameStream: new GameStream(),
                identity: new PeerIdentity(BotCharacterFactory.ForBot("Rok", 1, 7, 0), 10),
                connect: SessionHandshake.ConnectParams.Default(""),   // RequestCrc=false (default do bot)
                fileCrcOf: _ => 0);

            hs.Begin();                                            // S1: REQ_CONNECTREMOTE
            hs.OnMessage(NetMessage.From(BuildConnectReply()));    // S4: REQ_STATEDELTA
            hs.OnMessage(NetMessage.From(BuildStateDelta()));      // pula CRC -> REQ_CONNECTPLAYER direto

            Assert.Equal(3, reliableOut.Count);                    // CONNECTREMOTE, STATEDELTA, CONNECTPLAYER
            Assert.Equal(NetworkMessageType.ReqConnectPlayer, reliableOut[2].Type);
            Assert.Equal(SessionHandshake.State.WaitPlayerReply, hs.Current);

            hs.OnMessage(NetMessage.From(BuildConnectPlayerReply(10)));
            Assert.Equal(10, hs.AssignedPlayerIndex);
            Assert.True(hs.IsEstablished);
        }

        [Fact]
        public void Handshake_Disconnected_GoesToFailed()
        {
            var hs = new SessionHandshake(
                sendReliable: _ => { }, sendKeepAlive: () => { }, gameStream: new GameStream(),
                identity: new PeerIdentity(BotCharacterFactory.ForBot("x", 0, 0, 0), 0),
                connect: SessionHandshake.ConnectParams.Default(""), fileCrcOf: _ => 0);
            hs.Begin();
            hs.OnMessage(NetMessage.From(new byte[] { (byte)NetworkMessageType.InfDisconnected }));
            Assert.Equal(SessionHandshake.State.Failed, hs.Current);
        }

        // ---------- P6: BotPeer (integração das camadas) ----------

        /// <summary>O BotPeer inicia o handshake (emite keepalive + REQ_CONNECTREMOTE no 1º datagrama reliable)
        /// e acende GateOpen quando o host emite gameplay (0x030a).</summary>
        [Fact]
        public void BotPeer_Connect_EmitsHandshake_AndGateOpensOnHostGameplay()
        {
            var sent = new List<byte[]>();
            var peer = new BotPeer(
                send: sent.Add,
                identity: new PeerIdentity(BotCharacterFactory.ForBot("Rok", 1, 7, 0), 10),
                modName: "",
                fileCrcOf: _ => 0x12345678);

            peer.Connect();
            // 1º = 0x0304 OPEN role=0xff (abre o canal reliable direto — ground truth da captura); depois
            // keepalive 0x030d (S0) + 0x0304 role=0x0a carregando o REQ_CONNECTREMOTE (S1).
            Assert.Equal(WireFrames.MsgPushReliable, WireFrames.MsgTypeOf(sent[0]));
            Assert.True(WireFrames.TryReadReliable(sent[0], out var openHdr, out _) && openHdr.Role == WireFrames.RoleControl,
                "1º frame é um 0x0304 com role=0xff (open do canal)");
            Assert.Contains(sent, p => WireFrames.MsgTypeOf(p) == WireFrames.MsgKeepAlive);
            Assert.Contains(sent, p => WireFrames.MsgTypeOf(p) == WireFrames.MsgPushReliable);
            Assert.False(peer.GateOpen);

            // host emite gameplay 0x030a -> gate abre (sinal fiel).
            peer.OnDatagram(new byte[] { 0x0a, 0x03, 0, 0, 0, 0 });
            Assert.True(peer.GateOpen);
        }

        /// <summary>
        /// INTEGRAÇÃO end-to-end: dirige o <see cref="BotPeer"/> pelo handshake INTEIRO trocando datagramas
        /// 0x0304/0x0305 REAIS com um "host" fake (uma 2ª <see cref="ReliableStream"/> espelhando o servidor).
        /// Valida o caminho que roda in-game: OnDatagram → ReliableStream.OnPush → DispatchReliable → handshake,
        /// incluindo o PULA-CRC (o host nunca recebe REQ_CRCLIST) e o REQ_CONNECTPLAYER → REP_CONNECTPLAYER.
        /// Pega bugs de FIAÇÃO entre as camadas que os testes por-camada não veem.
        /// </summary>
        [Fact]
        public void BotPeer_FullHandshake_OverWire_ReachesEstablishedAndGate()
        {
            var queue = new Queue<(bool toBot, byte[] pkt)>();
            bool hostGotConnectPlayer = false;

            // host: reliable stream que ENVIA ao bot (enfileira toBot=true)
            var hostRel = new ReliableStream(send: p => queue.Enqueue((true, p)));

            void HostOnMessage(NetMessage msg)
            {
                switch (msg.Type)
                {
                    case NetworkMessageType.ReqConnectRemoteSessionState: hostRel.SendMessage(BuildConnectReply()); break;
                    case NetworkMessageType.ReqStateDelta: hostRel.SendMessage(BuildStateDelta()); break;
                    case NetworkMessageType.ReqCrcList: Assert.Fail("o bot NÃO deveria pedir CRC (pula-CRC)"); break;
                    case NetworkMessageType.ReqConnectPlayer:
                        hostGotConnectPlayer = true;
                        hostRel.SendMessage(BuildConnectPlayerReply(7));
                        break;
                }
            }

            void HostHandle(byte[] pkt)
            {
                ushort t = WireFrames.MsgTypeOf(pkt);
                if (t == WireFrames.MsgPushReliable)
                    foreach (var m in hostRel.OnPush(pkt)) HostOnMessage(m);   // OnPush já ecoa o 0x0305 ao bot
                else if (t == WireFrames.MsgAckReliable && WireFrames.TryReadReliable(pkt, out var ack, out _))
                    hostRel.OnAck(ack.Offset);
                // 0x030d keepalive do bot: ignora
            }

            var bot = new BotPeer(
                send: p => queue.Enqueue((false, p)),   // bot ENVIA ao host (toBot=false)
                identity: new PeerIdentity(BotCharacterFactory.ForBot("Rok", 1, 7, 0), 7),
                modName: "",
                fileCrcOf: _ => 0);

            bot.Connect();
            int guard = 0;
            while (queue.Count > 0 && guard++ < 1000)
            {
                var (toBot, pkt) = queue.Dequeue();
                if (toBot) bot.OnDatagram(pkt);
                else HostHandle(pkt);
            }

            Assert.True(hostGotConnectPlayer, "o host recebeu o REQ_CONNECTPLAYER");
            Assert.Equal(SessionHandshake.State.Established, bot.HandshakeState);

            // host emite gameplay 0x030a -> o gate do movimento abre
            bot.OnDatagram(new byte[] { 0x0a, 0x03, 0, 0, 0, 0 });
            Assert.True(bot.GateOpen);
        }

        // ---------- helpers de síntese (host -> peer) ----------

        private static byte[] Msg(NetworkMessageType type, int extraBytes)
        {
            using var w = NetMessage.BeginWrite(type);
            for (int i = 0; i < extraBytes; i++) w.WriteByte((byte)(i & 0xFF));
            return w.ToArray();
        }

        private static byte[] BuildConnectReply()
        {
            using var w = NetMessage.BeginWrite(NetworkMessageType.RepConnectRemoteSessionState);
            w.WriteCString("MOTD");      // message-of-day
            w.WriteCString("world.wld"); // world (CTFileName)
            w.WriteU32(0);               // spawnFlags
            w.WriteBytes(new byte[2048]);// props
            return w.ToArray();
        }

        private static byte[] BuildStateDelta()
        {
            using var w = NetMessage.BeginWrite(NetworkMessageType.RepStateDelta);
            w.WriteBytes(new byte[] { 1, 2, 3, 4 });   // blob não-zlib: o peer drena e descarta (tolerante)
            return w.ToArray();
        }

        private static byte[] BuildCrcChallenge(string[] names)
        {
            using var w = NetMessage.BeginWrite(NetworkMessageType.ReqCrcCheck);
            w.WriteIndex(names.Length);                // INDEX ctFiles
            foreach (var n in names) w.WriteCString(n);// CTString por arquivo
            return w.ToArray();
        }

        private static byte[] BuildConnectPlayerReply(int idx)
        {
            using var w = NetMessage.BeginWrite(NetworkMessageType.RepConnectPlayer);
            w.WriteIndex(idx);   // INDEX iPlayer atribuído pelo host
            return w.ToArray();
        }
    }
}
