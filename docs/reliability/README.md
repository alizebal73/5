# Reliability Rules

Realtime transport is not authoritative state.

Agent identity: durable DeviceId. Connection identity: ephemeral ConnectionId. Stale connections: fenced by a persistent LeaseToken.

Money, inventory and Session mutations have stable idempotency scopes. Restart, timeout, disconnect and duplicate delivery all reconcile from Server state.
