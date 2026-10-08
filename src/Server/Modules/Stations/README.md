# Stations Module

Station identity and operator-visible station configuration.

## Authority

Server-owned business state.

## State boundary

Station is not Agent, Connection or Session.

- Station: business identity/configuration.
- Agent: device identity and execution boundary.
- Connection: transient transport fact.
- Session: customer usage/occupancy.

## Supported V1 types

- PC
- PS5
- Foosball

## Invariants

- Station code is unique within the site.
- New stations begin Available.
- In-use stations cannot be disabled.
- Maintenance/recovery state is explicit.
- Billing/session ownership does not live in Station.

## Next integration

Persistence, permission enforcement, audit and idempotent API commands are added only through the corresponding application/infrastructure boundaries.
