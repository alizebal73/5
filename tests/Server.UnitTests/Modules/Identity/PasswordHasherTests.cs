using System.Security.Cryptography;
using GameNet.Server.Modules.Identity.Infrastructure.Security;
using Xunit;

namespace GameNet.Server.UnitTests.Modules.Identity;

public sealed class PasswordHasherTests
{
    [Fact]
    public void Password_hash_uses_random_salt_and_verifies_only_the_original_password()
    {
        var hasher = new Pbkdf2PasswordHasher();
        const string password = "example-correct-horse-battery";
        var first = hasher.Hash(password);
        var second = hasher.Hash(password);
        Assert.NotEqual(first, second);
        Assert.StartsWith("pbkdf2-sha256$v1$600000$", first);
        Assert.True(hasher.Verify(password, first));
        Assert.False(hasher.Verify("different-password", first));
        Assert.False(hasher.Verify(password, "invalid"));
    }
    [Fact]
    public void Weak_password_is_rejected() =>
        Assert.Throws<ArgumentException>(() => new Pbkdf2PasswordHasher().Hash("short"));

    [Fact]
    public void Verify_rejects_passwords_over_the_supported_limit_even_if_the_hash_matches()
    {
        var password = new string('x', 257);
        var salt = RandomNumberGenerator.GetBytes(32);
        var derived = Rfc2898DeriveBytes.Pbkdf2(password, salt, 100_000, HashAlgorithmName.SHA256, 32);
        var encodedHash = "pbkdf2-sha256$v1$100000$" + Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(derived);

        Assert.False(new Pbkdf2PasswordHasher().Verify(password, encodedHash));
    }
}
