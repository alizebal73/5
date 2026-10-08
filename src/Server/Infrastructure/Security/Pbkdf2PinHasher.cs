using System.Security.Cryptography;
using GameNet.Server.Modules.Customers.Application;

namespace GameNet.Server.Infrastructure.Security;

public sealed class Pbkdf2PinHasher : IPinHasher
{
    private const int Iterations = 300_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public string Hash(string pin)
    {
        ValidatePin(pin);
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var key = Rfc2898DeriveBytes.Pbkdf2(
            pin,
            salt,
            Iterations,
            HashAlgorithmName.SHA256,
            KeySize);

        return "pbkdf2-pin-sha256$v1$" +
               Iterations +
               "$" +
               Convert.ToBase64String(salt) +
               "$" +
               Convert.ToBase64String(key);
    }

    public bool Verify(string pin, string encodedHash)
    {
        if (string.IsNullOrWhiteSpace(pin) || string.IsNullOrWhiteSpace(encodedHash))
            return false;

        var parts = encodedHash.Split('$');
        if (parts.Length != 5 ||
            parts[0] != "pbkdf2-pin-sha256" ||
            parts[1] != "v1" ||
            !int.TryParse(parts[2], out var iterations))
            return false;

        try
        {
            var salt = Convert.FromBase64String(parts[3]);
            var expected = Convert.FromBase64String(parts[4]);
            var actual = Rfc2898DeriveBytes.Pbkdf2(
                pin,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expected.Length);

            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static void ValidatePin(string pin)
    {
        if (string.IsNullOrWhiteSpace(pin) ||
            pin.Length < 4 ||
            pin.Length > 12 ||
            pin.Any(c => c is < '0' or > '9'))
            throw new ArgumentException("PIN must contain 4 to 12 digits.", nameof(pin));
    }
}
