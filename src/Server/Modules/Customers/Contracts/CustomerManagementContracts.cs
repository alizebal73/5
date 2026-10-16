namespace GameNet.Server.Modules.Customers.Contracts;

public sealed record CustomerActorContext(Guid OperatorId, string CorrelationId, string Source, IReadOnlySet<string> Permissions);
public sealed record CreateCustomerCommand(string Code, string DisplayName, string? Nickname, string? PhoneNumber, string IdempotencyKey, CustomerActorContext Actor);
public sealed record UpdateCustomerProfileCommand(Guid CustomerId, string DisplayName, string? Nickname, string? PhoneNumber, int ExpectedVersion, string IdempotencyKey, CustomerActorContext Actor);
public sealed record SetCustomerStatusCommand(Guid CustomerId, GameNet.Shared.Contracts.V1.Customers.CustomerStatusContract Status, int ExpectedVersion, string Reason, string IdempotencyKey, CustomerActorContext Actor);
