using GameNet.Server.Infrastructure.Security;

namespace GameNet.Server.UnitTests.Modules.Identity;

public sealed class Pbkdf2PasswordHasherTests
{
    [Fact]
    public void Hash_can_be_verified()
    {
        var hasher = new Pbkdf2PasswordHasher();
        var encoded = hasher.Hash("StrongPassword123!");

        Assert.True(hasher.Verify("StrongPassword123!", encoded));
        Assert.False(hasher.Verify("WrongPassword123!", encoded));
    }

    [Fact]
    public void Passwords_are_salted()
    {
        var hasher = new Pbkdf2PasswordHasher();

        var first = hasher.Hash("StrongPassword123!");
        var second = hasher.Hash("StrongPassword123!");

        Assert.NotEqual(first, second);
    }
}
