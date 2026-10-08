# Money and Time

Money is integer Toman with checked arithmetic. No floating point monetary state. Financial mutation is idempotent and audited. Reversal references the original transaction. Balance is a ledger projection.

Persist UTC. Business timezone is explicit. Billable time comes from one authoritative Session timing model. No DateTime.Now or DateTimeOffset.Now in business code.
