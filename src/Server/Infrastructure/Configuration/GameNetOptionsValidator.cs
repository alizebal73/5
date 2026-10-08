using Microsoft.Extensions.Options;

namespace GameNet.Server.Infrastructure.Configuration;

public sealed class GameNetOptionsValidator : IValidateOptions<GameNetOptions>
{
    public ValidateOptionsResult Validate(string? name, GameNetOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BusinessTimeZone)) return ValidateOptionsResult.Fail("BusinessTimeZone is required.");
        if (!string.Equals(options.Currency.Trim(), "TOM", StringComparison.OrdinalIgnoreCase)) return ValidateOptionsResult.Fail("Currency must be TOM.");
        if (options.Authentication.Enabled && (string.IsNullOrWhiteSpace(options.Authentication.Issuer) || string.IsNullOrWhiteSpace(options.Authentication.Audience) || string.IsNullOrWhiteSpace(options.Authentication.SigningKey) || options.Authentication.SigningKey.Length < 32))
            return ValidateOptionsResult.Fail("Production authentication requires issuer, audience and a 32+ character signing key.");
        return ValidateOptionsResult.Success;
    }
}
