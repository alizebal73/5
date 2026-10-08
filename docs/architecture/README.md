# Architecture Baseline

Server owns business truth. Desktop and Agent are clients/executors. PostgreSQL is authoritative persistence.

Business modules use Domain -> Application -> Infrastructure -> Api -> tests.

No browser dashboard runtime. No client-side authoritative business calculations.
