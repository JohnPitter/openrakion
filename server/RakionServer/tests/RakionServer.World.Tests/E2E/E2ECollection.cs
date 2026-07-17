using Xunit;

namespace RakionServer.World.Tests.E2E
{
    /// <summary>
    /// Serializa os testes E2E: cada um sobe um <see cref="WorldServer"/> nas MESMAS
    /// portas fixas de loopback (41708/41709/41706), então não podem rodar em paralelo.
    /// </summary>
    [CollectionDefinition("E2E", DisableParallelization = true)]
    public sealed class E2ECollection { }
}
