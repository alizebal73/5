# Runtime Topology

Server: Windows Service, headless, authoritative.
Desktop: native WPF executable, API client only.
Agent: Windows Service on each client PC, executor/observer only.
PostgreSQL: authoritative persistence for Server state.

External side effects happen after database commit through durable boundaries.
