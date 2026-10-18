# GameNet 5 — Dashboard UI concept v1

This folder contains a **static visual concept only** for discussion and review.

- [Open the dashboard mockup](./gamenet-dashboard-concept-v1.svg)
- Design branch: `design/gamenet-dashboard-concept-v1`
- Based on the current Integration baseline at the time the concept branch was created.
- No WPF, API, Server, database, or runtime files were changed.
- All station counts, active sessions, recent events, invoices, and amounts in the image are illustrative placeholders, not live or test-database data.

## Design goals

- Persian RTL operator dashboard with a coherent visual system.
- Immediate visibility of station availability, active sessions, and pending invoices.
- Separate navigation areas for station management, sessions/cashier, customers, tariffs, and reports.
- Dense operational information without a single oversized form; pending invoices and recent activity stay visible.
- Clear status colors and a restrained dark navy/blue/violet palette.

This mockup is not an implementation specification by itself. If approved, implement the UI as a separate WPF view layer while preserving the existing server-authoritative business logic, API contracts, and stable UI ports.
