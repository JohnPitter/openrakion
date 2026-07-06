using System;
using RakionServer.Peer;
using Xunit;

namespace RakionServer.World.Tests
{
    /// <summary>
    /// Golden tests do codec de netcode do mini-peer (slice RakionServer.Peer, P1/P2/P3 do blueprint):
    /// round-trip NetReader↔NetWriter de cada tipo, segurança do reader (over-read NUNCA lança), e a
    /// SÍNTESE byte-a-byte do REQ_CONNECTREMOTESESSIONSTATE contra a spec da fonte SE1 × disasm (§4.1).
    /// O corpo é montado do estado/constantes nomeadas (nunca replay de blob — a captura reliable só tem
    /// headers de controle 12-13B; o corpo desreliabilizado não está no fio, ver minipeer_blueprint §6/M1).
    /// </summary>
    public class PeerCodecTests
    {
        // ---------- P1: round-trip tipado NetWriter -> NetReader ----------

        [Fact]
        public void RoundTrip_AllScalarTypes_LittleEndian()
        {
            byte[] buf;
            using (var w = NetMessage.BeginWrite(NetworkMessageType.Action))
            {
                w.WriteByte(0xAB);
                w.WriteU16(0x1234);
                w.WriteU32(0xDEADBEEF);
                w.WriteI16(-2000);
                w.WriteI32(-123456);
                w.WriteIndex(777);
                w.WriteFloat(1.5f);
                buf = w.ToArray();
            }

            var msg = NetMessage.From(buf);
            Assert.Equal(NetworkMessageType.Action, msg.Type);   // 1º byte = tipo

            var r = msg.BeginRead();                              // cursor após o byte de tipo
            Assert.Equal(0xAB, r.ReadByte());
            Assert.Equal(0x1234, r.ReadU16());
            Assert.Equal(0xDEADBEEF, r.ReadU32());
            Assert.Equal((short)-2000, r.ReadI16());
            Assert.Equal(-123456, r.ReadI32());
            Assert.Equal(777, r.ReadIndex());
            Assert.Equal(1.5f, r.ReadFloat());
            Assert.False(r.Overflowed);
            Assert.Equal(0, r.Remaining);
        }

        [Fact]
        public void RoundTrip_CString_IsNulTerminatedNoLengthPrefix()
        {
            byte[] buf;
            using (var w = NetMessage.BeginWrite(NetworkMessageType.ChatIn))
            {
                w.WriteCString("Rakion");
                w.WriteCString("");        // vazia = só o NUL
                buf = w.ToArray();
            }

            // layout: [type][R a k i o n 00][00]
            Assert.Equal((byte)'R', buf[1]);
            Assert.Equal(0x00, buf[7]);    // NUL após "Rakion" (sem prefixo de tamanho)
            Assert.Equal(0x00, buf[8]);    // a string vazia é um único NUL

            var r = NetMessage.From(buf).BeginRead();
            Assert.Equal("Rakion", r.ReadCString());
            Assert.Equal("", r.ReadCString());
            Assert.False(r.Overflowed);
        }

        [Fact]
        public void RoundTrip_SubMessage_SlongSizePrefixedBytes()
        {
            byte[] sub;
            using (var ws = NetMessage.BeginWrite(NetworkMessageType.SeqAddPlayer))
            {
                ws.WriteU16(0xCAFE);
                sub = ws.ToArray();        // corpo do sub-bloco (1º byte = tipo 22)
            }

            byte[] outer;
            using (var w = NetMessage.BeginWrite(NetworkMessageType.GameStreamBlocks))
            {
                w.WriteIndex(42);          // nº de sequência do bloco (CNetworkStreamBlock)
                w.InsertSubMessage(sub);   // [SLONG size][bytes]
                outer = w.ToArray();
            }

            var r = NetMessage.From(outer).BeginRead();
            Assert.Equal(42, r.ReadIndex());
            var got = r.ReadSubMessage().ToArray();
            Assert.Equal(sub, got);        // sub-mensagem idêntica
            Assert.Equal((byte)NetworkMessageType.SeqAddPlayer, got[0]);  // 1º byte do sub = seu tipo
            Assert.False(r.Overflowed);
        }

        // ---------- P1: segurança por construção (CLAUDE.md: frame curto/forjado -> erro tratado) ----------

