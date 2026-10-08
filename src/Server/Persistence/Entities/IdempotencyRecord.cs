namespace GameNet.Server.Persistence.Entities;

public sealed class IdempotencyRecord
{
    public long Id { get; set; }
    public string Scope { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string Operation { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string LeaseToken { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string ResponseJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset LeaseExpiresAtUtc { get; set; }
    public DateTimeOffset ExpiresAtUtc { get; set; }
}
