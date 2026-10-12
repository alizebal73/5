# GameNet Manager 5

Clean-slate Windows GameNet/CyberCafe management platform.

## Runtime boundaries
- Server: authoritative ASP.NET Core application.
- Desktop: native WPF operator/admin application.
- Agent: Windows Service on client PCs.
- Shared: versioned contracts and platform primitives.
- PostgreSQL: authoritative Server persistence.

## Non-negotiable rules
- Desktop and Agent never access PostgreSQL directly.
- Server owns business truth, money, Session ownership, inventory, permissions and recovery decisions.
- Shared V1 contracts are the single transport-contract authority.
- Business rules never live in API endpoints, WPF code-behind or Agent transport code.
- Money and inventory are ledger-first.
- Session, Station and Agent connection state are separate concepts.
- Financial, inventory, Session and privileged mutations are transactional, audited and idempotent.
- Realtime transport is not business authority; reconnect always reconciles against Server state.
- No business vertical slice merges before Foundation Certification.
- Every defect leaves permanent regression evidence.
- No browser dashboard runtime or Node UI toolchain.

## Build order
Foundation/automated Runtime certification -> requirement-tracked product vertical slices -> real Setup.exe/MSI candidate -> final physical Windows Service/LAN and interactive UI acceptance -> pilot/release.

Automated CI, unit/integration/regression/security tests continue during development. Physical two-PC acceptance and the final installed-UI smoke are intentionally deferred until the planned product scope and Setup candidate are complete.

No business feature is implemented in the Foundation baseline.
