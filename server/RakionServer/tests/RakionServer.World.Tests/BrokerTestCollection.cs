using Xunit;

namespace RakionServer.World.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class BrokerTestCollection
{
    public const string Name = "Broker global state";
}
