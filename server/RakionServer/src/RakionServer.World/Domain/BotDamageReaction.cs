namespace RakionServer.World.Domain
{
    /// <summary>
    /// Reação de dano que a vítima publica sobre si mesma no `0x0311 kind=2`. Os pares vêm de
    /// captura humano×humano (28/07/2026, seats 0 e 1 no field 1): o alvo responde ao golpe
    /// alternando `(01,02)` e `(02,01)`, e passa a `(0F,07)` quando o golpe derruba ou mata.
    /// O par `(00,0A)` observado na mesma captura é dano ambiental periódico, não melê.
    /// </summary>
    public enum BotDamageReaction : byte
    {
        StaggerA,
        StaggerB,
        Knockdown
    }
}