        [Fact]
        public void Reader_OverRead_DoesNotThrow_SetsOverflowAndZero()
        {
            var r = new NetReader(new byte[] { 0x01, 0x02 });   // só 2 bytes
            Assert.Equal(0x0201, r.ReadU16());
            Assert.False(r.Overflowed);

            uint past = r.ReadU32();                            // pede 4, não há nenhum
            Assert.Equal(0u, past);                             // zera o destino (= memset da fonte)
            Assert.True(r.Overflowed);                          // marca, NÃO lança IndexOutOfRange
            Assert.Equal(0, r.Remaining);
        }

        [Fact]
        public void Reader_ForgedSubMessageSize_DoesNotOverflowBuffer()
        {
            // [SLONG size = 0x7FFFFFFF][1 byte] -> size enorme forjado, só 1 byte disponível
            byte[] forged = { 0xFF, 0xFF, 0xFF, 0x7F, 0xAA };
            var r = new NetReader(forged);
            var sub = r.ReadSubMessage();
            Assert.True(r.Overflowed);
            Assert.Equal(0, sub.Length);                        // fatia vazia, nunca estoura
        }

        [Fact]
        public void Reader_CStringWithoutTerminator_StopsAtEnd()
        {
            var r = new NetReader(new byte[] { (byte)'a', (byte)'b' });   // sem NUL
            Assert.Equal("ab", r.ReadCString());
            Assert.True(r.Overflowed);
        }

        // ---------- P2: REQ_CONNECTREMOTE — síntese byte-a-byte contra a spec (§4.1) ----------

        [Fact]
        public void ConnectRemoteRequest_MatchesSpecLayout()
        {
            byte[] f = ConnectMessages.BuildConnectRemoteRequest(
                modName: "Rakion",
                password: "",
                sockParams: SessionSocketParams.Loopback,
                localPlayers: 1);

            int p = 0;
            Assert.Equal((byte)NetworkMessageType.ReqConnectRemoteSessionState, f[p]); p += 1;  // type 7
            Assert.Equal(ConnectMessages.VtagMagic, BitConverter.ToUInt32(f, p)); p += 4;        // INDEX 'VTAG'
            Assert.Equal(ConnectMessages.ProtocolVersion, BitConverter.ToInt32(f, p)); p += 4;   // INDEX version=10000
            Assert.Equal(ConnectMessages.ConnectOp, BitConverter.ToInt32(f, p)); p += 4;         // INDEX op=6 (DWORD, cmp edi,0x6)
            // CTString modName "Rakion" + NUL
            Assert.Equal("Rakion\0", System.Text.Encoding.ASCII.GetString(f, p, 7)); p += 7;
            Assert.Equal(0x00, f[p]); p += 1;                                                     // CTString pasw "" = NUL
            Assert.Equal(1, BitConverter.ToInt32(f, p)); p += 4;                                  // INDEX nLocalPlayers
            Assert.Equal(4, BitConverter.ToInt32(f, p)); p += 4;                                  // ssp BufferActions
            Assert.Equal(1_000_000, BitConverter.ToInt32(f, p)); p += 4;                          // ssp MaxBPS
            Assert.Equal(1_000_000, BitConverter.ToInt32(f, p)); p += 4;                          // ssp MinBPS
            Assert.Equal(p, f.Length);                                                            // sem bytes a mais
        }

        [Fact]
        public void ConnectRemoteRequest_MagicOnWireBytes_AreLittleEndianOfValue()
        {
            byte[] f = ConnectMessages.BuildConnectRemoteRequest("", "", SessionSocketParams.Loopback);
            // INDEX('VTAG') = VALOR u32 0x56544147 escrito LE (least-significant byte primeiro) sai no fio
            // como 47 41 54 56. O disasm (cmp @0x36105ffa) compara o u32 já LIDO contra 0x56544147 — então o
            // que importa é o VALOR, não a ordem textual; o golden trava os bytes exatos do fio (blueprint §4.1).
            Assert.Equal(0x47, f[1]);   // LSB de 0x56544147
            Assert.Equal(0x41, f[2]);
            Assert.Equal(0x54, f[3]);
            Assert.Equal(0x56, f[4]);   // MSB
        }

