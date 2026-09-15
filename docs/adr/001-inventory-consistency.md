# ADR-001: Inventory Consistency Strategy

## Context

Competing orders can reserve the same products through different application instances. A multi-product order must either reserve every item or leave inventory unchanged.

## Decision

Reserve stock with a conditional SQL decrement and use affected rows to determine success. Enclose reservation, Pending order, and Outbox insertion in one SQL transaction. Enforce nonnegative stock with a check constraint. Inventory remains outside the Order aggregate; multi-product reservations use stable product ordering.

## Consequences

Correctness holds across replicas without application locks. Failed creation rolls back all reserved items. SQL Server-specific mutation logic and contention on hot inventory rows are accepted costs. Ordinary database failures may require client retry with the same idempotency key.
