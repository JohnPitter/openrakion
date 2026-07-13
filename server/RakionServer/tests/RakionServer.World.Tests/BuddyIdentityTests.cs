using RakionServer.Buddy;
using Xunit;

namespace RakionServer.World.Tests
{
    /// <summary>
    /// Casamento conexão-Buddy ↔ sessão-World por proximidade de porta (<see cref="BuddyIdentity"/>): com 2+
    /// clientes no mesmo IP, cada conexão do Buddy deve casar com a sessão de World cuja porta efêmera foi alocada
    /// imediatamente antes (mesmo processo), em distância circular de 16 bits.
    /// </summary>
    public class BuddyIdentityTests
    {
        [Fact]
        public void DoisClientes_CadaBuddyCasaComSeuWorld()
        {
            // cliente A: world 50000 -> buddy 50002; cliente B: world 50040 -> buddy 50041
            int[] worlds = { 50000, 50040 };
            Assert.Equal(0, BuddyIdentity.PickNearestByPort(worlds, 50002));
            Assert.Equal(1, BuddyIdentity.PickNearestByPort(worlds, 50041));
        }

        [Fact]
        public void ConexoesIntercaladas_NaoTrocamAsContas()
        {
            // A-world 50000, B-world 50003, A-buddy 50005, B-buddy 50007: mesmo intercalado, cada buddy
            // fica mais perto (à frente) do PRÓPRIO world do que do outro.
            int[] worlds = { 50000, 50003 };
            Assert.Equal(1, BuddyIdentity.PickNearestByPort(worlds, 50005));   // dist 5 vs 2 -> B
            Assert.Equal(1, BuddyIdentity.PickNearestByPort(worlds, 50007));   // dist 7 vs 4 -> B (A já atrelada no pool real)
        }

        [Fact]
        public void WrapDoPoolEfemero_DistanciaCircular()
        {
            // world em 65530, buddy já deu a volta (porta 5): dist circular 11 vence a candidata em 40000 (dist 25541)
            int[] worlds = { 40000, 65530 };
            Assert.Equal(1, BuddyIdentity.PickNearestByPort(worlds, 5));
        }

        [Fact]
        public void PortaAtras_EhDistanciaGigante_NaoCasa()
        {
            // buddy 50001 NÃO pode casar com world 50002 (efêmera do buddy nasce DEPOIS): a distância circular
            // "para trás" é ~64k, então vence o world 49990 que está à frente por 11.
            int[] worlds = { 50002, 49990 };
            Assert.Equal(1, BuddyIdentity.PickNearestByPort(worlds, 50001));
        }

        [Fact]
        public void LinhaLegadaSemPorta_SoCasaSeForAUnica()
        {
            Assert.Equal(0, BuddyIdentity.PickNearestByPort(new[] { 0 }, 50001));            // única -> casa
            Assert.Equal(1, BuddyIdentity.PickNearestByPort(new[] { 0, 49999 }, 50001));     // real próxima vence
        }

        [Fact]
        public void ListaVazia_MenosUm() =>
            Assert.Equal(-1, BuddyIdentity.PickNearestByPort(System.Array.Empty<int>(), 50001));
    }
}
