using GameNet.Shared.Runtime;

namespace GameNet.Shared.Primitives;

/// <summary>
/// Compatibility adapter for callers that use the shared Server-origin policy.
/// All validation rules are owned by ServerEndpointPolicy.
/// </summary>
public static class ServerEndpointAddress
{
    public static bool IsAllowed(string? value) => ServerEndpointPolicy.IsValidBaseUrl(value);
}
