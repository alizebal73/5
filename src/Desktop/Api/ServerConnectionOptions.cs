namespace GameNet.Desktop.Api;

public sealed class ServerConnectionOptions
{
    public const string SectionName = "GameNet:Server";

    public string BaseUrl { get; init; } = string.Empty;
}
