using GameNet.Server.Infrastructure.Security;
using Xunit;

namespace GameNet.Server.UnitTests;

public sealed class ServerTestSecretProvisioningValidationTests
{
    [Fact]
    public void DisposableTestConnection_AcceptsOnlyDedicatedLocalDatabaseAndRole()
    {
        var builder = ServerSecretProvisioning.ValidateDisposableTestConnectionString(
            "Host=127.0.0.1;Port=5432;Database=gamenet_test_ab12cd34;Username=gamenet_test_app_ab12cd34;Password=not-a-real-secret;Pooling=false");

        Assert.Equal("127.0.0.1", builder.Host);
        Assert.Equal("gamenet_test_ab12cd34", builder.Database);
        Assert.Equal("gamenet_test_app_ab12cd34", builder.Username);
        Assert.False(builder.Pooling);
    }

    [Theory]
    [InlineData("Host=192.168.0.9;Database=gamenet_test_ab12cd34;Username=gamenet_test_app_ab12cd34;Password=x;Pooling=false")]
    [InlineData("Host=127.0.0.1;Database=postgres;Username=gamenet_test_app_ab12cd34;Password=x;Pooling=false")]
    [InlineData("Host=127.0.0.1;Database=gamenet_test_ab12cd34;Username=postgres;Password=x;Pooling=false")]
    [InlineData("Host=127.0.0.1;Database=gamenet_test_ab12cd34;Username=gamenet_test_app_ab12cd34;Password=x;Pooling=true")]
    [InlineData("Host=127.0.0.1;Database=gamenet_test_ab12cd34;Username=gamenet_test_app_ab12cd34;Pooling=false")]
    public void DisposableTestConnection_RejectsUnsafeConnectionStrings(string connectionString)
    {
        Assert.Throws<ServerSecretStoreException>(
            () => ServerSecretProvisioning.ValidateDisposableTestConnectionString(connectionString));
    }

    [Fact]
    public void DisposableTestConnection_RejectsBlankAndMalformedStringsWithoutEchoingThem()
    {
        Assert.Throws<ServerSecretStoreException>(
            () => ServerSecretProvisioning.ValidateDisposableTestConnectionString(" "));
        Assert.Throws<ServerSecretStoreException>(
            () => ServerSecretProvisioning.ValidateDisposableTestConnectionString("not a connection string"));
    }
}
