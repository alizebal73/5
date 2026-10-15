# GameNet Manager 5

Windows-first GameNet/CyberCafe management platform built around a native WPF operator desktop, an ASP.NET Core Server, Windows Agent services and PostgreSQL.

## Runtime boundaries

- **Server:** authority for business rules, permissions, Sessions, tariffs, billing, audit and recovery.
- **Desktop:** native WPF operator/admin application; calls Server APIs.
- **Agent:** Windows service on client PCs; executes authorized station commands.
- **Shared:** versioned API contracts and platform primitives.
- **PostgreSQL:** authoritative persistent state, accessed by Server only.

Desktop and Agent must never connect directly to PostgreSQL. Business rules must not live in API endpoints, WPF code-behind or Agent transport code. Money/inventory are ledger-first. Session state, administrative Station state and Agent connectivity are separate concepts. Sensitive mutations are transactional, audited and idempotent. Realtime messages are not business authority; reconnect reconciles against Server state.

## Current delivery state — 2026-10-15

Product work is underway on `integration/runtime-operator-v1`. The first PC walk-in Session-to-cash slice has partial implementation and automated coverage, but **is not accepted as a complete or production-ready feature**. Timing/restart behavior, safer cash entry, recovery after a committed payment whose response is lost, correction policy, manual WPF acceptance and independent database reconciliation remain open. Do not use it for real customer money.

- `main` is protected and intentionally unchanged.
- `foundation/runtime-final-v2` is the certified technical baseline; it is not the destination for routine product work.
- Read the live Integration SHA and its exact checks from GitHub before any merge. This README deliberately avoids a mutable Integration SHA.

## Canonical project documents

- [Master Build Plan](docs/planning/master-build-plan.md) — product stages and delivery order.
- [Requirements Traceability](docs/planning/requirements-traceability.md) — capability ownership, status and acceptance requirements.
- [Active Execution Control](docs/operations/active-execution-control.md) — the single current work queue and blocking risks.
- [Action Catalog](docs/development/action-catalog.md) — implemented Desktop/API operations and their contracts.
- [Foundation Stable Checkpoint](docs/operations/stable-checkpoint.md) — immutable evidence for the exact certified Foundation SHA.
- Architecture, ADR and domain files define durable contracts; PRs, Issues and Actions retain change-by-change evidence.

Do not create duplicate status reports for individual PRs. Update the existing source of truth at milestone boundaries and keep detailed check evidence with the matching PR/Action run.
