# ADR-005: Optional Product Cache

## Context

Product metadata is read frequently, while SQL remains the authority for checkout prices, inventory, orders, payments, and Outbox coordination. A cache outage must not make Product reads unavailable.

## Decision

Cache `GET /products/{id}` in Redis under versioned product-ID keys, with a five-minute absolute TTL and bounded operation timeout. Misses and failures read from SQL; cache-write failure does not discard a successful SQL result.

## Consequences

Redis is optional; checkout and inventory remain SQL-backed. There is no product update API or invalidation path, so cached metadata can lag SQL by up to the TTL. An outage adds timeout overhead before SQL fallback. Concurrent cold requests can duplicate SQL reads; request coalescing is not implemented.
