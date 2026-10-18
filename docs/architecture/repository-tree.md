# Target Repository Tree

This is the intended long-term shape. It is a design target, not permission to fill every folder with code immediately.

```text
GameNet 5/
├─ .github/
│  └─ workflows/
├─ .config/
├─ docs/
│  ├─ adr/
│  ├─ architecture/
│  ├─ development/
│  ├─ domain/
│  ├─ operations/
│  ├─ planning/
│  ├─ release/
│  ├─ security/
│  └─ testing/
├─ installer/
│  ├─ Setup/
│  └─ Update/
├─ scripts/
├─ src/
│  ├─ Shared/
│  │  ├─ Contracts/
│  │  │  └─ V1/
│  │  └─ Primitives/
│  ├─ Server/
│  │  ├─ Composition/
│  │  ├─ Configuration/
│  │  ├─ Infrastructure/
│  │  │  ├─ Audit/
│  │  │  ├─ Idempotency/
│  │  │  ├─ Outbox/
│  │  │  ├─ Persistence/
│  │  │  ├─ Security/
│  │  │  └─ Transactions/
│  │  ├─ Observability/
│  │  ├─ Persistence/
│  │  └─ Modules/
│  │     ├─ Identity/          # operator users, roles, permissions
│  │     ├─ Stations/          # station identity/type/administrative state
│  │     ├─ Agents/            # device identity, enrollment, lease, heartbeat
│  │     ├─ ClientControl/     # authorized commands sent through Agents
│  │     ├─ Customers/         # customer profile and customer PIN
│  │     ├─ Sessions/          # authoritative use/session lifecycle and ownership
│  │     ├─ Tariffs/           # tariff rules and tariff resolution
│  │     ├─ Billing/           # invoices, settlement/payment allocation, reversals
│  │     ├─ Wallet/            # wallet/free value/free time/debt ledgers
│  │     ├─ Inventory/         # stock locations and stock ledger
│  │     ├─ Buffet/            # sales/orders; coordinates Billing and Inventory
│  │     ├─ VIP/               # plans, entitlements and consumption
│  │     ├─ Games/             # catalog and Server-authorized launch policy
│  │     ├─ GameAccounts/      # shared account pool and account leases
│  │     ├─ Reports/           # read models only; never business authority
│  │     ├─ Shifts/            # cash opening, reconciliation and close
│  │     ├─ Settings/          # versioned Server-backed operational policy
│  │     ├─ Approvals/         # privileged-action requests and decisions
│  │     └─ Backup/            # backup/restore orchestration and evidence
│  ├─ Desktop/
│  │  ├─ Shell/
│  │  ├─ Features/
│  │  │  ├─ Overview/
│  │  │  ├─ Stations/
│  │  │  ├─ Customers/
│  │  │  ├─ Sessions/
│  │  │  ├─ Billing/
│  │  │  ├─ Wallet/
│  │  │  ├─ Inventory/
│  │  │  ├─ Buffet/
│  │  │  ├─ VIP/
│  │  │  ├─ Reports/
│  │  │  ├─ Agents/
│  │  │  ├─ Settings/
│  │  │  ├─ Approvals/
│  │  │  ├─ Backup/
│  │  │  ├─ Audit/
│  │  │  └─ Diagnostics/
│  │  ├─ Infrastructure/
│  │  │  ├─ Api/
│  │  │  ├─ Authentication/
│  │  │  ├─ Localization/
│  │  │  ├─ Notifications/
│  │  │  └─ Updates/
│  │  └─ Resources/
│  │     ├─ fa-IR/
│  │     └─ en-US/
│  ├─ Client/
│  │  └─ Agent/
│  │     ├─ Identity/
│  │     ├─ Transport/
│  │     ├─ Lease/
│  │     ├─ Heartbeat/
│  │     ├─ Reconciliation/
│  │     ├─ Execution/
│  │     └─ Recovery/
│  └─ Updater/
│     ├─ Download/
│     ├─ Verification/
│     ├─ Staging/
│     ├─ Switch/
│     ├─ Health/
│     └─ Rollback/
├─ tests/
│  ├─ Architecture/
│  ├─ Shared.Tests/
│  ├─ Server.UnitTests/
│  │  ├─ Infrastructure/
│  │  └─ Modules/<Module>/
│  ├─ Server.IntegrationTests/ # add when DB-backed suites need a separate project
│  ├─ ContractTests/
│  ├─ Desktop.Tests/
│  ├─ Desktop.IntegrationTests/
│  ├─ Agent.Tests/
│  ├─ Agent.IntegrationTests/
│  └─ ReleaseTests/
├─ Directory.Build.props
├─ Directory.Packages.props
├─ GameNet.slnx
└─ README.md
```

## Structural rules

- Composition files register services only.
- The canonical Server module inventory is enforced by `scripts/check-architecture.ps1`; each module has a root README documenting ownership.
- A module may not reach into another module's internal implementation.
- `scripts/check-module-layer-boundaries.ps1` enforces layer-aware dependency rules: Domain/Application/API cannot access EF Core, Npgsql or Server.Persistence directly; Domain/Application also cannot own outbound network, filesystem, process or ASP.NET framework work.
- Infrastructure is intentionally allowed to implement persistence and external integrations. API is an inbound transport boundary: ASP.NET types are allowed there, but direct persistence access is not.
- `scripts/test-module-layer-boundaries.ps1` exercises both allowed Infrastructure/API examples and forbidden Domain/Application/API dependencies; `scripts/verify.ps1` runs these regression checks.
- Cross-module communication uses explicit application contracts/events.
- Unit/contract/DB-backed tests stay in root `tests/` projects under module-specific paths; do not create nested production test projects inside each module.
- Server modules do not expose EF entities as API DTOs.
- Desktop feature folders own their view models/state; the shell only composes.
- Agent folders contain execution/transport concerns, not authoritative billing/session decisions.
- Installer and updater are separate deployable concerns.
