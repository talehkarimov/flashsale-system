# ADR-002: Request and Payment Idempotency

## Context

Concurrent order retries must not reserve stock twice. A payment timeout can hide a committed charge, including when the reservation deadline has passed.

## Decision

Enforce a case-sensitive unique `(UserId, IdempotencyKey)` index. Insert the order inside the transaction before reserving stock so duplicates contend on operation identity first. Compare a hash of canonical product IDs and quantities on replay; return conflict if the payload changed.

Use the order ID as the stable provider operation ID. The provider durably stores a terminal outcome and validates amount and expiry on every attempt. An atomic close operation records cancellation when payment has not yet occurred and returns Paid if a charge already exists.

## Consequences

Concurrent retries resolve to one committed order and payment. Distinct client users can reuse a key. Storage and provider contracts must preserve idempotency records. Closing an ambiguous operation requires provider support; a provider without equivalent guarantees needs a different reconciliation/refund design.
