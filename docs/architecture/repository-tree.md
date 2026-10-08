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
│  │     ├─ Identity/
│  │     ├─ Customers/
│  │     ├─ Stations/
│  │     ├─ Agents/
│  │     ├─ Sessions/
│  │     ├─ Tariffs/
│  │     ├─ Billing/
│  │     ├─ Wallet/
│  │     ├─ Inventory/
│  │     ├─ Buffet/
│  │     ├─ VIP/
│  │     ├─ Games/
│  │     ├─ Reports/
│  │     ├─ Settings/
│  │     ├─ Approvals/
│  │     └─ Backup/
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
│  ├─ Server.IntegrationTests/
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
- A module may not reach into another module's internal implementation.
- Cross-module communication uses explicit application contracts/events.
- Server modules do not expose EF entities as API DTOs.
- Desktop feature folders own their view models/state; the shell only composes.
- Agent folders contain execution/transport concerns, not authoritative billing/session decisions.
- Installer and updater are separate deployable concerns.
