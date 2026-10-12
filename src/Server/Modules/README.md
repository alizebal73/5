# Canonical Server Module Inventory

This is the single module catalog enforced by `scripts/check-architecture.ps1`. A listed module boundary is not a claim that its business capability is implemented. Missing modules start with ownership documentation only; code enters only through a Ready, requirement-tracked vertical slice.

| Module | Owns | Explicitly does not own |
|---|---|---|
| Identity | Operator accounts, credentials, roles, permissions, operator auth sessions | Customer profile, wallet, station occupancy |
| Stations | Stable station identity/code, type (PC/PS5/Foosball), administrative status | Agent connectivity, Session ownership, billing |
| Agents | Device identity/enrollment, device credential lifecycle, heartbeat, lease/fencing, reconnect/reconciliation | Authoritative Session/billing rules or operator command policy |
| ClientControl | Server-authorized PC command policy, command lifecycle/acknowledgement and operator-visible outcome | Agent identity/lease storage or unsupported hardware behavior |
| Customers | Customer profile/code, optional customer PIN, activation/deactivation and profile history | Wallet/debt ledger, active Session, station ownership |
| Sessions | Start/pause/resume/extend/stop/transfer/release/recovery state and ownership invariants | Pricing authority or wallet ledger |
| Tariffs | PC/PS5/Foosball rate plans, tariff matching and immutable price snapshots | Final payment collection or wallet balance |
| Billing | Bill preview/checkout, invoices, settlement/payment allocation, pending settlement, refund/reversal orchestration | Wallet balance projection or physical stock |
| Wallet | Stored-value/free-money/free-time ledgers, debt and debt settlement, balance projection | Actual cash/card receipt source of truth or pending invoice state |
| Inventory | Stock locations, stock movements/ledger, purchases, counts, waste, returns and corrections | Buffet order lifecycle or sales invoice ownership |
| Buffet | Order/sale lifecycle and orchestration with Billing and Inventory | Independent stock or payment balances |
| VIP | Plan lifecycle, eligibility, entitlements, usage limits and usage | Duplicate tariff/billing engine |
| Games | Game catalog, install metadata and Server-authorized launch policy | Direct client-side launch authority or shared credential storage |
| GameAccounts | Shared game-account pool, account/session/station leases and safe release/recovery | Password display to operators/clients or game catalog ownership |
| Reports | Read-only reporting queries/read models sourced from authoritative modules/ledgers | Mutations or a second source of truth |
| Shifts | Cash opening, expected-vs-counted cash, reconciliation and shift close/handover | Recalculating original invoices or editing ledger history |
| Settings | Validated, versioned Server-backed operational settings/policy | Owning another module's domain rules |
| Approvals | Requester/approver/reason/expiry/state for actions requiring extra authorization | Performing the approved business mutation without its owning module |
| Backup | Backup jobs, verification and restore orchestration/evidence | Automatic unreviewed production migration or recovery decisions outside Server policy |

## Cross-cutting platform (not business modules)

- Persistence and schema migrations
- Transactions and concurrency policy
- Authentication/authorization primitives and current actor context
- Audit, idempotency, Outbox/Inbox
- API/Shared V1 contracts, typed errors and contract versioning
- Time/money primitives, correlation, logging, health/readiness and diagnostics
- Release/installer/updater and platform-specific secret/TLS integration

## Capability ownership decisions

- **Operator users are inside Identity; no separate Users module.** Customer profiles and customer PINs are inside Customers; they are not operator accounts.
- **ClientControl is separate from Agents.** Agents authenticate devices and report connectivity; ClientControl decides whether a command is authorized and tracks its result.
- **Game Account Pool is the GameAccounts module**, dependent on Games, Sessions, Agents and audited secret handling. It must not be implemented until ADR-0003 is accepted and its secret/recovery policy is ready.
- **Shift/Cash is the Shifts module**, distinct from read-only Reports.
- **Discounts/promotions are an initial Billing/Tariffs capability, not a separate module** until a real boundary requires one.
- **Network policy** is validated/configured by Settings and executed only by ClientControl through a capable Agent. No router/PS5/Foosball behavior is fabricated.
- **Recovery/reconciliation is owned by the domain that owns the state**; Backup orchestrates backup/restore, while Agents and Sessions reconcile their own state.
- Tests live in root `tests/` projects under module-specific paths; no dummy test project or fake endpoint is created just to make a folder appear.

## Entry/exit rules

Every module slice must satisfy [Definition of Ready](../../../docs/planning/definition-of-ready.md) before implementation and [Definition of Done](../../../docs/planning/definition-of-done.md) before closure. Implement Domain/Application/Infrastructure/Api only when the slice requires them. Do not add unused interfaces, fake behavior or placeholder-success endpoints. Cross-module collaboration uses explicit contracts/events; the architecture guard rejects references to another module's implementation namespace.
