using System.Collections.Generic;

namespace RakionServer.World.Domain
{
    /// <summary>
    /// Combate server-side do bot (o servidor é a autoridade do HP do bot). Reproduz o que o RE
    /// permite: o bot leva dano de humanos e MORRE (dá kill/pontos ao humano) — o cliente do humano
    /// detecta o hit (computa cell points) mesmo sem o número cosmético HIT×N (gate `[+0x394]`).
    /// Funções PURAS: a detecção de acerto é por proximidade (melee) no espaço wire (i16).
    /// </summary>
    public static class BotCombat
    {
        /// <summary>Alcance de melee em unidades wire (i16 do 0x030A). Aproxima a hitbox do golpe.</summary>
        public const float MeleeRangeWire = 600f;

        public readonly record struct BotHit(PlayerRec Bot, bool Died);

        /// <summary>
        /// Resolve um golpe de melee do atacante em <paramref name="attackerPos"/> (time
        /// <paramref name="attackerTeam"/>) contra os bots inimigos vivos a até <see cref="MeleeRangeWire"/>.
        /// Aplica <paramref name="damage"/> a cada um. Devolve os bots atingidos e se morreram.
        /// </summary>
        public static IReadOnlyList<BotHit> ResolveMeleeAttack(
            Field field, BotVector attackerPos, byte attackerTeam, int damage)
        {
            var hits = new List<BotHit>();
            foreach (PlayerRec rec in field.BotSlots)
            {
                BotPlayer bot = rec.Bot!;
                if (!bot.Alive || bot.Team == attackerTeam) continue;
                if (rec.Position.HorizontalDistanceTo(attackerPos) > MeleeRangeWire) continue;
                bool died = bot.TakeDamage(damage);
                // Não marca rec.Dead aqui: o scoring da morte (ApplyReportedDeath) exige a vítima
                // ainda "viva" no field e cuida do estado de morte conforme o modo.
                hits.Add(new BotHit(rec, died));
            }
            return hits;
        }
    }
}
