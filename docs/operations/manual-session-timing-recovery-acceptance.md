# Manual WPF Acceptance and Financial Reconciliation — Session Timing Recovery

**Status:** procedure prepared; execution evidence is still required. This document does not close Issue #98 or authorize real-money use.

## 1. Safety boundary and candidate identity

- Use only a disposable Windows PC/VM and disposable PostgreSQL database. Never run a reboot, clock-change, proxy-fault, or service test against the live GameNet/Manager PC, a customer PC, or a database containing real customer/financial records.
- Stop if isolation, snapshot/revert, Server database target, and test-only service identity cannot be proven. The Runtime LAN package is portable output only; it does not install/register Windows services and is not Setup/MSI.
- PR: [#124](https://github.com/alizebal73/5/pull/124).
- **Before any manual acceptance**, take the source SHA and Git tree from the current PR head; verify Quick Validation/Foundation and Runtime LAN Payload are both green on that exact head. Download the payload artifact from the matching Runtime LAN run, verify its `SHA256SUMS.txt`, and confirm `manifest.json` identifies the same tested checkout SHA. Do not use an artifact built for a previous commit just because only documentation changed; any source SHA change requires matching exact-head evidence.
- Snapshot/revert the disposable VM and database. Record Windows edition/build, VM identifier, Server endpoint, disposable database name, test operator IDs (never passwords/tokens), role permissions, source SHA/tree, payload SHA-256, and unique test-run label.
- The recovery operator must have both `sessions.operate` and the separately granted `sessions.timing.recover`. Use a second operator with `sessions.operate` but without `sessions.timing.recover` for the denied-attempt check. Do not change a production role for this test.
- Enter only independently verified **billable active durations**. For a Session with pauses, add the trusted active segments and exclude every paused interval; the recovery input is total billable active time, not total wall-clock time since start. The Server also rejects a recovered total that is below the Session's already accumulated active segments. Do not use UTC subtraction, computer uptime, or an estimated Server duration as proof of actual play time. A successful API call records a reconciliation; it does not prove the user actually played for that duration.

## 2. Manual WPF sequence

### A. Normal same-boot path and wall-clock movement

1. Log in to the isolated Server and refresh the Session workspace.
2. Select an available disposable PC and fixed PC hourly tariff. Start a Walk-in Session; note Session ID, rate snapshot, and UTC start event time.
3. While the Session remains active, perform the planned forward/backward Windows clock change only inside the disposable VM (or use a controlled clock test mechanism). Do not reboot Windows in this scenario.
4. Restart only the Server process/service within the disposable environment, keeping the same Windows boot. Confirm the service is connected to the same disposable database and no second Server instance is active.
5. End the Session using the ordinary End action. The Server should calculate duration from its persisted monotonic baseline and independent uptime/boot evidence; changing UTC must not change elapsed duration or amount.
6. Check the pending invoice and note the Session ID, Invoice ID, elapsed ticks/duration, immutable hourly-rate snapshot, amount and correlation IDs.

### B. Windows reboot / fail-closed / explicit reconciliation

1. Start a new Session on a disposable PC. Record Session ID, version, rate snapshot, and start evidence.
2. Reboot Windows only inside the disposable VM while the Session remains active. Reopen the app and connect to the same isolated Server/database.
3. Select the active Session and click ordinary End. Expected: the Server returns `sessions.timing_recovery_required`; no invoice is created and the Session remains Active. Stop and investigate if a pending invoice or financial record appears here.
4. With the operator holding both required permissions, select that same Session. The timing-recovery panel must become available only after the ordinary End attempt failed closed for timing continuity.
5. Independently determine the total billable active duration using approved test evidence (for example, the recorded test plan and controlled active-segment records). Exclude all time between Pause and Resume. Enter whole minutes from 1 through 43,200, add a printable 3–256 character evidence/reason note, and check the explicit confirmation.
6. Submit once. Record Session ID/version, requested minutes, reason, Invoice ID, elapsed ticks, amount, correlation ID and request time. Expected: Session transitions to PendingSettlement; one PendingPayment invoice is created; no Payment or billing ledger row exists yet.
7. Refresh the workspace, close/reopen Desktop and log in again. Confirm the same invoice remains pending. Do not repeat a command after a timeout until Server state is reconciled.
8. Replay the same request key and same payload using a controlled test client only if this can be done safely in the isolated environment. Expected: the same Invoice ID and amount. Reusing the key for a different payload must conflict. Never simulate retries against live financial data.
9. In the second operator session without `sessions.timing.recover`, verify the panel/command is unavailable. A controlled direct API attempt must return 403 and leave Session, Invoice, Payment, ledger and successful-recovery Audit unchanged.

### C. Validation/error cases

Verify, through automated tests and isolated direct-API cases where available, that missing confirmation, missing/invalid reason, 0 minutes, more than 43,200 minutes, stale Session version, inactive Session, missing either permission, and attempted manual recovery while continuity is still valid are rejected without creating an invoice. Record any scenario that cannot safely be exercised manually as `NOT RUN`; do not infer a manual pass from CI.

**Stop condition:** if the Session ID, version, requested duration, immutable tariff snapshot, invoice status/amount, permission decision, or Audit evidence disagrees, preserve the VM/database snapshot and application logs. Do not delete or edit financial records to make a case pass.

## 3. Read-only PostgreSQL reconciliation

Run only against the disposable PostgreSQL database. Replace `PASTE-SESSION-UUID-HERE` with the Session ID created by this specific test. The queries are read-only.

### A. Session, invoice, amount calculation, and forbidden financial side effects

The authoritative calculator rounds any fractional Toman upward using exact integer arithmetic. The SQL below uses PostgreSQL `numeric` and the exact ticks-per-hour constant (`36,000,000,000)); it does not use binary floating point.

```sql
WITH input AS (
    SELECT 'PASTE-SESSION-UUID-HERE'::uuid AS session_id
),
s AS (
    SELECT
        x.session_id AS requested_session_id,
        se.id AS session_id,
        se.station_id,
        se.customer_id,
        se.status AS session_status,
        se.version AS session_version,
        se.started_at_utc,
        se.ended_at_utc,
        se.started_boot_identifier,
        se.elapsed_ticks AS session_elapsed_ticks,
        se.tariff_id,
        se.tariff_code_snapshot,
        se.tariff_name_snapshot,
        se.tariff_hourly_rate_toman_snapshot,
        se.ended_by_operator_id,
        se.started_by_operator_id
    FROM input x
    LEFT JOIN sessions se ON se.id = x.session_id
),
i AS (
    SELECT inv.*
    FROM invoices inv
    JOIN input x ON inv.session_id = x.session_id
)
SELECT
    s.*,
    i.id AS invoice_id,
    i.status AS invoice_status,
    i.elapsed_ticks AS invoice_elapsed_ticks,
    i.amount_toman AS invoice_amount_toman,
    i.tariff_hourly_rate_toman_snapshot AS invoice_rate_snapshot,
    i.issued_at_utc,
    CASE
        WHEN i.elapsed_ticks IS NULL OR i.tariff_hourly_rate_toman_snapshot IS NULL THEN NULL
        ELSE ceil(i.tariff_hourly_rate_toman_snapshot::numeric * i.elapsed_ticks::numeric / 36000000000::numeric)::bigint
    END AS independently_recomputed_amount_toman,
    (SELECT count(*) FROM invoices ix JOIN input x ON ix.session_id = x.session_id) AS invoice_count,
    (SELECT count(*) FROM payments p JOIN i ON p.invoice_id = i.id) AS payment_count,
    (SELECT count(*) FROM billing_ledger_entries le JOIN i ON le.invoice_id = i.id) AS ledger_entry_count,
    (SELECT count(*) FROM audit_entries a
       WHERE a.operation = 'sessions.timing.reconciled'
         AND a.reference_type = 'Session'
         AND a.reference_id = s.session_id::text) AS timing_reconciliation_audit_count,
    CASE WHEN
        s.session_id IS NOT NULL
        AND s.session_status = 2
        AND s.ended_at_utc IS NOT NULL
        AND s.session_elapsed_ticks > 0
        AND i.id IS NOT NULL
        AND i.status = 1
        AND i.elapsed_ticks = s.session_elapsed_ticks
        AND i.tariff_id = s.tariff_id
        AND i.tariff_hourly_rate_toman_snapshot = s.tariff_hourly_rate_toman_snapshot
        AND i.amount_toman = ceil(i.tariff_hourly_rate_toman_snapshot::numeric * i.elapsed_ticks::numeric / 36000000000::numeric)::bigint
        AND (SELECT count(*) FROM invoices ix JOIN input x ON ix.session_id = x.session_id) = 1
        AND (SELECT count(*) FROM payments p JOIN i ON p.invoice_id = i.id) = 0
        AND (SELECT count(*) FROM billing_ledger_entries le JOIN i ON le.invoice_id = i.id) = 0
        AND (SELECT count(*) FROM audit_entries a
             WHERE a.operation = 'sessions.timing.reconciled'
               AND a.reference_type = 'Session'
               AND a.reference_id = s.session_id::text) = 1
    THEN 'PASS' ELSE 'FAIL' END AS reconciliation_status
FROM s
LEFT JOIN i ON TRUE;
```

Expected: exactly one row with `reconciliation_status = PASS`, Session status `2` (PendingSettlement), Invoice status `1` (PendingPayment), one invoice with elapsed ticks equal to the persisted Session, original rate snapshot preserved, amount equal to the exact ceil calculation, one `sessions.timing.reconciled` Audit row, zero payments and zero ledger rows. If the invoice is later checked out in a separate downstream test, record that as a separate state and use the cash-checkout reconciliation procedure; do not expect zero payments after checkout.

### B. Inspect timing-recovery and invoice Audit evidence

Replace the Session UUID below. It selects the reconciliation event and resolves its correlation ID to related audit facts, including invoice issuance.

```sql
WITH input AS (
    SELECT 'PASTE-SESSION-UUID-HERE'::uuid AS session_id
),
recovery AS (
    SELECT a.*
    FROM audit_entries a
    JOIN input x
      ON a.reference_type = 'Session'
     AND a.reference_id = x.session_id::text
    WHERE a.operation = 'sessions.timing.reconciled'
),
related AS (
    SELECT a.*
    FROM audit_entries a
    JOIN recovery r ON a.correlation_id = r.correlation_id
)
SELECT
    id, occurred_at_utc, actor_type, actor_id, operation,
    reference_type, reference_id, reason, correlation_id, source, outcome,
    idempotency_key, before_json, after_json
FROM related
WHERE operation IN ('sessions.timing.reconciled', 'billing.invoice_issued')
ORDER BY occurred_at_utc, operation;
```

Expected: one successful `sessions.timing.reconciled` event referencing the Session with the entered reason, correlation ID, idempotency key and before/after JSON documenting `operator_reconciliation`; plus one successful `billing.invoice_issued` event referencing the created invoice under the same correlation. Confirm the elapsed minutes and tariff snapshot in the recorded after-state. Do not treat an Audit row as proof of real-world duration without the independently retained evidence.

### C. Confirm no duplicate invoice / reconciliation event

```sql
SELECT
    (SELECT count(*) FROM invoices WHERE session_id = 'PASTE-SESSION-UUID-HERE'::uuid) AS invoice_count,
    (SELECT count(*) FROM audit_entries
     WHERE operation = 'sessions.timing.reconciled'
       AND reference_type = 'Session'
       AND reference_id = 'PASTE-SESSION-UUID-HERE'::uuid::text) AS recovery_audit_count;
```

Expected: `1`, `1`. Anything else is a stop condition; do not delete rows to make the test pass.

## 4. Evidence package and acceptance decision

Attach to Issue #98:

- [ ] Exact final tested source SHA and Git tree; matching Quick Validation/Foundation and Runtime LAN workflow links.
- [ ] Payload ZIP SHA-256, `manifest.json` with matching tested checkout SHA, and adjacent checksum evidence.
- [ ] Windows edition/build, disposable VM snapshot/revert identity, isolated Server endpoint/database, test operator IDs/permissions, and test-run label.
- [ ] Screenshots for Session start, failed ordinary End with recovery-required state, the gated recovery form, submitted verified duration/reason/confirmation, resulting pending invoice, and post-Desktop-restart state.
- [ ] Session UUID, Invoice UUID, amount and elapsed ticks, rate snapshot, correlation ID, idempotency key, Audit result and event timestamps.
- [ ] Read-only SQL outputs A–C and an independent hand calculation of duration and amount.
- [ ] Permission-denial result and retry/response-loss result, or explicit `NOT RUN` with reason.
- [ ] Pass/fail per step, operator, date/time, deviations and any blocked/NOT RUN cases.

Do not close Issue #98 based on CI alone. Automated PostgreSQL, Desktop and Agent tests do not prove an actual Windows reboot, a usable installed Windows Service, interactive WPF acceptance, correct operator evidence, or independent financial reconciliation. Keep the PR Draft and Issue #98 open until the required manual and financial evidence is attached and reviewed.
