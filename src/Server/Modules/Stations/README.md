# Stations

Station administration and live connection visibility only.
- Administrative status belongs to the operator.
- Online/offline is derived from the Server-owned Agent lease expiry.
- Device binding requires a provisioned, active Agent credential and is unique.
- Mutations require expected Version, request-hash idempotency, and transactional audit.
- Session/billing/launch decisions are intentionally outside this slice.
