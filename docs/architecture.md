# Architecture

## Components and data ownership

Project references follow `FlashSale.Api -> FlashSale.Infrastructure -> FlashSale.Application -> FlashSale.Domain`. The API maps HTTP contracts; Application coordinates orders and payment; Domain enforces order transitions; Infrastructure implements SQL persistence, workers, payment HTTP, and product caching.

SQL Server owns orders, inventory, request idempotency, and payment work. Order items retain quantity and price snapshots in a single implicit currency with two decimal places. Checkout reads prices from SQL. Optional Redis caching serves product metadata only; it does not participate in reservations or payment.

`PaymentSimulator.Api` has separate contracts and a separate SQL ledger. The order ID is its payment operation ID. Its terminal outcome is authoritative when resolving payment or expiration.

## Order creation

`POST /orders` validates the request, fingerprints product IDs and quantities independently of item order, and resolves committed replays. A case-sensitive unique `(UserId, IdempotencyKey)` index scopes request identity to the user. Matching replays return 200 with the current order; changed items return 409.

After catalog reads, creation runs in one read-committed SQL transaction:

1. Insert the Pending order and items, claiming the idempotency key before competing for stock.
2. Reserve inventory with conditional decrements in stable product order.
3. Insert one payment Outbox record and commit.

Insufficient inventory rolls back the entire transaction. A duplicate-key failure rolls back before loading the winning order and checking its fingerprint. Successful creation returns 201 and a Location header; payment remains asynchronous.

Multi-item reservations use one parameterized SQL batch. This reduces round trips but increases logical reads within the command. Stable update ordering reduces deadlocks; hot inventory rows still serialize competing reservations. Database failures can surface as 500 responses and require caller retry with the same key.

## Payment work and recovery

The hosted Outbox worker claims eligible rows using `UPDLOCK`, `READPAST`, and `READCOMMITTEDLOCK`, supporting read-committed snapshot databases. Leases and scheduling use the database clock. Each iteration has a fresh scope and DbContext. A filtered `(NextAttemptAt, Id)` index with included lease and order fields avoids sorting candidates.

Before reservation expiry, the worker calls `POST /payments`; at or after expiry, it calls `POST /payments/close`. The same work item drives both payment and expiration. HTTP runs outside SQL transactions.

Unresolved work receives persisted exponential backoff capped at 60 seconds. Crashes and lease expiry can cause repeat processing. Claim tokens fence retry scheduling; they do not prevent an already-running provider call. The stable payment identity and locked terminal-order transition protect against duplicate charges and compensation.

`SqlOrderPaymentCompletion` locks the current order with `UPDLOCK, HOLDLOCK`, applies the Application transition, and commits order state, any inventory release, and Outbox completion together. A release flag and database constraints prevent unsuccessful orders from retaining inventory or releasing it twice.

## Provider contract and expiration

The simulator serializes pay/close using a key-range update lock and retains one outcome per operation: Paid, Rejected, or Cancelled. Replays must preserve amount and expiry. Close records Cancelled if no operation exists; later payment cannot replace it. If payment wins first, close returns Paid and inventory remains consumed.

Confirmed rejection produces Failed. Confirmed cancellation after the deadline produces Expired. Both release inventory in the terminal-order transaction. An ambiguous response never releases inventory, and Paid cannot become Failed or Expired.

Payment HTTP has attempt and total timeouts, bounded retries with exponential backoff and jitter, and a per-process circuit breaker. Response buffering remains inside the timeout and limits bodies to 64 KiB. Durable retries continue after HTTP retries are exhausted.

Simulator scenarios exercise distinct outcomes:

- `charge-then-delay`: records Paid before delaying the first response; replay returns the recorded result without that delay.
- `reject`: records Rejected.
- `unavailable`: returns 503 before accepting payment, while close remains available. An unreachable simulator prevents both operations.

## Product reads

`GET /products/{id}` optionally uses versioned Redis keys with a five-minute absolute TTL and bounded cache operations. Misses and failures fall back to SQL; cache-write failure does not discard a successful SQL result. Redis is disabled by default.

There is no product update API or cache invalidation path. Concurrent cold requests can duplicate SQL reads. A cache outage increases latency and database load; checkout prices always come from SQL.

## Operational constraints

- Existing boundary controls include parameterized SQL, bounded item/quantity validation, generic production errors, disabled payment redirects/cookies, and HTTPS validation for non-loopback payment URLs. These do not provide caller identity or authorization.
- A real provider must retain payment and cancellation identities for the replay lifetime and support equivalent atomic close semantics. Otherwise, ambiguous payments require a reconciliation/refund design before inventory can be released.
- Expiration depends on provider and database recovery. Outages or persistently invalid provider responses can hold Pending orders and inventory beyond the deadline; recovery throughput needs deployment-specific sizing.
- Order idempotency, payment, and Outbox records are retained. Cleanup must preserve replay and cancellation guarantees. Retention policy, backlog alerting, and intervention for persistently failing work are not implemented.
- Application hosts and databases need synchronized UTC clocks. Skew can delay resolution but cannot replace a provider's terminal outcome.
- Authentication and order-ownership authorization are absent; user IDs are client-supplied. Public deployment also requires rate limits and a request-size policy.
- Integration hosts share a process with independent service providers. The 1,000-request test checks SQL invariants; it does not establish throughput or validate separately deployed replicas.
