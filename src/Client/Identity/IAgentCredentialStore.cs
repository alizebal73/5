namespace GameNet.Agent.Identity;

public interface IAgentCredentialStore
{
    Task<string?> TryLoadAsync(CancellationToken cancellationToken = default);
    Task<string> GetOrBootstrapAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(string secret, CancellationToken cancellationToken = default);
    Task<AgentPendingCredentialRotation?> TryLoadPendingRotationAsync(string deviceId, CancellationToken cancellationToken = default);
    Task SavePendingRotationAsync(Guid operationId, string deviceId, string currentSecret, string replacementSecret, CancellationToken cancellationToken = default);
    Task PromotePendingRotationAsync(Guid operationId, string deviceId, CancellationToken cancellationToken = default);
}

public sealed record AgentPendingCredentialRotation(
    Guid OperationId,
    string DeviceId,
    string CurrentSecret,
    string ReplacementSecret,
    DateTimeOffset CreatedAtUtc);
