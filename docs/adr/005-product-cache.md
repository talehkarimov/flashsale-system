# ADR-005: Optional Product Cache

**Status:** Accepted

## Context

Product metadata is read frequently, while SQL remains the authority for checkout prices, inventory, orders, payments, and Outbox coordination. A cache outage must not make Product reads unavailable.

## Decision

Use a small cache-aside Redis implementation for `GET /products/{id}`. Keys are versioned and contain only the Product ID. Entries have a five-minute absolute TTL and cache operations have a bounded timeout. Misses and cache failures read from SQL; a successful SQL result is returned even if cache population fails.

## Consequences

Warm reads reduce SQL work and improve read latency. Redis is optional and never participates in correctness. Products currently have no update API, so TTL provides bounded staleness. A Redis outage adds timeout overhead before SQL fallback, and concurrent cold requests may perform duplicate SQL reads; single-flight coordination is intentionally deferred until measurements justify it.
