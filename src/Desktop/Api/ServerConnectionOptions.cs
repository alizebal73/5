namespace GameNet.Desktop.Api;

public sealed class ServerConnectionOptions
{
    public const string SectionName = "GameNet:Server";

    public string BaseUrl { get; init; } = "http://127.0.0.1:5080";
}
