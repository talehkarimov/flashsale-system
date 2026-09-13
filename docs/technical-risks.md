# Technical risks

| Risk | Impact | Mitigation | Residual concern |
|---|---|---|---|
| Overselling across replicas | More paid orders than stock | Conditional SQL updates, nonnegative constraint, atomic creation transaction | Hot products serialize on their inventory rows |
| Duplicate order requests | Repeated reservations | Unique user/key index, request hash, duplicate resolution after rollback | Keys and successful operations must remain retained |
| Duplicate payments or lost responses | Multiple charges or incorrect failure | Stable operation ID and durable provider ledger; safe replay after timeout | Real provider must retain identities and honor the same contract |
| Payment versus expiration | Charge after inventory release | Atomic provider close/tombstone, terminal order lock, release only after final outcome | Requires provider cooperation; no universal safe local-only expiration |
| Retry amplification | Dependency overload | Three HTTP attempts, jitter, circuit breaker, durable backoff capped at 60 seconds | Breakers are per process; durable retries continue until resolved |
| Stuck Pending orders | Unavailable stock | Persisted Outbox drives payment and close; leases recover after restart | Provider outage or protocol errors can hold inventory beyond the deadline |
| Duplicate Outbox processing | Conflicting completion or release | Stable payment ID, order update lock, atomic terminal transaction | Leases may overlap when processing is slow |
| Failed compensation | Inventory remains reserved | Transition, release, and Outbox completion commit together; failure retries | Database recovery is required; no separate dead-letter UI |
| Database contention or failure | Request latency/errors | Stable product lock order, short local transactions, client replay with same key | Deadlocks and connection failures may surface as 500; retries are caller-driven |
| Retained operational records | Growing storage | Filtered index on unprocessed Outbox rows | Retention policies must preserve idempotency and cancellation guarantees |
| Missing public identity | Unauthorized order access or user spoofing | Client-supplied IDs are documented for local/trusted use | Real authentication and Order ownership authorization are required before public deployment |
| Public request abuse | Database or dependency exhaustion | Bounded item count, quantity validation, and payment/Redis timeouts | Deployment-specific API or edge rate limits and request-size policy remain to be selected |
| Redis outage | Product read latency increases | Bounded cache timeout and SQL fallback | Redis is optional; cache counters and operational alerts belong to deployment observability |
| Payment-provider outage | Pending orders and reserved inventory persist | Durable Outbox retry, stable operation identity, and terminal compensation | Recovery throughput, retention, and alerting need production sizing |
| Multi-item transaction cost | Higher CreateOrder latency and logical reads | Parameterized inventory batch with deterministic update ordering | Fewer round trips trade for more reads in one SQL command |

HTTP hosting tests share a process while using independent application service providers. The 1,000-request test validates SQL invariants; it is not a throughput benchmark or a substitute for testing separately deployed replicas.