        [Fact]
        public void ConnectRemoteRequest_RoundTrips_ThroughReader()
        {
            var sock = new SessionSocketParams(BufferActions: 5, MaxBps: 12345, MinBps: 678);
            byte[] f = ConnectMessages.BuildConnectRemoteRequest("ModX", "pw", sock, localPlayers: 2);

            var msg = NetMessage.From(f);
            Assert.Equal(NetworkMessageType.ReqConnectRemoteSessionState, msg.Type);
            var r = msg.BeginRead();
            Assert.Equal(unchecked((int)ConnectMessages.VtagMagic), r.ReadIndex());
            Assert.Equal(ConnectMessages.ProtocolVersion, r.ReadIndex());   // INDEX version
            Assert.Equal(ConnectMessages.ConnectOp, r.ReadIndex());         // INDEX op=6 (DWORD)
            Assert.Equal("ModX", r.ReadCString());
            Assert.Equal("pw", r.ReadCString());
            Assert.Equal(2, r.ReadIndex());
            Assert.Equal(sock, SessionSocketParams.ReadFrom(r));
            Assert.False(r.Overflowed);
        }

        // ---------- P2: CRC-check reply e type-only ----------

        [Fact]
        public void CrcCheckReply_MatchesSpec()
        {
            byte[] f = ConnectMessages.BuildCrcCheckReply(crc: 0x11223344, lastProcessedSequence: -1);
            Assert.Equal((byte)NetworkMessageType.RepCrcCheck, f[0]);
            Assert.Equal(0x11223344u, BitConverter.ToUInt32(f, 1));   // [ULONG crc]
            Assert.Equal(-1, BitConverter.ToInt32(f, 5));             // [INDEX lastSeq] começa em -1
            Assert.Equal(9, f.Length);
        }

        [Fact]
        public void TypeOnly_IsSingleTypeByte()
        {
            byte[] sd = ConnectMessages.BuildTypeOnly(NetworkMessageType.ReqStateDelta);
            byte[] crc = ConnectMessages.BuildTypeOnly(NetworkMessageType.ReqCrcList);
            Assert.Equal(new byte[] { 9 }, sd);     // REQ_STATEDELTA vazio = só o tipo
            Assert.Equal(new byte[] { 11 }, crc);   // REQ_CRCLIST vazio = só o tipo
        }

        // ---------- P2: CSessionSocketParams DTO ----------

        [Fact]
        public void SessionSocketParams_RoundTrips_ThreeIndex()
        {
            var p = new SessionSocketParams(BufferActions: 3, MaxBps: 999, MinBps: 111);
            using var w = NetMessage.BeginWrite(NetworkMessageType.ReqConnectRemoteSessionState);
            p.WriteTo(w);
            byte[] f = w.ToArray();

            Assert.Equal(1 + SessionSocketParams.SerializedSize, f.Length);   // type + 3×INDEX
            var r = NetMessage.From(f).BeginRead();
            Assert.Equal(p, SessionSocketParams.ReadFrom(r));
        }

        // ---------- P3: enum casa a fonte SE1 (golden source do protocolo) ----------

        [Theory]
        [InlineData(NetworkMessageType.KeepAlive, 2)]
        [InlineData(NetworkMessageType.ReqConnectLocalSessionState, 5)]
        [InlineData(NetworkMessageType.ReqConnectRemoteSessionState, 7)]
        [InlineData(NetworkMessageType.RepConnectRemoteSessionState, 8)]
        [InlineData(NetworkMessageType.ReqStateDelta, 9)]
        [InlineData(NetworkMessageType.RepStateDelta, 10)]
        [InlineData(NetworkMessageType.ReqCrcList, 11)]
        [InlineData(NetworkMessageType.ReqCrcCheck, 12)]
        [InlineData(NetworkMessageType.RepCrcCheck, 13)]
        [InlineData(NetworkMessageType.SyncCheck, 19)]
        [InlineData(NetworkMessageType.SeqAllActions, 21)]
        [InlineData(NetworkMessageType.SeqAddPlayer, 22)]
        [InlineData(NetworkMessageType.GameStreamBlocks, 26)]
        [InlineData(NetworkMessageType.RequestGameStreamResend, 27)]
        [InlineData(NetworkMessageType.Extra, 0x2f)]
        [InlineData(NetworkMessageType.RepDisconnected, 0x30)]
        public void Enum_MatchesSourceOrdinals(NetworkMessageType type, int expected)
        {
            Assert.Equal(expected, (int)type);
        }

        // ---------- CPlayerCharacter wire layout (FATO fonte SE1 PlayerCharacter.cpp:198-204) ----------

