using System.Security.Cryptography;
using GameNet.Server.Modules.Identity.Application;

namespace GameNet.Server.Modules.Identity.Infrastructure.Security;

public sealed class Pbkdf2PasswordHasher : IPasswordHasher
{
    private const int Iterations = 600_000;
    private const int SaltSize = 32;
    private const int KeySize = 32;
    private const int MaximumAcceptedIterations = 1_000_000;

    public string Hash(string password)
    {
        ValidatePassword(password);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return "pbkdf2-sha256$v1$" + Iterations + "$" + Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(key);
    }
    public bool Verify(string password, string encodedHash)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length > 256 || string.IsNullOrWhiteSpace(encodedHash)) return false;
        var parts = encodedHash.Split('$');
        if (parts.Length != 5 || parts[0] != "pbkdf2-sha256" || parts[1] != "v1" ||
            !int.TryParse(parts[2], out var iterations) || iterations < 100_000 || iterations > MaximumAcceptedIterations) return false;
        try
        {
            var salt = Convert.FromBase64String(parts[3]);
            var expected = Convert.FromBase64String(parts[4]);
            if (salt.Length != SaltSize || expected.Length != KeySize) return false;
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
        catch (ArgumentException) { return false; }
    }
    private static void ValidatePassword(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 10)
            throw new ArgumentException("Password must contain at least 10 characters.", nameof(password));
        if (password.Length > 256) throw new ArgumentException("Password is too long.", nameof(password));
    }
}
