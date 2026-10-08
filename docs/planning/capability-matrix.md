# GameNet 5 Capability Matrix

| Area | Required capabilities | Authority | UI | Priority |
|---|---|---|---|---|
| Identity | operator login, credentials, roles, permissions | Server | Login / Settings | P0 |
| Stations | PC/PS5/Foosball inventory, states, pricing links | Server | Stations | P0 |
| Agents | durable device identity, pairing, heartbeat, lease, reconnect | Server | Agents / Stations | P0 |
| Customers | ID/PIN/profile/search/balance/debt/history | Server | Customers | P0 |
| Sessions | start/stop/pause/recover/transfer/release | Server | Stations / Sessions | P0 |
| Tariffs | PC, PS5, Foosball tariffs, time rules, promotions | Server | Settings / Billing | P0 |
| Billing | preview, charge, settlement, receipts/records | Server | Billing | P0 |
| Wallet | top-up, debit, payment, debt, adjustments, ledger | Server | Wallet / Customers | P0 |
| Discounts | controlled promotions and permissions | Server | Billing / Settings | P1 |
| VIP | plans, duration, entitlements, lifecycle | Server | VIP / Customers | P1 |
| Inventory | products, receipts, sales, returns, waste, counts, corrections | Server | Inventory | P1 |
| Buffet | item sales and settlement linkage | Server | Buffet / Billing | P1 |
| Games | game catalog / allowed station metadata where needed | Server | Settings / Stations | P2 |
| Reports | revenue, sessions, wallet, inventory, VIP, operators, audit | Server DB | Reports | P1 |
| Settings | business/site/tariff/session/backup/update settings | Server | Settings | P1 |
| Approvals | privileged action approval workflows | Server | Approvals | P1 |
| Audit | actor/action/time/target/correlation/reason | Server | Audit | P0 |
| Backup | create/verify/restore/recovery status | Server/ops | Backup & Recovery | P0 |
| Diagnostics | health, agents, outbox, reconciliation, errors | Server | Diagnostics | P0 |
| Setup | install/configure/services/firewall/shortcuts | Installer | Setup | P0 |
| Update | signed package, staging, migration gate, rollback | Updater | Update UI + installer | P0 |
| Recovery | restart/reconcile/restore/rollback | Server/ops | Diagnostics / Backup | P0 |
| Localization | fa-IR RTL + en-US resources | Desktop | All screens | P0 |
| Observability | correlation, structured logs, readiness/health | Server | Diagnostics | P0 |

## Business invariants that must never be delegated to UI

- wallet balance
- debt
- final session charge
- station/session ownership
- Agent lease ownership
- stock balance
- VIP eligibility
- discount entitlement
- approval authorization
- audit record truth

## Initial product boundary

Not first-release goals:

- multi-tenant SaaS;
- microservice decomposition;
- browser operator dashboard;
- client-side database replication;
- uncontrolled plugins;
- speculative distributed infrastructure.
