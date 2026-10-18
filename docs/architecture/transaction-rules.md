# Transaction Rules

Authoritative mutations run inside an explicit transaction boundary owned by the application use case.

Rules:
- serializable isolation is the initial safety baseline for contention-sensitive operations;
- retry only when the operation is retry-safe and idempotent;
- no network, Agent, SignalR, filesystem or irreversible side effect inside the transaction;
- audit and Outbox records that must be atomic are written in the same transaction;
- external publication happens only after commit;
- transaction retry clears tracked state before replay.
