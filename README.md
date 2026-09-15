# FlashSale

FlashSale is a limited-inventory ordering service. It accepts orders while stock is contested, records the reservation and payment work atomically, and completes payment asynchronously. SQL Server is the consistency boundary for inventory, orders, idempotency, and Outbox work.

## Architecture

- **FlashSale.Api** exposes HTTP contracts, routing, validation boundaries, and Problem Details.
- **FlashSale.Application** coordinates CreateOrder, Product reads, Order reads, and payment processing.
- **FlashSale.Domain** owns order state transitions, item rules, quantities, prices, and invariants.
- **FlashSale.Infrastructure** owns EF Core, SQL transactions, atomic inventory SQL, Outbox leases, payment HTTP, resilience, and the optional Product cache.
- **PaymentSimulator.Api** is an independent payment boundary with its own SQL ledger.

Project references follow `Api -> Infrastructure -> Application -> Domain`. Persistence is use-case-specific.

## Core consistency model

SQL Server is authoritative. CreateOrder validates the request and catalog, inserts the Pending order, conditionally decrements each inventory row in deterministic product order, and inserts the payment Outbox message in one local SQL transaction. A failed reservation rolls the whole transaction back.

Idempotency uses a case-sensitive unique `(UserId, IdempotencyKey)` constraint plus a request fingerprint. Replaying the same request returns the committed order; changing the request under the same key returns a conflict.

Payment is asynchronous. The API returns Pending after local commit. The Outbox worker claims durable work with a lease and claim token, calls payment outside SQL transactions, and commits terminal order state, any inventory release, and Outbox completion together. Failed or expired orders release reservations only after the payment outcome is safely resolved.

Payment HTTP uses bounded retries, timeouts, and a circuit breaker. Unresolved work remains in the Outbox for durable retry. Optional Redis caching serves `GET /products/{id}` with a five-minute TTL, bounded cache operations, and SQL fallback; checkout prices always come from SQL.

## Performance evidence

These are short local engineering measurements on SQL Server 2019 Express, .NET 8, eight logical processors, a 100-connection pool, and the seeded datasets described in [the performance harness README](tests/FlashSale.Performance/README.md). They are not production capacity estimates.

| Workload | Concurrency | Throughput | p95 | p99 | Technical errors / business results |
|---|---:|---:|---:|---:|---|
| Product, DB-only | 100 | 5,077/s | 32.8 ms | 50.4 ms | 0% |
| Product, warm Redis | 100 | 12,036/s | 11.4 ms | 13.9 ms | 0% |
| Product, Redis unavailable | 100 | 1,480/s | 80.9 ms | 163.8 ms | 0%; SQL fallback |
| CreateOrder, 20 items | 100 | 808/s | 211.6 ms | 291.9 ms | 0% |
| FlashSale spike, 500 clients | 500 | 1,225/s | 450.5 ms | 468.5 ms | 0%; 6,531 expected 409s |
| Healthy Outbox processing | 100 | 411/s | 594.4 ms | 1,232.1 ms | 0% |

The Outbox claim index `(NextAttemptAt, Id)` removed candidate sorting. Inventory batching reduced multi-item round trips but increased logical reads. Before/after runs used different durations; SQL Express variance and cumulative Redis counters limit comparisons between runs, including cold versus warm cache.

## Testing

Unit and SQL-backed integration tests cover order transitions, idempotency, rollback, payment recovery, and Outbox processing. A 1,000-request concurrency test checks inventory invariants. Test hosts share a process; this is not a multi-process capacity test.

`FlashSale.Performance` separates expected 409 conflicts and idle polls from technical failures, and exercises cache/provider failures, contention, spikes, and mixed traffic. Run the test suites with `dotnet test FlashSale.sln --no-build --no-restore` after building; integration tests require SQL Server.

## Running locally

Requirements: .NET 8 SDK, SQL Server 2019 or newer (LocalDB is supported), PowerShell, and optionally `sqlcmd`.

```powershell
dotnet restore FlashSale.sln --locked-mode
dotnet build FlashSale.sln --no-restore
SqlLocalDB start MSSQLLocalDB
dotnet ef database update --project src/FlashSale.Infrastructure
dotnet ef database update --project src/PaymentSimulator.Api
```

Run the simulator and API on loopback ports 5081 and 5080. Environment variables override `appsettings.json`; nested settings use double underscores. Redis is disabled by default. See the performance README for isolated database runs and failure scenarios.

## Known limitations and trade-offs

- Authentication and order-ownership checks are absent; user IDs are client-supplied. Use local or trusted environments until these controls, rate limits, and a request-size policy are implemented.
- Hot inventory rows intentionally serialize competing reservations.
- Redis outage increases product-read latency and SQL load.
- Payment outages retain Pending orders and reservations beyond expiry. A real provider must support durable operation identity and atomic close, or require reconciliation/refund handling.
- Outbox, payment, and idempotency records are retained. Retention, backlog alerting, and recovery capacity need deployment-specific policies and sizing.

See [architecture](docs/architecture.md) for transaction flows and operational constraints, and [ADRs](docs/adr/) for decisions and trade-offs.
