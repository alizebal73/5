# Money and Time

Money is integer Toman with checked arithmetic. No floating point monetary state. Financial mutation is idempotent and audited. Reversal references the original transaction. Balance is a ledger projection.

## Fixed-hourly Session charge rounding

Calculate the charge from the server-authoritative elapsed UTC ticks and the Session's immutable hourly-rate snapshot. Use the exact numerator `hourlyRateToman × elapsedTicks` and denominator `TimeSpan.TicksPerHour` with integer/`BigInteger` arithmetic. Divide to an integer quotient and remainder; if the remainder is non-zero, add one Toman. In other words, **any fractional Toman rounds upward (ceiling) to the next whole Toman**; do not round to nearest and do not use `float`/`double`. Reject invalid non-positive rates, non-positive elapsed time, and totals beyond `Int64` rather than wrapping.

Persist UTC. Business timezone is explicit. Billable time comes from one authoritative Session timing model. No DateTime.Now or DateTimeOffset.Now in business code.
