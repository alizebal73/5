# Manual WPF Acceptance and Financial Reconciliation — PC Cash Sale Reversal

**Status:** procedure prepared; execution evidence is still required. This guide does not mark Issue #98 accepted.

## 1. Safety boundary and exact candidate

- Use a disposable Windows test PC/VM and a disposable PostgreSQL database only. Never run these tests against the live GameNet/Manager PC, a live customer station, or a database containing customer financial records.
- Integration commit containing PR #120: `0660e632f0e38ec1654a2830f633e2f3cd60ae5f`. The tested source-tree SHA for the reversal code was `ad92960382088872b1da0441241f4d71ff3603f9`; its Git tree matches the merge commit tree.
- Portable Runtime LAN Smoke Payload artifact: [run 38887541248](https://github.com/alizebal73/5/actions/runs/38887541248), artifact `runtime-lan-smoke-payload-38887541248`. It is a portable payload, not Setup/MSI and not proof of a live LAN acceptance test. Record the downloaded ZIP SHA-256 and the payload's own manifest/checksums before use.
- Confirm the isolated Server is configured to point only at the disposable database. Snapshot/revert the VM and database before beginning. Stop if you cannot prove the environment is isolated.
- Use a test operator with the minimum permissions needed: `sessions.operate`, `billing.write`, and `billing.cash-reverse`. Keep a second test operator without `billing.cash-reverse` for the denied-permission check. Do not grant these roles to a production operator as part of this test.

## 2. Capture evidence before touching the UI

Record the date/time, Windows edition/build, test PC/VM name, Server endpoint, payload SHA-256, exact source SHA/tree, operator test IDs (not secrets), role permissions, database name, and a unique test run label. Capture screenshots at each checkpoint; redact tokens, passwords, and personal/customer information.

## 3. WPF acceptance sequence

1. **Start:** refresh the workspace, select the disposable PC and fixed tariff, start a walk-in Session, and record the station/session ID shown by the UI or server logs. Confirm one active Session exists and no invoice/payment was created yet.
2. **End and invoice:** end that Session. Record the invoice ID, tariff snapshot, elapsed duration and displayed integer-Toman amount. The Session should be waiting for settlement and the invoice pending payment.
3. **Cash checkout:** enter a received-cash amount greater than or equal to the displayed invoice amount; confirm the exact cash and change. Record invoice amount, cash received, and change as three distinct values. Confirm the UI shows success only after the Server confirms it.
4. **Verify recent checkout:** refresh and locate the paid invoice. Confirm the recorded invoice amount, payment ID, cash received, and change match the values just captured.
5. **Full reversal:** select that exact paid cash sale, enter a meaningful reason of 3–256 printable characters, verify the invoice/payment identity and amount, check the explicit confirmation, and submit once. Record the reversal ID/time shown by the UI or Server outcome.
6. **Post-reversal view:** refresh the workspace. The invoice must show `Reversed`; the original Session must remain settled/paid; the original payment and original sale must still be visible in persisted history. The reversal amount must equal the invoice amount applied, never gross cash received and never cash change.
7. **Restart/reload recovery:** close and reopen the Desktop, authenticate to the same isolated Server, refresh recent checkouts, and verify the reversed outcome remains. Do not click Reverse again merely because a previous request timed out; reconcile the durable outcome first.
8. **Permission boundary:** switch to the disposable operator without `billing.cash-reverse`. The UI must not permit reversal, and a direct request (if exercised) must be rejected by the Server. Run the read-only SQL below afterward and confirm no new reversal, ledger row, or success audit was created by the denied attempt.
9. **Ambiguous-response case:** only attempt this if the isolated environment can reliably simulate a response being lost after the Server commits (for example, a controlled test proxy). A general network disconnect may prevent the request from reaching the Server and is not equivalent. After an ambiguous response, reload/reconcile the durable Server outcome before retrying. If the timing cannot be controlled, mark this manual case `NOT RUN`; the automated Desktop regression is separate evidence.

**Stop immediately** if identities, amounts, statuses, or result messages disagree. Do not retry a financially ambiguous operation. Preserve the database snapshot, request/correlation IDs, timestamps, application logs, and screenshots for investigation.

## 4. Read-only PostgreSQL reconciliation

Run these queries in the disposable database after replacing the UUID literal in each query with the tested invoice ID. They are `SELECT` only; do not edit or delete financial records to make a test pass.

### A. Reconcile Session, Invoice, Payment, original sale, reversal, and compensating ledger

```sql
WITH input AS (
    SELECT 'PASTE-TEST-INVOICE-UUID-HERE'::uuid AS invoice_id
),
r AS (
    SELECT
        i.id AS invoice_id, i.session_id, i.status AS invoice_status, i.amount_toman AS invoice_amount_toman,
        i.paid_at_utc AS invoice_paid_at_utc, i.paid_by_operator_id AS invoice_paid_by_operator_id,
        s.status AS session_status, s.elapsed_ticks AS session_elapsed_ticks, s.paid_at_utc AS session_paid_at_utc,
        p.id AS payment_id, p.method AS payment_method, p.amount_applied_toman, p.amount_received_toman, p.change_given_toman,
        cr.id AS reversal_id, cr.payment_id AS reversal_payment_id, cr.amount_toman AS reversal_amount_toman,
        cr.reason AS reversal_reason, cr.reversed_by_operator_id, cr.correlation_id, cr.idempotency_key, cr.reversed_at_utc,
        sl.id AS sale_ledger_id, sl.payment_id AS sale_ledger_payment_id, sl.entry_type AS sale_entry_type, sl.amount_toman AS sale_amount_toman,
        rl.id AS reversal_ledger_id, rl.payment_id AS reversal_ledger_payment_id, rl.entry_type AS reversal_entry_type, rl.amount_toman AS reversal_ledger_amount_toman,
        (SELECT count(*) FROM payments px WHERE px.invoice_id = i.id) AS payment_count,
        (SELECT count(*) FROM billing_ledger_entries lx WHERE lx.invoice_id = i.id AND lx.entry_type = 1) AS sale_ledger_count,
        (SELECT count(*) FROM billing_ledger_entries lx WHERE lx.invoice_id = i.id AND lx.entry_type = 2) AS reversal_ledger_count,
        (SELECT count(*) FROM cash_sale_reversals rx WHERE rx.invoice_id = i.id) AS reversal_record_count,
        COALESCE((SELECT sum(CASE lx.entry_type WHEN 1 THEN lx.amount_toman WHEN 2 THEN -lx.amount_toman ELSE 0 END)
                  FROM billing_ledger_entries lx WHERE lx.invoice_id = i.id AND lx.entry_type IN (1, 2)), 0) AS net_ledger_effect_toman
    FROM input x
    JOIN invoices i ON i.id = x.invoice_id
    LEFT JOIN sessions s ON s.id = i.session_id
    LEFT JOIN payments p ON p.invoice_id = i.id
    LEFT JOIN cash_sale_reversals cr ON cr.invoice_id = i.id
    LEFT JOIN billing_ledger_entries sl ON sl.invoice_id = i.id AND sl.entry_type = 1
    LEFT JOIN billing_ledger_entries rl ON rl.invoice_id = i.id AND rl.entry_type = 2
)
SELECT r.*,
    CASE WHEN
        invoice_status = 3 AND session_status = 3
        AND invoice_paid_at_utc IS NOT NULL AND invoice_paid_by_operator_id IS NOT NULL
        AND session_paid_at_utc IS NOT NULL AND session_elapsed_ticks > 0
        AND payment_count = 1 AND payment_id IS NOT NULL AND payment_method = 1
        AND amount_applied_toman = invoice_amount_toman
        AND amount_received_toman >= amount_applied_toman
        AND change_given_toman = amount_received_toman - amount_applied_toman
        AND sale_ledger_count = 1 AND sale_ledger_id IS NOT NULL AND sale_entry_type = 1
        AND sale_ledger_payment_id = payment_id AND sale_amount_toman = invoice_amount_toman
        AND reversal_record_count = 1 AND reversal_id IS NOT NULL
        AND reversal_payment_id = payment_id AND reversal_amount_toman = invoice_amount_toman
        AND length(btrim(reversal_reason)) BETWEEN 3 AND 256
        AND reversal_ledger_count = 1 AND reversal_ledger_id IS NOT NULL AND reversal_entry_type = 2
        AND reversal_ledger_payment_id = payment_id AND reversal_ledger_amount_toman = invoice_amount_toman
        AND net_ledger_effect_toman = 0
    THEN 'PASS' ELSE 'FAIL' END AS reconciliation_status
FROM r;
```

Expected: exactly one row with `reconciliation_status = PASS`, invoice status `3` (Reversed), Session status `3` (settled/paid), one Payment, one original `entry_type = 1` sale row, one reversal record, one `entry_type = 2` reversal ledger row, and net ledger effect `0`. The cash tender and change values must remain the original recorded values.

### B. Confirm the two successful Audit events for the reversal

```sql
WITH input AS (
    SELECT 'PASTE-TEST-INVOICE-UUID-HERE'::uuid AS invoice_id
)
SELECT
    a.id, a.occurred_at_utc, a.actor_type, a.actor_id, a.operation, a.reference_type, a.reference_id,
    a.reason, a.correlation_id, a.outcome, a.idempotency_key, a.before_json, a.after_json
FROM input x
JOIN cash_sale_reversals r ON r.invoice_id = x.invoice_id
JOIN audit_entries a ON a.correlation_id = r.correlation_id
WHERE a.operation IN ('billing.cash_sale_reversal', 'billing.cash_sale_reversal_ledger_entry')
ORDER BY a.occurred_at_utc, a.operation;
```

Expected: two rows, both with `outcome = Succeeded`, the recorded operator/reason/correlation/idempotency key, and references to the reversal record and compensating ledger row respectively. Inspect `before_json`/`after_json` to confirm the original sale/payment identities and the recorded reversal result.

### C. Check there is no duplicate reversal record or ledger entry

```sql
SELECT
    (SELECT count(*) FROM cash_sale_reversals WHERE invoice_id = 'PASTE-TEST-INVOICE-UUID-HERE'::uuid) AS reversal_records,
    (SELECT count(*) FROM billing_ledger_entries WHERE invoice_id = 'PASTE-TEST-INVOICE-UUID-HERE'::uuid AND entry_type = 1) AS original_sale_entries,
    (SELECT count(*) FROM billing_ledger_entries WHERE invoice_id = 'PASTE-TEST-INVOICE-UUID-HERE'::uuid AND entry_type = 2) AS reversal_entries;
```

Expected: `1`, `1`, `1` respectively. Anything else is a stop condition, not a reason to delete rows.

## 5. Evidence record and acceptance decision

Attach to Issue #98:

- [ ] Exact source SHA and Git tree; runtime ZIP SHA-256 and included manifest/checksum evidence.
- [ ] Windows edition/build, disposable VM snapshot identifier, Server endpoint and disposable database name.
- [ ] Screenshots of Start, End/invoice, cash entry/confirmation, paid outcome, reversal reason/confirmation, reversed outcome and post-restart history.
- [ ] Invoice UUID, Session UUID, Payment UUID, original sale ledger UUID, reversal UUID, compensating ledger UUID, correlation ID and timestamps.
- [ ] Output from queries A–C and an independent hand calculation of applied amount, received tender, change and net ledger effect.
- [ ] Permission-denial result, and ambiguous-response result or explicitly `NOT RUN` with reason.
- [ ] Pass/fail per step, operator, date/time and any deviation.

Do not close Issue #98 based on CI alone. Acceptance requires the complete WPF Session-to-cash path, restart/network behavior, independent financial reconciliation and all other Issue #98 criteria. Keep any test that could not actually be executed marked `NOT RUN` rather than inferred as passed.