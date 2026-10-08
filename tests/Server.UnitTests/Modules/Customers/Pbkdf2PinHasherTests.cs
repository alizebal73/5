using GameNet.Server.Infrastructure.Security;

namespace GameNet.Server.UnitTests.Modules.Customers;

public sealed class Pbkdf2PinHasherTests
{
    [Fact]
    public void Pin_hash_can_be_verified()
    {
        var hasher = new Pbkdf2PinHasher();
        var hash = hasher.Hash("1234");

        Assert.True(hasher.Verify("1234", hash));
        Assert.False(hasher.Verify("4321", hash));
    }

    [Fact]
    public void Invalid_pin_is_rejected()
    {
        var hasher = new Pbkdf2PinHasher();
        Assert.Throws<ArgumentException>(() => hasher.Hash("123"));
        Assert.Throws<ArgumentException>(() => hasher.Hash("12a4"));
    }
}
