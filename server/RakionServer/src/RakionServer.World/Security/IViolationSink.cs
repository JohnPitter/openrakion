using RakionServer.Common;

namespace RakionServer.World.Security
{
    /// <summary>
    /// Porta de saida das violacoes (auditoria). Mantem o servico de dominio isolado do
    /// destino concreto — log hoje, tabela no DB amanha — sem acoplar I/O ao pipeline.
    /// </summary>
    public interface IViolationSink
    {
        void Record(in Violation v, int sessionScore, in GuardDecision decision);
    }

    /// <summary>Encaminha cada violacao a varios sinks (log + DB, etc.).</summary>
    public sealed class CompositeViolationSink : IViolationSink
    {
        private readonly IViolationSink[] _sinks;
        public CompositeViolationSink(params IViolationSink[] sinks) => _sinks = sinks;

        public void Record(in Violation v, int sessionScore, in GuardDecision decision)
        {
            foreach (var s in _sinks)
                s.Record(v, sessionScore, decision);
        }
    }

    /// <summary>
    /// Sink padrao: registra a violacao no log ("guard"). Nivel pela gravidade — High/
    /// Critical (e qualquer Kick) em Warn; o resto em Debug para nao poluir o modo observacao.
    /// </summary>
    public sealed class LogViolationSink : IViolationSink
    {
        public void Record(in Violation v, int sessionScore, in GuardDecision decision)
        {
            string action = decision.Kick ? "KICK" : decision.Drop ? "DROP" : "log";
            bool loud = decision.Kick || v.Severity >= ViolationSeverity.High;
            const string fmt = "[{0}] {1} sev={2} score={3} -> {4}: {5}";
            if (loud)
                Log.Warn("guard", fmt, v.Slot, v.Kind, v.Severity, sessionScore, action, v.Detail);
            else
                Log.Debug("guard", fmt, v.Slot, v.Kind, v.Severity, sessionScore, action, v.Detail);
        }
    }
}
