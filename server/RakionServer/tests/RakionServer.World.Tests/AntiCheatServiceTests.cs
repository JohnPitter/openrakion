using System.Collections.Generic;
using RakionServer.World.Security;
using Xunit;

namespace RakionServer.World.Tests
{
    /// <summary>
    /// Contrato do anti-cheat server-side (OpenGuard). Puro dominio: sink capturado + clock
    /// controlavel, sem socket/DB. Cobre o pipeline deteccao -> score -> politica -> decisao.
    /// </summary>
    public class AntiCheatServiceTests
    {
        private sealed class CapturingSink : IViolationSink
        {
            public readonly List<(Violation V, int Score, GuardDecision Decision)> Records = new();
            public void Record(in Violation v, int sessionScore, in GuardDecision decision)
                => Records.Add((v, sessionScore, decision));
        }

        private static (AntiCheatService Svc, CapturingSink Sink, long[] Clock) Make(AntiCheatConfig cfg)
        {
            var sink = new CapturingSink();
            var clock = new long[] { 5000 };
            var svc = new AntiCheatService(cfg, sink, () => clock[0]);
            return (svc, sink, clock);
        }

        [Fact]
        public void Disabled_PassesEverything_AndDoesNotAudit()
        {
            var (svc, sink, _) = Make(new AntiCheatConfig { Enabled = false, MaxOpcodesPerWindow = 0 });

            var d = svc.OnOpcode(1, "u", 0x43);
            var h = svc.OnClientHash(1, "u", "aaa", "bbb", present: true);

            Assert.Equal(GuardDecision.Pass, d);
            Assert.Equal(GuardDecision.Pass, h);
            Assert.Empty(sink.Records);
        }

        [Fact]
        public void OpcodeFlood_DropsOverLimit_MonitorModeDoesNotKick()
        {
            var (svc, sink, _) = Make(new AntiCheatConfig
            {
                MaxOpcodesPerWindow = 3, OpcodeWindowMs = 1000, EnforceKick = false,
            });

            Assert.False(svc.OnOpcode(1, "u", 0x12).Drop); // 1
            Assert.False(svc.OnOpcode(1, "u", 0x12).Drop); // 2
            Assert.False(svc.OnOpcode(1, "u", 0x12).Drop); // 3 (no teto)
            var over = svc.OnOpcode(1, "u", 0x12);          // 4 (estoura)

            Assert.True(over.Drop);
            Assert.False(over.Kick);
            var last = sink.Records[^1];
            Assert.Equal(ViolationKind.OpcodeFlood, last.V.Kind);
        }

        [Fact]
        public void OpcodeFlood_WindowResets_AfterWindowElapses()
        {
            var (svc, _, clock) = Make(new AntiCheatConfig { MaxOpcodesPerWindow = 2, OpcodeWindowMs = 1000 });

            svc.OnOpcode(1, "u", 1);
            svc.OnOpcode(1, "u", 1);
            Assert.True(svc.OnOpcode(1, "u", 1).Drop);   // estoura na janela atual

            clock[0] += 1000;                             // vira a janela
            Assert.False(svc.OnOpcode(1, "u", 1).Drop);   // contagem reinicia
        }

        [Fact]
        public void ProtocolViolations_AccumulateScore_KickAtThreshold()
        {
            var (svc, _, _) = Make(new AntiCheatConfig { EnforceKick = true, KickScore = 100 });

            Assert.False(svc.OnProtocolViolation(1, "u", ViolationKind.MalformedFrame, "x").Kick); // 40
            Assert.False(svc.OnProtocolViolation(1, "u", ViolationKind.MalformedFrame, "x").Kick); // 80
            var kick = svc.OnProtocolViolation(1, "u", ViolationKind.MalformedFrame, "x");          // 120 -> kick

            Assert.True(kick.Kick);
        }

        [Fact]
        public void ProtocolViolations_MonitorMode_NeverKicks()
        {
            var (svc, _, _) = Make(new AntiCheatConfig { EnforceKick = false });

            for (int i = 0; i < 20; i++)
                Assert.False(svc.OnProtocolViolation(1, "u", ViolationKind.MalformedFrame, "x").Kick);
        }

        [Fact]
        public void ClientHash_NoReference_IsNoOp()
        {
            var (svc, sink, _) = Make(new AntiCheatConfig { EnforceClientHash = true });

            var d = svc.OnClientHash(1, "u", "whatever", expected: "", present: true);

            Assert.False(d.Kick);
            Assert.Empty(sink.Records); // sem hash configurado nao ha o que atestar
        }

        [Fact]
        public void ClientHash_Mismatch_MonitorMode_AuditsWithoutKick()
        {
            var (svc, sink, _) = Make(new AntiCheatConfig { EnforceClientHash = false });

            var d = svc.OnClientHash(1, "u", "aaa", "bbb", present: true);

            Assert.False(d.Kick);
            Assert.Equal(ViolationKind.ClientHashMismatch, sink.Records[^1].V.Kind);
        }

        [Fact]
        public void ClientHash_Mismatch_Enforced_KicksWith0xBC()
        {
            var (svc, _, _) = Make(new AntiCheatConfig { EnforceClientHash = true });

            var d = svc.OnClientHash(1, "u", "aaa", "bbb", present: true);

            Assert.True(d.Kick);
        }

        [Fact]
        public void ClientHash_Match_Passes()
        {
            var (svc, sink, _) = Make(new AntiCheatConfig { EnforceClientHash = true });

            var d = svc.OnClientHash(1, "u", "deadbeef", "deadbeef", present: true);

            Assert.False(d.Kick);
            Assert.Empty(sink.Records);
        }

        [Fact]
        public void ClientHash_MissingAttestation_Enforced_Kicks()
        {
            var (svc, _, _) = Make(new AntiCheatConfig { EnforceClientHash = true });

            var d = svc.OnClientHash(1, "u", reported: "", expected: "deadbeef", present: false);

            Assert.True(d.Kick);
        }

        [Fact]
        public void ForgetSession_ResetsAccumulatedScore()
        {
            var (svc, _, _) = Make(new AntiCheatConfig { EnforceKick = true, KickScore = 100 });

            svc.OnProtocolViolation(1, "u", ViolationKind.MalformedFrame, "x"); // 40
            svc.OnProtocolViolation(1, "u", ViolationKind.MalformedFrame, "x"); // 80
            svc.ForgetSession(1);

            Assert.False(svc.OnProtocolViolation(1, "u", ViolationKind.MalformedFrame, "x").Kick); // volta a 40
        }

        [Fact]
        public void Sessions_AreScoredIndependently()
        {
            var (svc, _, _) = Make(new AntiCheatConfig { EnforceKick = true, KickScore = 100 });

            svc.OnProtocolViolation(1, "a", ViolationKind.MalformedFrame, "x"); // slot 1: 40
            svc.OnProtocolViolation(1, "a", ViolationKind.MalformedFrame, "x"); // slot 1: 80
            Assert.False(svc.OnProtocolViolation(2, "b", ViolationKind.MalformedFrame, "x").Kick); // slot 2: 40
        }
    }
}
