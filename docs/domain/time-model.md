# Authoritative Time Model

## Wall-clock time

Persist UTC timestamps for durable facts.

Business timezone is configured separately for:

- tariff calendar interpretation;
- reports;
- operator display;
- daily settlement boundaries.

## Duration time

Billable duration must not be calculated by subtracting two mutable wall-clock values.

Use monotonic time measurement through the .NET TimeProvider timestamp/elapsed-time capabilities.

This protects Session timing from Windows clock corrections, NTP adjustments and manual clock changes.

## Session authority

The Server owns:

- Session start timestamp;
- duration baseline;
- pause intervals;
- resumed intervals;
- final billable duration;
- recovery reconciliation.

Agent-reported timestamps are evidence, not final billing authority.

## Recovery

After Server restart, the Session timing service reconstructs state from persisted Session facts and the defined duration model.

A restart must not create duplicate elapsed time.

## Tests

Required:

- normal elapsed duration;
- pause/resume;
- system wall-clock jump;
- Server restart;
- Agent disconnect;
- duplicate command;
- concurrent stop/settlement.
