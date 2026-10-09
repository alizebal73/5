namespace GameNet.Shared.Runtime;

public static class ServerEndpointPolicy
{
    public static bool IsValidBaseUrl(string? baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            return false;

        if (!string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) ||
            !string.Equals(uri.AbsolutePath, "/", StringComparison.Ordinal))
        {
            return false;
        }

        if (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return !IsWildcardAddress(uri);

        return string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && uri.IsLoopback;
    }

    private static bool IsWildcardAddress(Uri uri) =>
        string.Equals(uri.Host, "0.0.0.0", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(uri.Host, "::", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(uri.Host, "[::]", StringComparison.OrdinalIgnoreCase);
}
