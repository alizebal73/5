# Session State Machine

Created -> Active -> Paused -> Active -> Ending -> PendingSettlement -> Paid
PendingSettlement -> Debt

Exceptional transitions: Cancelled, Transfer, Recovery.

Before implementation define customer ownership, Station binding, tariff snapshot, billable timing, transfer concurrency, settlement, retry, restart and reconciliation.
