# Cash Tender Correction and Cash Sale Reversal — V1 Contract

**Status: proposed contract; not implemented and not enabled for operational use.** This contract is the next bounded Billing slice under [Issue #98](https://github.com/alizebal73/5/issues/98). Its purpose is to define safe behavior before adding endpoints or UI. Automated Foundation/Integration checks do not constitute acceptance of cash correction or reversal.

## 1. Existing contract and constraint

The current Slice 1 creates one paid Invoice, one immutable cash Payment, one CashSale ledger entry and Audit evidence in a transaction. Payment.AmountAppliedToman equals the invoice amount; AmountReceivedToman records cash tendered; ChangeGivenToman is received minus applied. The posted sale is only the invoice amount, not the gross tender.

The current schema enforces one Payment per invoice and one ledger row per invoice/payment, requires positive ledger amounts and permits only CashSale. A correction/reversal implementation therefore needs a forward-only migration and revised, explicit database constraints. It must not simply append a second ledger row against today's constraints, change old amounts in place, or bypass them.

## 2. Two operations with different meanings

### A. Correct mis-recorded cash tender facts

Use this operation only when the recorded cash-received/change facts were entered incorrectly and the invoice charge itself remains correct.

- Require a dedicated permission named billing.cash-tender-correct, a required meaningful reason, the target invoice/payment identity and the expected current correction version.
- The corrected AmountReceivedToman must be at least the unchanged AmountAppliedToman; calculate ChangeGivenToman using checked integer arithmetic as received minus applied. A correction cannot change the amount charged, invoice status, paid Session, or sale ledger.
- Preserve the original Payment and every previous correction. Persist each correction as an append-only record with Payment/invoice references, monotonically increasing version, previous and corrected tender/change facts, actor, server UTC timestamp, reason, correlation and idempotency evidence. A read model may present the latest corrected tender facts while retaining access to the original facts and full history.
- Apply an expected-version/concurrency check in the same transaction. A stale correction must conflict rather than overwrite a newer one. A repeated identical idempotency key and request returns the original result; reuse with a different request conflicts.
- Do not allow a tender correction once the payment has been reversed. This operation records a correction of facts; it is not a cash refund and must not be used to force the cash drawer to an expected balance.

### B. Reverse a posted cash sale by returning the amount charged

Use this operation only when the business is actually returning the full amount applied to the invoice. V1 supports a **single full reversal only**; partial refunds, multiple partial allocations, wallet credit, and refund/reissue workflows are outside this slice.

- Require a dedicated permission named billing.cash-reverse, a required reason, explicit confirmation of the invoice and full reversal amount, and an idempotency key. Do not grant this permission implicitly merely because an operator has billing.write.
- The refund amount is exactly the original Payment.AmountAppliedToman and Invoice.AmountToman. Do **not** refund AmountReceivedToman: any cash change was already given to the customer at checkout.
- Only a currently paid, cash-settled invoice may be reversed; reject pending, missing, inconsistent or already reversed records. Database uniqueness/constraints and transaction-level concurrency control must guarantee at most one full reversal per invoice even under concurrent requests.
- Preserve the original Payment and CashSale ledger entry. Write a durable reversal record that references the original invoice/payment and the reversing operator, reason, amount, server UTC timestamp, correlation and idempotency evidence. Write a compensating CashSaleReversal ledger entry as a positive amount **magnitude** with an explicit reversal type; ledger projections must treat that type as an outflow/negative effect. Do not encode reversal by deleting a sale or writing a negative value that violates the ledger's positive-amount contract.
- Record the reversal and its ledger entry atomically, update the Invoice lifecycle projection to Reversed while preserving its original paid-by/paid-at facts, and append Audit evidence. A failure at any point must leave no partial reversal. The original Session remains ended/settled and the Station remains available; reversal must not reopen the gaming session or alter its duration, tariff snapshot or historical invoice amount.
- Return a durable reversal outcome that can be queried after a lost HTTP response or Desktop restart. A retry must not issue a second refund. An already reversed invoice returns an actionable conflict and must never report success for a new refund.

A separate approval/dual-control workflow is not inferred or simulated by this contract. The dedicated permission and mandatory reason are required for V1; any later requirement for a second approver must be implemented through a real approval boundary before exposure, not by a UI-only confirmation.

## 3. Persistence and transaction requirements

The implementation design and forward-only migration must account for all current constraints explicitly:

- Add append-only storage for tender corrections and full reversals with foreign keys to their original records; use unique versioning for corrections and database-enforced uniqueness for a full reversal.
- Extend the ledger entry type constraint to the explicit cash sale and cash reversal types. Keep stored amounts positive and make all balance/reporting projections apply the reversal type with the correct sign.
- Replace the current blanket unique ledger indexes only with constrained uniqueness that still guarantees one original cash-sale entry per invoice/payment and at most one full-reversal entry per invoice/payment. Preserve foreign-key and referential-integrity guarantees.
- Extend the Invoice status/domain constraint for a Reversed state while retaining the original payment timestamp/operator evidence. Existing historical records must continue to pass migration and certification.
- Use the shared Server CommandMutationPipeline for transaction/idempotency execution, preserve operation-specific authorization, and include correction/reversal records, ledger effects and required Audit records in the same transaction.
- The Server remains the source of truth. Desktop must not calculate authoritative money, access PostgreSQL directly, or show a completed correction/refund before the Server confirms it.

## 4. Required verification before acceptance

Tests must be added with the implementation; this document does not satisfy them.

1. Domain tests: invalid/missing reason, invalid amount, checked arithmetic, tender/change calculation, stale correction version, invalid invoice/payment lifecycle, and rejection of a second full reversal.
2. Disposable PostgreSQL tests: clean migration, historical data compatibility, all revised constraints/foreign keys, append-only history, one reversal under concurrent requests, rollback on failure and independent persisted-row reconciliation.
3. Command/security tests: dedicated permission boundaries, idempotent same-intent replay, same key with changed payload conflict, concurrent/retried requests, durable outcome lookup after a simulated lost response, and Audit records with actor/reason/original references.
4. UI-contract/manual acceptance: distinct correction-versus-refund language, visible invoice and exact amount, mandatory reason/confirmation, actionable conflicts, no success fabricated on network failure, and Persian RTL/Toman display.
5. Run the required Quick Validation and full PostgreSQL/runtime gates on the exact candidate SHA. Capture a matching test-payload hash, screenshots and an independent hand reconciliation of Invoice, Payment, correction/reversal records, Ledger and Audit.

## 5. Explicit non-goals

No invoice amount editing, deletion/rewrite of historical Payment/Ledger/Audit rows, partial refunds, wallet/debt/bonus handling, card payments, session reopening, automatic cash-drawer balancing, or generalized accounting/reporting changes. This remains unavailable for real customer money until implementation, tests, manual acceptance and the remaining Issue #98 criteria are complete.
