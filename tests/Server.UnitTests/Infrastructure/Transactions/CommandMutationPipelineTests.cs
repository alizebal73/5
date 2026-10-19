using GameNet.Server.Infrastructure.Idempotency;
using GameNet.Server.Infrastructure.Transactions;
using GameNet.Shared.Primitives;
using Xunit;

namespace GameNet.Server.UnitTests.Infrastructure.Transactions;

public sealed class CommandMutationPipelineTests
{
    [Fact]
    public async Task Successful_command_is_audited_and_completed_inside_transaction_then_replayed_once()
    {
        var transactions = new RecordingTransactionCoordinator();
        var auditWrites = 0;
        var idempotency = new MemoryIdempotencyStore(
            () => transactions.IsActive,
            () => auditWrites > 0);
        var pipeline = new CommandMutationPipeline(transactions, idempotency);
        var actions = 0;

        var first = await pipeline.ExecuteAsync(
            "test.scope", "test.create", "intent-1", "fingerprint-1", 201,
            TimeSpan.FromSeconds(30), TimeSpan.FromDays(1),
            _ =>
            {
                Assert.True(transactions.IsActive);
                actions++;
                auditWrites++;
                return Task.FromResult(Result<string>.Success("created-1"));
            });

        var replay = await pipeline.ExecuteAsync(
            "test.scope", "test.create", "intent-1", "fingerprint-1", 201,
            TimeSpan.FromSeconds(30), TimeSpan.FromDays(1),
            _ =>
            {
                actions++;
                return Task.FromResult(Result<string>.Success("created-2"));
            });

        Assert.True(first.IsSuccess);
        Assert.Equal("created-1", first.Value);
        Assert.True(replay.IsSuccess);
        Assert.Equal("created-1", replay.Value);
        Assert.Equal(1, actions);
        Assert.Equal(1, auditWrites);
        Assert.Equal(1, idempotency.CompletionCount);
        Assert.Equal(2, transactions.CommitCount);
        Assert.Equal(0, transactions.RollbackCount);
        Assert.True(idempotency.CompletionOccurredInsideTransaction);
        Assert.True(idempotency.AuditWasWrittenBeforeCompletion);
    }

    [Fact]
    public async Task Reusing_key_for_different_payload_returns_conflict_without_repeating_action()
    {
        var transactions = new RecordingTransactionCoordinator();
        var idempotency = new MemoryIdempotencyStore(() => transactions.IsActive);
        var pipeline = new CommandMutationPipeline(transactions, idempotency);
        var actions = 0;

        var first = await pipeline.ExecuteAsync(
            "test.scope", "test.update", "intent-2", "fingerprint-before", 200,
            TimeSpan.FromSeconds(30), TimeSpan.FromDays(1),
            _ =>
            {
                actions++;
                return Task.FromResult(Result<string>.Success("updated"));
            });

        var conflict = await pipeline.ExecuteAsync(
            "test.scope", "test.update", "intent-2", "fingerprint-after", 200,
            TimeSpan.FromSeconds(30), TimeSpan.FromDays(1),
            _ =>
            {
                actions++;
                return Task.FromResult(Result<string>.Success("should-not-run"));
            });

        Assert.True(first.IsSuccess);
        Assert.False(conflict.IsSuccess);
        Assert.Equal("idempotency.key_reused", conflict.Error.Code);
        Assert.Equal(1, actions);
    }

    [Fact]
    public async Task In_flight_duplicate_is_rejected_before_business_action()
    {
        var transactions = new RecordingTransactionCoordinator();
        var idempotency = new MemoryIdempotencyStore(() => transactions.IsActive);
        idempotency.SeedInFlight("test.scope", "intent-3", "test.start", "fingerprint-3");
        var pipeline = new CommandMutationPipeline(transactions, idempotency);
        var actions = 0;

        var result = await pipeline.ExecuteAsync(
            "test.scope", "test.start", "intent-3", "fingerprint-3", 200,
            TimeSpan.FromSeconds(30), TimeSpan.FromDays(1),
            _ =>
            {
                actions++;
                return Task.FromResult(Result<string>.Success("unexpected"));
            });

        Assert.False(result.IsSuccess);
        Assert.Equal("idempotency.in_flight", result.Error.Code);
        Assert.Equal(0, actions);
        Assert.Equal(1, transactions.RollbackCount);
        Assert.Equal(0, idempotency.CompletionCount);
    }

    [Fact]
    public async Task Failed_business_action_rolls_back_and_does_not_complete_idempotency_record()
    {
        var transactions = new RecordingTransactionCoordinator();
        var idempotency = new MemoryIdempotencyStore(() => transactions.IsActive);
        var pipeline = new CommandMutationPipeline(transactions, idempotency);

        var result = await pipeline.ExecuteAsync(
            "test.scope", "test.end", "intent-4", "fingerprint-4", 200,
            TimeSpan.FromSeconds(30), TimeSpan.FromDays(1),
            _ => Task.FromResult(Result<string>.Failure(new Error("session.invalid_transition", "Session cannot end."))));

        Assert.False(result.IsSuccess);
        Assert.Equal("session.invalid_transition", result.Error.Code);
        Assert.Equal(0, idempotency.CompletionCount);
        Assert.Equal(0, transactions.CommitCount);
        Assert.Equal(1, transactions.RollbackCount);
    }

