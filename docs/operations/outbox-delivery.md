# GameNet 5 — Outbox delivery operations

## Delivery contract

The Outbox row is written in the same PostgreSQL transaction as the business mutation it records. The delivery worker is a separate hosted service: it claims only due, un-published and non-dead-lettered rows using a database lease, routes the event by its explicit versioned type, and marks the row published only after the registered handler succeeds.

Delivery to the handler is **at-least-once**. The worker wraps each handler in an Inbox transaction keyed by the stable `EventId`: PostgreSQL's unique `outbox_inbox_receipts.event_id` ensures concurrent/replayed deliveries execute the local database operation once, while the receipt and local writes commit or roll back together. If the worker crashes after that transaction commits but before the Outbox published marker commits, replay finds the receipt, skips the handler, and safely completes the marker.

Inbox handlers may only perform local database work in that transaction; network, SignalR, file-system and other external side effects are forbidden inside it. A handler that needs a follow-up effect must append another Outbox event in the same transaction. The receiving external service (especially Agent command execution) still needs durable deduplication by a stable command ID because no local database transaction can atomically commit a remote side effect. A SignalR send or a successful worker call is not proof that an Agent performed a command exactly once.

No handler is a successful no-op: an unregistered event type is recorded as `handler_not_registered` and dead-lettered. A handler may throw `OutboxPermanentDeliveryException` for an invalid or permanently unsupported event. Other handler exceptions are recorded as the stable code `handler_failed`; exception messages, payload JSON, connection strings and secret values are intentionally not written to delivery logs.

## Persistent states

- Pending: `published_at_utc IS NULL`, `dead_lettered_at_utc IS NULL`, no active lease and no future retry delay.
- Leased: an unexpired `lease_token`/ `lease_expires_at_utc` pair belongs to the current attempt.
- Backoff: `next_attempt_at_utc` is in the future. The worker does not claim the row before that time.
- Published: `published_at_utc` is set and the lease is cleared.
- Dead-lettered: `dead_lettered_at_utc` is set after a permanent failure or the configured maximum number of attempts.
- Inbox receipt: `outbox_inbox_receipts` contains one unique `event_id` after a handler's local database operation commits. A failed handler rolls the receipt back, so a later retry can execute again.

The reliability migration adds `attempt_count`, `next_attempt_at_utc`, `last_failure_code` and `dead_lettered_at_utc`, plus a partial due-work index and the durable Inbox receipt table. Claim increments the attempt count. Failed delivery clears the lease only if the caller still owns the unexpired lease; stale workers cannot overwrite a newer worker's result. If a worker crashes at the attempt limit, the next claim transaction dead-letters the row after the last lease expires, preventing infinite reclaim loops. An Inbox receipt uses `INSERT ... ON CONFLICT DO NOTHING` as the atomic ownership check, and is written in the same transaction as handler-local writes.

## Configuration

Defaults are deliberately bounded and validated at startup:

| Setting under `GameNet:OutboxDelivery` | Default | Allowed range |
|---|---:|---:|
| `BatchSize` | 10 | 1–100 |
| `PollIntervalSeconds` | 2 | 1–60 |
| `LeaseDurationSeconds` | 120 | 10–600 |
| `MaxAttempts` | 10 | 1–100 |
| `InitialRetryDelaySeconds` | 2 | 1–300 |
| `MaximumRetryDelaySeconds` | 300 | Initial delay–3600 |

Retry uses capped exponential backoff with jitter. The lease duration must remain greater than the bounded processing time of a batch; handlers should apply explicit timeouts to remote calls.

## Read-only operational checks

Use the authorized DBA/operator connection to inspect counts without selecting the event payload:

```sql
SELECT
    count(*) FILTER (
        WHERE published_at_utc IS NULL
          AND dead_lettered_at_utc IS NULL
          AND (lease_expires_at_utc IS NULL OR lease_expires_at_utc <= now())
          AND (next_attempt_at_utc IS NULL OR next_attempt_at_utc <= now())
    ) AS ready_count,
    count(*) FILTER (
        WHERE published_at_utc IS NULL
          AND dead_lettered_at_utc IS NULL
          AND lease_expires_at_utc > now()
    ) AS leased_count,
    count(*) FILTER (
        WHERE published_at_utc IS NULL
          AND dead_lettered_at_utc IS NULL
          AND next_attempt_at_utc > now()
    ) AS retry_wait_count,
    count(*) FILTER (WHERE dead_lettered_at_utc IS NOT NULL) AS dead_letter_count
FROM outbox_messages;
```

Inspect recent failures using only safe metadata:

```sql
SELECT id, event_id, type, attempt_count, next_attempt_at_utc,
       last_failure_code, dead_lettered_at_utc
FROM outbox_messages
WHERE dead_lettered_at_utc IS NOT NULL
ORDER BY dead_lettered_at_utc DESC
LIMIT 100;
```

Do not include `payload_json` in support logs, tickets or screenshots. Dead-letter replay/admin tooling is not included in this slice; do not manually clear flags or replay rows in production. Build a reviewed recovery path before using Outbox for high-impact business or Agent commands.

## Retention and release follow-up

Outbox rows and Inbox receipts intentionally have no automatic purge in this slice. Deleting an Inbox receipt can make an old replay execute again, so receipt retention must exceed every supported retry/replay window. Before production, define database growth monitoring, archiving/retention, controlled dead-letter replay and operator audit. Until those policies exist, do not delete Inbox receipts or dead-letter rows manually. The default safe behavior is to retain them.


## Acceptance required before async-dependent features

The disposable PostgreSQL lane must prove concurrent claims do not return the same message, concurrent duplicate EventIds execute a local handler once, failed handler transactions do not leave receipts, only the current lease can complete/fail a row, retry delay is respected, attempt limits produce a dead letter, and published/dead-lettered rows are not re-delivered. The exact candidate SHA must pass canonical build/tests and clean-migration/model-drift checks. Physical Windows-service/LAN acceptance remains a separate gate.

Before Session-to-Agent command dispatch is added, its handler and Agent-side consumer must provide stable command identity, idempotent execution/acknowledgement, stale-lease rejection and crash/reconnect tests. The generic Outbox worker alone does not satisfy those requirements.
