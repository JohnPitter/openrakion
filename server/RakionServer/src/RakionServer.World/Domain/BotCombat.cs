namespace RakionServer.World.Domain
{
    /// <summary>
    /// Combate server-side do bot (o servidor é a autoridade do HP do bot). Reproduz o que o RE
    /// permite: o bot leva dano de humanos e MORRE (dá kill/pontos ao humano) — o cliente do humano
    /// detecta o hit (computa cell points) mesmo sem o número cosmético HIT×N (gate `[+0x394]`).
    /// A colisão vem da engine do atacante; o domínio valida alvo, estado, time e alcance antes de
    /// alterar o HP. Uma animação 0x0311 isolada nunca é tratada como acerto.
    /// </summary>
    public static class BotCombat
    {
        /// <summary>Alcance de melee em unidades wire (i16 do 0x030A). Aproxima a hitbox do golpe.</summary>
        public const float MeleeRangeWire = 600f;

        public readonly record struct BotHit(PlayerRec BotRecord, bool Died);

        /// <summary>
        /// Aplica um hit confirmado pela engine a um único assento de bot, após as validações
        /// autoritativas do match.
        /// </summary>
        public static bool TryApplyConfirmedHit(
            Field field, PlayerRec attacker, byte targetSeat, int damage, out BotHit hit)
        {
            hit = default;
            PlayerRec? target = field.RecAt(targetSeat);
            BotPlayer? bot = target?.Bot;
            if (target == null || bot == null || !bot.Alive || attacker.Dead ||
                attacker.State != 4 || bot.Team == attacker.Team || damage <= 0)
                return false;
            if (target.Position.HorizontalDistanceTo(attacker.Position) > MeleeRangeWire)
                return false;

            hit = new BotHit(target, bot.TakeDamage(damage));
            return true;
        }
    }
}
