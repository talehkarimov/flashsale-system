# FlashSale

## Overview

FlashSale is a limited-inventory ordering service. It accepts orders while stock is contested, records the reservation and payment work atomically, and completes payment asynchronously. SQL Server is the consistency boundary for inventory, orders, idempotency, and Outbox work.

## Architecture

- **FlashSale.Api** exposes HTTP contracts, routing, validation boundaries, and Problem Details.
- **FlashSale.Application** coordinates CreateOrder, Product reads, Order reads, and payment processing.
- **FlashSale.Domain** owns order state transitions, item rules, quantities, prices, and invariants.
- **FlashSale.Infrastructure** owns EF Core, SQL transactions, atomic inventory SQL, Outbox leases, payment HTTP, resilience, and the optional Product cache.
- **PaymentSimulator.Api** is an independent payment boundary with its own SQL ledger.

The dependency direction is API → Infrastructure → Application → Domain. Persistence remains use-case-specific; there are no generic repositories or Unit of Work abstractions.

## Core consistency model

SQL Server is authoritative. CreateOrder validates the request and catalog, inserts the Pending order, conditionally decrements each inventory row in deterministic product order, and inserts the payment Outbox message in one local SQL transaction. A failed reservation rolls the whole transaction back.

Idempotency uses a case-sensitive unique `(UserId, IdempotencyKey)` constraint plus a request fingerprint. Replaying the same request returns the committed order; changing the request under the same key returns a conflict.

Payment is asynchronous. The API returns Pending after local commit. The Outbox worker claims durable work with a lease and claim token, calls payment outside SQL transactions, and commits terminal order state, any inventory release, and Outbox completion together. Failed or expired orders release reservations only after the payment outcome is safely resolved.

## Resilience

Payment HTTP uses an attempt timeout, total timeout, bounded retry with exponential backoff and jitter, and a circuit breaker. These short-lived HTTP retries protect one provider call. Durable Outbox retries persist unresolved work with backoff and survive process restarts; they operate on a longer time scale.

## Caching

`GET /products/{id}` uses cache-aside Redis with a versioned key and a five-minute TTL. Redis operations have a bounded timeout. A miss, read failure, or write failure falls back to SQL, and SQL remains authoritative for Product reads, checkout prices, inventory, orders, payments, and Outbox state. Products have no update API in this release, so TTL is the bounded-staleness strategy.

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

The Outbox claim optimization changed the plan from candidate sorting to ordered `(NextAttemptAt, Id)` index access. Inventory batching reduced multi-item database round trips while increasing logical reads inside the batch; that is an explicit trade-off. Earlier before/after runs used different durations, and local SQL Express variance is material. Redis counters can be cumulative across a container, and cold-cache results should not be treated as inherently faster than warm-cache results.

## Testing

The repository contains domain and application unit tests, SQL-backed integration tests, payment and Outbox tests, idempotency and rollback tests, a 1,000-request inventory concurrency correctness gate, and the reusable `FlashSale.Performance` harness. The harness distinguishes successful requests, expected 409 business conflicts, idle Outbox polls, and technical failures. It also exercises Redis fallback, payment unavailability, slow payment, business rejection, contention, spike traffic, and mixed traffic.

## Security considerations

Implemented controls include parameterized SQL, database constraints for inventory and idempotency, bounded request validation, stable payment operation identity, payment response-size limits, disabled payment redirects/cookies, HTTPS validation for non-loopback payment endpoints, bounded Redis operations, and generic production error responses.

Before public deployment, the service still requires real authentication, Order ownership authorization, a deployment-specific rate-limiting policy, and an explicit request-size policy. The current API accepts client-supplied user IDs and is intended for local or trusted environments until those controls are integrated.

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

- Authentication and ownership checks are not implemented because no identity provider is part of this repository.
- Rate limiting and request-size limits depend on the deployment boundary and are not faked locally.
- Hot inventory rows intentionally serialize competing reservations.
- Multi-item batching trades fewer round trips for more logical reads in one SQL batch.
- Redis outage increases Product latency but does not change correctness.
- A prolonged payment outage retains Pending work and can grow the Outbox backlog; recovery capacity needs production sizing and operational retention policy.
- Local measurements do not establish production capacity.

Further design detail is in [architecture](docs/architecture.md), [ADRs](docs/adr/), and [technical risks](docs/technical-risks.md).