    [Fact]
    public async Task Missing_idempotency_key_is_rejected_without_opening_transaction()
    {
        var transactions = new RecordingTransactionCoordinator();
        var idempotency = new MemoryIdempotencyStore(() => transactions.IsActive);
        var pipeline = new CommandMutationPipeline(transactions, idempotency);

        var result = await pipeline.ExecuteAsync(
            "test.scope", "test.create", " ", "fingerprint-5", 201,
            TimeSpan.FromSeconds(30), TimeSpan.FromDays(1),
            _ => Task.FromResult(Result<string>.Success("unexpected")));

        Assert.False(result.IsSuccess);
        Assert.Equal("idempotency.required", result.Error.Code);
        Assert.Equal(0, transactions.CommitCount);
        Assert.Equal(0, transactions.RollbackCount);
    }

    [Fact]
    public async Task Explicit_isolation_level_is_passed_to_the_transaction_boundary()
    {
        var transactions = new RecordingTransactionCoordinator();
        var idempotency = new MemoryIdempotencyStore(() => transactions.IsActive);
        var pipeline = new CommandMutationPipeline(transactions, idempotency);

        var result = await pipeline.ExecuteAsync(
            "test.scope", "test.create", "intent-isolation", "fingerprint-isolation", 200,
            TimeSpan.FromSeconds(30), TimeSpan.FromDays(1),
            _ => Task.FromResult(Result<string>.Success("created")),
            CancellationToken.None,
            System.Data.IsolationLevel.ReadCommitted);

        Assert.True(result.IsSuccess);
        Assert.Equal(System.Data.IsolationLevel.ReadCommitted, transactions.LastIsolationLevel);
    }

    private sealed class RecordingTransactionCoordinator : ITransactionCoordinator
    {
        public bool IsActive { get; private set; }
        public int CommitCount { get; private set; }
        public int RollbackCount { get; private set; }
        public System.Data.IsolationLevel LastIsolationLevel { get; private set; } = System.Data.IsolationLevel.Serializable;

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> action,
            CancellationToken cancellationToken = default,
            System.Data.IsolationLevel isolationLevel = System.Data.IsolationLevel.Serializable)
        {
            LastIsolationLevel = isolationLevel;
            Assert.False(IsActive);
            IsActive = true;
            try
            {
                var result = await action(cancellationToken);
                CommitCount++;
                return result;
            }
            catch
            {
                RollbackCount++;
                throw;
            }
            finally
            {
                IsActive = false;
            }
        }
    }

    private sealed class MemoryIdempotencyStore(
        Func<bool> transactionIsActive,
        Func<bool>? auditHasBeenWritten = null) : IIdempotencyStore
    {
        private readonly Dictionary<(string Scope, string Key), Entry> entries = new();

        public int CompletionCount { get; private set; }
        public bool CompletionOccurredInsideTransaction { get; private set; }
        public bool AuditWasWrittenBeforeCompletion { get; private set; }

        public void SeedInFlight(string scope, string key, string operation, string requestHash) =>
            entries[(scope, key)] = new Entry(operation, requestHash, "seeded-lease");

        public Task<IdempotencyClaim> TryClaimAsync(
            string scope,
            string key,
            string operation,
            string requestHash,
            TimeSpan leaseDuration,
            TimeSpan retention,
            CancellationToken cancellationToken = default)
        {
            Assert.True(transactionIsActive());
            var recordKey = (scope, key);
            if (entries.TryGetValue(recordKey, out var existing))
            {
                if (!string.Equals(existing.Operation, operation, StringComparison.Ordinal) ||
                    !string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
                    throw new IdempotencyKeyConflictException();

                return Task.FromResult(existing.Completed
                    ? new IdempotencyClaim(false, true, false, string.Empty, existing.StatusCode, existing.ResponseJson)
                    : new IdempotencyClaim(false, false, true, string.Empty, null, null));
            }

            var entry = new Entry(operation, requestHash, "lease-" + (entries.Count + 1));
            entries.Add(recordKey, entry);
            return Task.FromResult(new IdempotencyClaim(true, false, false, entry.LeaseToken, null, null));
        }

        public Task CompleteAsync(
            string scope,
            string key,
            string leaseToken,
            int statusCode,
            string responseJson,
            CancellationToken cancellationToken = default)
        {
            Assert.True(transactionIsActive());
            var entry = entries[(scope, key)];
            Assert.Equal(entry.LeaseToken, leaseToken);
            CompletionOccurredInsideTransaction = true;
            if (auditHasBeenWritten is not null)
                AuditWasWrittenBeforeCompletion = auditHasBeenWritten();
            entry.Completed = true;
            entry.StatusCode = statusCode;
            entry.ResponseJson = responseJson;
            CompletionCount++;
            return Task.CompletedTask;
        }

        private sealed class Entry(string operation, string requestHash, string leaseToken)
        {
            public string Operation { get; } = operation;
            public string RequestHash { get; } = requestHash;
            public string LeaseToken { get; } = leaseToken;
            public bool Completed { get; set; }
            public int? StatusCode { get; set; }
            public string? ResponseJson { get; set; }
        }
    }
}
