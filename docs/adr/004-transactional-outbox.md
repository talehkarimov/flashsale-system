# ADR-004: Transactional Outbox

**Status:** Accepted

## Context

A committed order must survive a crash before asynchronous processing begins. Multiple workers can run and restart independently.

## Decision

Persist one payment work item with the order in the local SQL transaction. A hosted worker atomically claims eligible rows using expiring SQL leases. Resolve payment or expiration, then mark completion in the terminal-order transaction. Retry unresolved messages with persisted backoff. Use claim tokens to prevent stale workers from rescheduling another worker's claim.

## Consequences

There is no database/queue dual write and no broker dependency. Delivery is at least once; handlers and provider operations must be idempotent. Polling adds latency and SQL load. A crash can delay work until lease expiry, and persistently failing messages remain stored for recovery rather than being silently discarded.
