using System.Security.Cryptography;
using GameNet.Server.Modules.Customers.Application;

namespace GameNet.Server.Modules.Customers.Infrastructure;

public sealed class CustomerPasswordHasher : ICustomerPasswordHasher
{
    private const int Iterations = 600_000, SaltSize = 32, KeySize = 32, MaximumAcceptedIterations = 1_000_000;
    private static readonly Lazy<string> DummyEncodedHash = new(() => HashUnchecked("DummyCustomerPassphrase!"), LazyThreadSafetyMode.ExecutionAndPublication);

    public static string DummyHash => DummyEncodedHash.Value;
    public bool IsValidPassword(string? password) =>
        password is { Length: >= 8 and <= 256 } && !string.IsNullOrWhiteSpace(password) &&
        !password.Any(char.IsControl);

    public string HashPassword(string password)
    {
        if (!IsValidPassword(password))
            throw new ArgumentException("Customer password must be 8-256 characters and contain no control characters.", nameof(password));
        return HashUnchecked(password);
    }

    private static string HashUnchecked(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return "pbkdf2-sha256$v1$" + Iterations + "$" + Convert.ToBase64String(salt) + "$" + Convert.ToBase64String(key);
    }

    public bool VerifyPassword(string? password, string encodedHash)
    {
        var supplied = password is { Length: <= 256 } ? password : string.Empty;
        if (string.IsNullOrWhiteSpace(encodedHash)) encodedHash = DummyHash;
        var parts = encodedHash.Split('$');
        if (parts.Length != 5 || parts[0] != "pbkdf2-sha256" || parts[1] != "v1" ||
            !int.TryParse(parts[2], out var iterations) || iterations < 100_000 || iterations > MaximumAcceptedIterations)
            return false;
        try
        {
            var salt = Convert.FromBase64String(parts[3]);
            var expected = Convert.FromBase64String(parts[4]);
            if (salt.Length != SaltSize || expected.Length != KeySize) return false;
            var actual = Rfc2898DeriveBytes.Pbkdf2(supplied, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException) { return false; }
        catch (ArgumentException) { return false; }
    }
}
