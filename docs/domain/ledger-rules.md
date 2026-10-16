# Ledger Rules

Wallet entries: TopUp, SessionCharge, BuffetCharge, Bonus, Refund, Adjustment, DebtPayment.

Inventory entries: Purchase, Sale, Return, Waste, Correction, Transfer.

UI balance/stock values are projections. Direct authoritative set-balance/set-stock commands are forbidden except explicit audited corrections with permission and approval rules.

## Billing cash ledger

- A posted CashSale is an immutable historical fact. Its positive amount is the invoice amount applied, not the gross tender received at the counter.
- Correcting entered cash-received/change facts and returning money to a customer are different business operations. A tender correction must be append-only and must not alter the sale amount; a refund/reversal must use an explicitly typed compensating entry linked to the original transaction.
- Never delete or rewrite a posted sale, Payment or Audit record to correct history. Reversal entries store a positive amount magnitude with an explicit type whose projection contributes the opposite sign.
- See the proposed [Cash Tender Correction and Cash Sale Reversal V1 contract](cash-correction-and-reversal-v1.md). Until its persistence migration, authorization, idempotency, audit and verification gates are implemented, these operations are not available for operational use.
