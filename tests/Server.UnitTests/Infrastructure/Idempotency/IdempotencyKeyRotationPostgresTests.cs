using System.Text.Json;
using GameNet.Server.Infrastructure.Idempotency;
using GameNet.Server.Infrastructure.Security;
using GameNet.Server.Persistence;
using GameNet.Shared.Primitives;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace GameNet.Server.UnitTests;

/// <summary>
/// Exercises replay compatibility against the disposable PostgreSQL certification database.
/// </summary>
[Collection("Idempotency PostgreSQL serial")]
public sealed class IdempotencyKeyRotationPostgresTests
{
    [Fact]
    public async Task Completed_and_in_progress_idempotency_records_survive_jwt_signing_key_rotation()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable("GAMENET_IDEMPOTENCY_POSTGRES_TESTS"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var connectionString = Environment.GetEnvironmentVariable("GAMENET_DATABASE_CONNECTION");
        Assert.False(string.IsNullOrWhiteSpace(connectionString));

        var now = DateTimeOffset.UtcNow;
        var clock = new FixedClock(now);
        const string scope = "idempotency.jwt-rotation-test";
        var completedKey = "rotation-completed-" + Guid.NewGuid().ToString("N");
        var inProgressKey = "rotation-in-progress-" + Guid.NewGuid().ToString("N");
        const string operation = "test.jwt-rotation";
        const string responseJson = "{\"result\":\"recorded\"}";
        var payload = new { Operation = operation, Password = "Sensitive-Test-Password-42" };
        var dedicatedFingerprintKey = Key('f');

        // Simulate a JWT signing-key rotation while retaining the same independently
        // protected idempotency key in the Server secret store.
        var beforeRotation = ServerSecretMaterial.Create(
            connectionString, Key('a'), Key('b'), dedicatedFingerprintKey);
        var afterRotation = ServerSecretMaterial.Create(
            connectionString, Key('c'), Key('d'), dedicatedFingerprintKey);
        var oldFingerprint = new HmacRequestFingerprint(beforeRotation).Compute(payload);
        var newFingerprint = new HmacRequestFingerprint(afterRotation).Compute(payload);
        Assert.Equal(oldFingerprint, newFingerprint);

        var options = new DbContextOptionsBuilder<GameNetDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        await using var db = new GameNetDbContext(options);
        var store = new EfIdempotencyStore(db, clock);
        var lease = TimeSpan.FromSeconds(30);
        var retention = TimeSpan.FromDays(1);

        try
        {
            var completedClaim = await store.TryClaimAsync(
                scope, completedKey, operation, oldFingerprint, lease, retention);
            Assert.True(completedClaim.Claimed);
            Assert.False(string.IsNullOrWhiteSpace(completedClaim.LeaseToken));

            await store.CompleteAsync(
                scope, completedKey, completedClaim.LeaseToken, 200, responseJson);

            var replay = await store.TryClaimAsync(
                scope, completedKey, operation, newFingerprint, lease, retention);
            Assert.True(replay.Completed);
            Assert.Equal(200, replay.StatusCode);
            using var replayJson = JsonDocument.Parse(replay.ResponseJson!);
            Assert.Equal("recorded", replayJson.RootElement.GetProperty("result").GetString());

            var inProgressClaim = await store.TryClaimAsync(
                scope, inProgressKey, operation, oldFingerprint, lease, retention);
            Assert.True(inProgressClaim.Claimed);

            var inProgressRetry = await store.TryClaimAsync(
                scope, inProgressKey, operation, newFingerprint, lease, retention);
            Assert.True(inProgressRetry.InFlight);
            Assert.False(inProgressRetry.Claimed);
            Assert.False(inProgressRetry.Completed);
        }
        finally
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"DELETE FROM idempotency_records WHERE scope = {scope} AND key IN ({completedKey}, {inProgressKey})");
        }
    }

    private static string Key(char value) => new(value, 64);

    private sealed class FixedClock(DateTimeOffset utcNow) : IGameClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
        public GameClockSnapshot Capture() => new(
            UtcNow,
            System.Diagnostics.Stopwatch.GetTimestamp(),
            System.Diagnostics.Stopwatch.Frequency,
            Environment.TickCount64,
            Guid.Parse("04ad13fd-6cb2-4b17-a54d-5e0a48f3e9f0"));
    }
}
