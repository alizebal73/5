# Recovery Requirements

Before release the product must prove:
- Server restart with active Session;
- Agent restart and reconnect;
- duplicate command after timeout;
- repeated payment request with the same idempotency key;
- inventory commit with lost acknowledgement;
- database restore into an isolated target;
- update failure followed by rollback.

Each scenario records authority, duplicate behavior, reconciliation direction, audit impact and operator-visible status.
