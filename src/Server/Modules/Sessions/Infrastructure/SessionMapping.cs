using GameNet.Server.Modules.Sessions.Domain;
using GameNet.Shared.Contracts.V1.Sessions;
using GameNet.Shared.Contracts.V1.Stations;
using GameNet.Shared.Primitives;

namespace GameNet.Server.Modules.Sessions.Infrastructure;

/// <summary>
/// Single place that projects a Session aggregate to its public response shape and
/// builds typed failures, shared by every Session use case.
/// </summary>
internal static class SessionMapping
{
    public static SessionResponse ToResponse(Session session) =>
        new(session.Id, session.StationId, session.CustomerId, session.TariffId,
            session.TariffCodeSnapshot, session.TariffNameSnapshot, session.TariffHourlyRateTomanSnapshot,
            session.StartedByOperatorId, session.StartedAtUtc, session.EndedByOperatorId, session.EndedAtUtc,
            session.PaidByOperatorId, session.PaidAtUtc, (SessionStatusContract)session.Status, session.Version);

    public static Result<T> Fail<T>(string code, string message) =>
        Result<T>.Failure(new Error(code, message));
}
