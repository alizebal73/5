# Pre-Business Architecture Locks

These decisions are mandatory before the first business vertical slice is merged.

## 1. First-run bootstrap and operator identity

The installer/Server must have a first-run bootstrap path.

Required lifecycle:

1. Server starts with no application operator.
2. Setup creates or guides creation of the first owner/admin account.
3. Initial credential is never hard-coded.
4. Passwords are hashed with an approved password-hashing mechanism; plaintext is never persisted.
5. Bootstrap token/setup secret is one-time and revocable.
6. After the first admin is created, bootstrap mode closes.
7. Login/session tokens have explicit expiry and revocation behavior.
8. Credential changes, resets, lockouts and permission changes are audited.

The Desktop must never contain a hidden default admin username/password.

## 2. Authoritative time model

Two time concepts are required:

- Wall-clock UTC: persisted event timestamps, audit, reporting and business calendar interpretation.
- Monotonic elapsed time: measuring durations that must not change when Windows system time is adjusted.

The Session timing service must use TimeProvider timestamp/elapsed-time capabilities for duration measurement and IGameClock for UTC timestamps.

A Session never trusts a client-reported elapsed time as authoritative billing time.

## 3. API contract enforcement

V1 is not only a DTO namespace.

The Server must enforce:

- explicit V1 route space;
- contract version header validation where required;
- stable error envelope;
- correlation ID;
- typed authorization failures;
- no exception-message matching in endpoint logic.

Every API mutation declares its permission and idempotency requirements.

## 4. Health/readiness contract

Status strings are centralized in Shared contracts.

Health and readiness are distinct:

- Health: process is responding.
- Readiness: process can serve authoritative requests.

No endpoint or client may invent equivalent strings such as ok, Healthy, ready independently.

## 5. Authentication key and machine secret lifecycle

Production signing keys and machine credentials:

- are generated or supplied at installation;
- are stored outside source control;
- are never logged;
- can be rotated;
- have explicit ownership and backup/recovery behavior.

The installer/setup flow must not require editing source files.

## 6. Agent command authority

All Agent commands follow:

Server authorization -> command ID -> lease validation -> Agent execution -> result/receipt -> Server reconciliation.

Agent command IDs are idempotent.

Agent capabilities are explicit. A capability not granted by Server cannot be executed because a client asks for it.

Examples to model:

- lock/unlock workstation;
- logoff;
- shutdown/restart;
- launch/close approved application/game;
- network-profile operation where explicitly enabled;
- display/maintenance commands.

Privileged OS operations are auditable.

## 7. Station model

Station has business identity and configuration.

Agent has device identity.

Session has occupancy/usage identity.

Agent connection is a transient transport fact.

These four must never be collapsed into one status field.

## 8. Financial promotion semantics

The wallet example "100,000 + 10% free = 110,000 usable" is a ledger bonus/credit rule, not a hidden UI price calculation.

The product must distinguish:

- cash top-up;
- promotional bonus;
- session charge;
- debt;
- refund;
- correction.

Promotion rules specify eligibility, calculation, expiry when applicable, maximums and audit metadata.

## 9. Database lifecycle

Before business migrations:

- design-time DbContext creation must work;
- clean database migration must work;
- upgrade migration test exists;
- production startup does not silently migrate;
- schema version is observable;
- destructive migration requires compatibility/approval evidence.

## 10. Operator audit boundary

Audit records must include, where relevant:

- actor;
- action;
- target type/id;
- timestamp;
- correlation ID;
- reason;
- old/new summary;
- outcome;
- source (Desktop/Agent/Server);
- command/idempotency key for mutation operations.

Sensitive secrets are excluded from audit payloads.
