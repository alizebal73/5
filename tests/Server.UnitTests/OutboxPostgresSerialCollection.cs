using Xunit;

namespace GameNet.Server.UnitTests;

[CollectionDefinition("Outbox PostgreSQL serial", DisableParallelization = true)]
public sealed class OutboxPostgresSerialCollection { }
