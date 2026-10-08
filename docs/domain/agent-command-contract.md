# Agent Command Contract

Agent is an execution boundary, not a business authority.

Every command contains:

- CommandId;
- DeviceId;
- StationId;
- LeaseToken/version;
- command type;
- issued-at UTC;
- expiry/timeout;
- payload;
- required capability.

Execution result contains:

- CommandId;
- DeviceId;
- started/finished UTC;
- status;
- normalized error code;
- evidence/receipt where needed.

## Rules

- Server authorizes command.
- Server verifies current Agent lease before dispatch.
- Agent rejects commands for an invalid/stale lease.
- Agent de-duplicates CommandId.
- Agent executes only declared capabilities.
- Results are returned to Server.
- Server reconciles authoritative state.
- External side effects are not treated as committed merely because a command was sent.

## Initial PC capabilities

- lock/unlock;
- logoff;
- restart;
- shutdown;
- launch approved executable/game;
- close approved executable/game;
- display/maintenance control;
- optional network-profile operation behind explicit policy.

Each capability requires permission and audit policy.
