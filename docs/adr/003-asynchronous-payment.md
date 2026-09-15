# ADR-003: Asynchronous Payment and Consistency Model

## Context

Payment availability and latency must not extend the order creation transaction. Expiration must not release stock for a payment whose successful response was lost.

## Decision

Return Pending after local creation commits. Process payment asynchronously and let clients retrieve order state. Resolve the provider outcome outside SQL transactions. At the deadline, close the provider operation before expiring the order. Serialize local terminal transitions on the order row and commit inventory release with Failed/Expired state.

## Consequences

Creation is independent of payment latency; clients poll for the terminal state. Paid, Failed, and Expired follow the provider's terminal result. Provider unavailability can retain reservations beyond the deadline because unresolved payments cannot safely release inventory.