        /// <summary>operator&lt;&lt;(CNetworkMessage&amp;, CPlayerCharacter&amp;) = name + team + GUID(16) + appearance(32),
        /// SEM o magic "PLC4" (esse só no caminho de ARQUIVO). Trava o layout byte-a-byte e o round-trip.</summary>
        [Fact]
        public void PlayerCharacter_WireLayout_IsName_Team_Guid16_Appearance32_NoPlc4()
        {
            var guid = new byte[16]; for (int i = 0; i < 16; i++) guid[i] = (byte)(0x10 + i);
            var app = new byte[32]; for (int i = 0; i < 32; i++) app[i] = (byte)(0x40 + i);
            var pc = new PlayerCharacter("Rok", "1", guid, app);

            using var w = NetMessage.BeginWrite(NetworkMessageType.ReqConnectPlayer);
            pc.WriteTo(w);
            byte[] body = w.ToArray();

            // [u8 14]["Rok"\0]["1"\0][16 GUID][32 appearance] = 1 + 4 + 2 + 16 + 32 = 55B; sem "PLC4".
            Assert.Equal(1 + 4 + 2 + 16 + 32, body.Length);
            Assert.Equal((byte)NetworkMessageType.ReqConnectPlayer, body[0]);
            Assert.Equal(new byte[] { (byte)'R', (byte)'o', (byte)'k', 0 }, body[1..5]);
            Assert.Equal(new byte[] { (byte)'1', 0 }, body[5..7]);
            Assert.Equal(guid, body[7..23]);
            Assert.Equal(app, body[23..55]);
            Assert.DoesNotContain("PLC4", System.Text.Encoding.ASCII.GetString(body));

            // round-trip
            var r = NetMessage.From(body).BeginRead();
            var back = PlayerCharacter.ReadFrom(r);
            Assert.False(r.Overflowed);
            Assert.Equal("Rok", back.Name);
            Assert.Equal("1", back.Team);
            Assert.Equal(guid, back.Guid.ToArray());
            Assert.Equal(app, back.Appearance.ToArray());
        }

        /// <summary>O DTO normaliza GUID/appearance para os tamanhos fixos (trunca o que sobra, zera o que falta).</summary>
        [Fact]
        public void PlayerCharacter_NormalizesFixedBuffers()
        {
            var pc = new PlayerCharacter("a", "", new byte[] { 1, 2, 3 }, new byte[100]);
            Assert.Equal(16, pc.Guid.Length);
            Assert.Equal(32, pc.Appearance.Length);
            Assert.Equal(new byte[] { 1, 2, 3, 0 }, pc.Guid.Slice(0, 4).ToArray());   // resto zerado
        }

        /// <summary>SEQ_ADDPLAYER (FATO Server.cpp:1298-1300 / SessionState.cpp:1329-1330):
        /// [u8 22][INDEX iNewPlayer][CPlayerCharacter]. NÃO é [name][charIndex][playerIndex].</summary>
        [Fact]
        public void AddPlayer_Layout_IsIndexThenCharacter()
        {
            var pc = BotCharacterFactory.ForBot("Bot", charClass: 3, level: 9, team: 1);
            byte[] body = PlayerMessages.BuildAddPlayer(new PeerIdentity(pc, PlayerIndex: 5));

            var r = NetMessage.From(body).BeginRead();
            Assert.Equal(NetworkMessageType.SeqAddPlayer, NetMessage.From(body).Type);
            Assert.Equal(5, r.ReadI32());                       // INDEX iNewPlayer primeiro
            var back = PlayerCharacter.ReadFrom(r);
            Assert.False(r.Overflowed);
            Assert.Equal("Bot", back.Name);
            Assert.Equal("1", back.Team);
        }

        /// <summary>O appearance do factory é NÃO-ZERADO (placeholder classe/level) — evita o modelo-NULL→AV do
        /// AddPlayer headless. RESSALVA: é placeholder, não o blob real do Rakion (ver GAP/relatório).</summary>
        [Fact]
        public void BotCharacterFactory_Appearance_IsNonZero()
        {
            var pc = BotCharacterFactory.ForBot("Bot", charClass: 7, level: 42, team: 0);
            Assert.Equal((byte)7, pc.Appearance.Span[0]);      // classe
            Assert.Equal((byte)42, pc.Appearance.Span[1]);     // level
            Assert.Equal(16, pc.Guid.Length);
            // GUID determinístico: mesmo nome → mesmo GUID.
            var pc2 = BotCharacterFactory.ForBot("Bot", 0, 0, 1);
            Assert.Equal(pc.Guid.ToArray(), pc2.Guid.ToArray());
        }
    }
}
