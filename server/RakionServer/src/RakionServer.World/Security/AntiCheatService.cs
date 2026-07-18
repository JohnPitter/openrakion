using System;
using System.Collections.Concurrent;

namespace RakionServer.World.Security
{
    /// <summary>
    /// Anti-cheat server-side do OpenRakion ("OpenGuard"). Substitui o nProtect GameGuard
    /// (morto) por verificacoes que o SERVIDOR impoe sozinho, sem agente no cliente:
    /// integridade do binario (hash), anomalias de protocolo (seq/opcode/frame) e flood
    /// de pacotes (TCP e UDP). Une num so pipeline as checagens antes dispersas pelo world.
    ///
    /// Dominio isolado de I/O: recebe primitivos/DTOs (nunca o <c>ClientSession</c>), pontua
    /// a sessao, consulta a politica e devolve uma <see cref="GuardDecision"/>. Quem chama
    /// aplica (dropa/desconecta); a auditoria sai por <see cref="IViolationSink"/>.
    /// </summary>
    public sealed class AntiCheatService
    {
        private readonly AntiCheatConfig _cfg;
        private readonly IViolationSink _sink;
        private readonly Func<long> _nowMs;
        private readonly ConcurrentDictionary<ushort, SessionMonitor> _monitors = new();

        public AntiCheatService(AntiCheatConfig cfg, IViolationSink sink, Func<long>? nowMs = null)
        {
            _cfg = cfg;
            _sink = sink;
            _nowMs = nowMs ?? (() => Environment.TickCount64);
        }

        /// <summary>Rate-limit de opcodes TCP. Estouro -> flood (dropa; escala se EnforceKick).</summary>
        public GuardDecision OnOpcode(ushort slot, string userId, ushort opcode)
        {
            if (!_cfg.Enabled) return GuardDecision.Pass;
            var m = _monitors.GetOrAdd(slot, NewMonitor);
            bool flood;
            lock (m) flood = m.Opcodes.Hit(_nowMs(), _cfg.OpcodeWindowMs, _cfg.MaxOpcodesPerWindow);
            return flood
                ? Report(slot, userId, ViolationKind.OpcodeFlood, ViolationSeverity.Low, $"op=0x{opcode:x2}", drop: true)
                : GuardDecision.Pass;
        }

        /// <summary>Rate-limit de pacotes UDP de gameplay (vetor de amplificacao do relay).</summary>
        public GuardDecision OnGameplayPacket(ushort slot, string userId)
        {
            if (!_cfg.Enabled) return GuardDecision.Pass;
            var m = _monitors.GetOrAdd(slot, NewMonitor);
            bool flood;
            lock (m) flood = m.Gameplay.Hit(_nowMs(), _cfg.GameplayWindowMs, _cfg.MaxGameplayPerWindow);
            return flood
                ? Report(slot, userId, ViolationKind.GameplayFlood, ViolationSeverity.Low, "udp gameplay", drop: true)
                : GuardDecision.Pass;
        }

        /// <summary>Anomalia de protocolo (seq fora de ordem, opcode desconhecido, frame forjado, chave UDP).</summary>
        public GuardDecision OnProtocolViolation(ushort slot, string userId, ViolationKind kind, string detail)
        {
            if (!_cfg.Enabled) return GuardDecision.Pass;
            var sev = kind switch
            {
                ViolationKind.MalformedFrame => ViolationSeverity.High,
                ViolationKind.UnknownOpcode => ViolationSeverity.Medium,
                ViolationKind.ProtocolSequence => ViolationSeverity.Medium,
                ViolationKind.UdpKeyMismatch => ViolationSeverity.Medium,
                _ => ViolationSeverity.Low,
            };
            return Report(slot, userId, kind, sev, detail, drop: false);
        }

        /// <summary>
        /// Atestacao de integridade do binario (Op_VerifyClientHash / file.php): compara o hash
        /// reportado com o esperado. Mismatch/ausencia => kick imediato quando EnforceClientHash
        /// (fiel ao DISC 0xbc do exe); senao, so audita (modo observacao).
        /// </summary>
        public GuardDecision OnClientHash(ushort slot, string userId, string reported, string expected, bool present)
        {
            if (!_cfg.Enabled) return GuardDecision.Pass;
            if (string.IsNullOrEmpty(expected)) return GuardDecision.Pass; // sem referencia configurada -> nada a atestar
            if (!present)
                return HashViolation(slot, userId, ViolationKind.ClientHashMissing, ViolationSeverity.Medium, "atestacao ausente");
            if (!string.Equals(reported ?? "", expected ?? "", StringComparison.Ordinal))
                return HashViolation(slot, userId, ViolationKind.ClientHashMismatch, ViolationSeverity.High, "hash divergente");
            return GuardDecision.Pass;
        }

        /// <summary>Esquece o estado da sessao ao desconectar (evita vazamento do dicionario).</summary>
        public void ForgetSession(ushort slot) => _monitors.TryRemove(slot, out _);

        private GuardDecision HashViolation(ushort slot, string userId, ViolationKind kind, ViolationSeverity sev, string detail)
        {
            bool kick = _cfg.EnforceClientHash;
            return Report(slot, userId, kind, sev, detail, drop: kick, forceKick: kick);
        }

        private GuardDecision Report(ushort slot, string userId, ViolationKind kind, ViolationSeverity sev,
            string detail, bool drop, bool forceKick = false)
        {
            var m = _monitors.GetOrAdd(slot, NewMonitor);
            long now = _nowMs();
            int score;
            lock (m)
            {
                DecayScore(m, now);
                score = m.Score += ScoreOf(sev);
            }

            bool kick = forceKick || (_cfg.EnforceKick && score >= _cfg.KickScore);
            var decision = kick ? GuardDecision.Kicked(kind.ToString())
                         : drop ? GuardDecision.DropOnly(kind.ToString())
                                : GuardDecision.Pass;

            _sink.Record(new Violation(slot, userId ?? "", kind, sev, detail ?? ""), score, decision);
            return decision;
        }

        /// <summary>Decai o score pelo tempo decorrido (ScoreDecayPerMin) — chamar com o lock do monitor.</summary>
        private void DecayScore(SessionMonitor m, long now)
        {
            if (_cfg.ScoreDecayPerMin <= 0) return;
            if (m.LastDecayMs == 0) { m.LastDecayMs = now; return; }
            long mins = (now - m.LastDecayMs) / 60_000;
            if (mins <= 0) return;
            m.Score = Math.Max(0, m.Score - (int)Math.Min(int.MaxValue, mins * _cfg.ScoreDecayPerMin));
            m.LastDecayMs += mins * 60_000;
        }

        private static SessionMonitor NewMonitor(ushort _) => new();

        private static int ScoreOf(ViolationSeverity sev) => sev switch
        {
            ViolationSeverity.Low => 5,
            ViolationSeverity.Medium => 15,
            ViolationSeverity.High => 40,
            ViolationSeverity.Critical => 100,
            _ => 0,
        };

        private sealed class SessionMonitor
        {
            public int Score;
            public long LastDecayMs;
            public FixedWindow Opcodes;
            public FixedWindow Gameplay;
        }

        /// <summary>Contador de janela fixa: reinicia ao virar a janela e sinaliza estouro do teto.</summary>
        private struct FixedWindow
        {
            private long _start;
            private int _count;

            public bool Hit(long now, int windowMs, int max)
            {
                if (now - _start >= windowMs) { _start = now; _count = 0; }
                _count++;
                return _count > max;
            }
        }
    }
}
