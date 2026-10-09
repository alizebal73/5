namespace GameNet.Shared.Primitives;

/// <summary>
/// Defines the allowed shape and transport security policy for the Server origin shared by Desktop and Agent.
/// </summary>
public static class ServerEndpointAddress
{
    /// <summary>
    /// Accepts root HTTPS origins for LAN/remote use and HTTP only for loopback development.
    /// Embedded credentials, path prefixes, query strings and fragments are not valid Server origins.
    /// </summary>
    public static bool IsAllowed(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme != Uri.UriSchemeHttps &&
            !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback))
        {
            return false;
        }

        return string.IsNullOrEmpty(uri.UserInfo) &&
            string.IsNullOrEmpty(uri.Query) &&
            string.IsNullOrEmpty(uri.Fragment) &&
            uri.AbsolutePath == "/";
    }
}
