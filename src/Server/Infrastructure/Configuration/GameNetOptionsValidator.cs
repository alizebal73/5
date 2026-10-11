using Microsoft.Extensions.Options;

namespace GameNet.Server.Infrastructure.Configuration;

public sealed class GameNetOptionsValidator : IValidateOptions<GameNetOptions>
{
    public ValidateOptionsResult Validate(string? name, GameNetOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BusinessTimeZone)) return ValidateOptionsResult.Fail("BusinessTimeZone is required.");
        if (!string.Equals(options.Currency.Trim(), "TOM", StringComparison.OrdinalIgnoreCase)) return ValidateOptionsResult.Fail("Currency must be TOM.");
        if (options.Authentication.Enabled && (string.IsNullOrWhiteSpace(options.Authentication.Issuer) || string.IsNullOrWhiteSpace(options.Authentication.Audience)))
            return ValidateOptionsResult.Fail("Production authentication requires issuer and audience.");

        if (options.Agent.LeaseDurationSeconds is < 5 or > 1800)
            return ValidateOptionsResult.Fail("Agent lease duration must be 5-1800 seconds.");

        if (options.Agent.HeartbeatIntervalSeconds is < 1 or >= 1800 || options.Agent.HeartbeatIntervalSeconds >= options.Agent.LeaseDurationSeconds)
            return ValidateOptionsResult.Fail("Agent heartbeat interval must be positive and shorter than the lease duration.");

        if (options.Agent.AccessTokenLifetimeSeconds is < 60 or > 3600)
            return ValidateOptionsResult.Fail("Agent access token lifetime must be 60-3600 seconds.");

        return ValidateOptionsResult.Success;
    }
}
