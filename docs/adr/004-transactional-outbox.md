# ADR-004: Transactional Outbox

## Context

A committed order must survive a crash before asynchronous processing begins. Multiple workers can run and restart independently.

## Decision

Persist one payment work item with the order in the local SQL transaction. A hosted worker atomically claims eligible rows using expiring SQL leases. Resolve payment or expiration, then mark completion in the terminal-order transaction. Retry unresolved messages with persisted backoff. Use claim tokens to prevent stale workers from rescheduling another worker's claim.

## Consequences

Payment work shares the order database without a broker dependency. Lease expiry can repeat provider calls, so the payment operation ID and terminal-order lock must tolerate duplicates. Polling adds latency and SQL load. Crashes delay work until lease expiry; persistently failing messages remain pending and require operational intervention.
