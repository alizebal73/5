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

        if (options.Authentication.Enabled && string.IsNullOrWhiteSpace(options.DatabaseConnectionString))
            return ValidateOptionsResult.Fail("A database connection string is required when authentication is enabled.");

        if (options.Agent.LeaseDurationSeconds is < 5 or > 1800)
            return ValidateOptionsResult.Fail("Agent lease duration must be 5-1800 seconds.");

        if (options.Agent.HeartbeatIntervalSeconds is < 1 or >= 1800 || options.Agent.HeartbeatIntervalSeconds >= options.Agent.LeaseDurationSeconds)
            return ValidateOptionsResult.Fail("Agent heartbeat interval must be positive and shorter than the lease duration.");

        if (options.Agent.AccessTokenLifetimeSeconds is < 60 or > 3600)
            return ValidateOptionsResult.Fail("Agent access token lifetime must be 60-3600 seconds.");

        if (options.Authentication.Enabled && string.IsNullOrWhiteSpace(options.Agent.ProvisioningKey))
            return ValidateOptionsResult.Fail("Agent provisioning key is required when production authentication is enabled.");

        if (!string.IsNullOrWhiteSpace(options.Agent.ProvisioningKey) && options.Agent.ProvisioningKey.Length < 32)
            return ValidateOptionsResult.Fail("Agent provisioning key must contain at least 32 characters.");
        return ValidateOptionsResult.Success;
    }
}
