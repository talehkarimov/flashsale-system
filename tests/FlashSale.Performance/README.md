# FlashSale baseline runner

Measurement tooling only. It references production implementations without changing them.
Run from the repository root with SQL Server available to the current user:

This harness has four distinct uses: the integration stress test is a correctness gate;
`load` is a closed-loop system load test; `probe` captures SQL/EF evidence and execution
plans; and the Product/payment/Redis modes are dependency-failure scenarios. None of these
local runs is a production capacity claim.

```powershell
dotnet build tests/FlashSale.Performance -c Release
dotnet tests/FlashSale.Performance/bin/Release/net8.0/FlashSale.Performance.dll . "$env:TEMP/FlashSaleBaseline/run-1" all 10 30
python tests/FlashSale.Performance/summarize.py "$env:TEMP/FlashSaleBaseline/run-1"
```

Arguments: repository root, output directory, `all` / `load` / `probe`, warm-up seconds,
measurement seconds, and optional workload name (`get-20`, `create-1`, `create-20`,
`sold-out`, `spike`, `outbox`, `payment-unavailable`, `payment-slow`, `payment-reject`,
`mixed`, `product`). Use a fresh output directory outside the repository.
For `product`, add an optional cache mode (`disabled`, `redis`, `redis-cold`, or
`redis-down`) and, optionally, a Redis connection string. `redis-cold` skips warm-up;
flush the selected Redis database before repeating it. `redis-down` measures SQL fallback
when Redis is unavailable. Arguments nine and ten select the PaymentSimulator scenario and
delay for payment workloads (`Success`, `Reject`, `Unavailable`, or `ChargeThenDelay`; the
default delay is five seconds). Optional `FLASHSALE_TEST_SQL` supplies the
server/authentication connection string. The runner
always substitutes unique database names and a pool maximum of 100. It needs database
creation/deletion and server Extended Events permissions. Databases and XE sessions are
removed on normal completion or exceptions; abrupt process termination may require cleanup
of the uniquely named databases printed at startup.

Load mode starts the built Release API and PaymentSimulator on available loopback ports.
Each workload runs at concurrency 10, 50, and 100. `spike` additionally runs 500 clients
against one zero-stock product. `mixed` uses a fixed 50% Product GET, 20% Order GET, and
30% CreateOrder profile. This is a closed-loop load test:
each client starts its next operation after its previous response completes. The fixed
admission window is followed by draining outstanding requests; throughput uses total elapsed
time including drain. Latencies include reading the response body. Success, HTTP 409 business
rejections, and technical failures have separate counts and distributions.
Outbox claims that find no eligible unlocked message are counted separately as idle polls.

The seed is reset before each concurrency case: 2,001 products and 10,000 terminal orders
with 20 items each. GET cycles across those orders. Successful creates use a separate
20-product group per client and stock of 1,000,000,000; sold-out requests share one zero-stock
product. Each POST has a fresh idempotency key. Product tests cycle across the 2,001 seeded
products and issue one metadata read per request. Outbox tests instead seed 100,000 due pending
orders/messages, each with one item. Seeded reservations are synthetic and excluded from
timing; existing integration tests remain the inventory correctness gate.

The hosted outbox worker is disabled for HTTP isolation. Outbox load calls the existing
processor in independent scopes at the requested concurrency, with real HTTP to the Success
simulator and its SQL ledger. This measures controlled concurrent processing, not the
one-worker production scheduling policy. Compare the DB terminal-completion delta with the
reported processor successes: the processor catches failures and schedules retries internally.

SQL plan capture runs separately from load. Extended Events includes direct ADO.NET claims
and EF commands. RPC totals exclude connection resets and transaction/savepoint protocol
traffic. SQL writes are XE's reported writes, not a count of rows mutated. Actual-plan
profiling and first-use statistics work can increase probe timings; use repeated timings
without plan capture for comparisons. The summarizer exports `.sqlplan` files, SQL, and
query summaries beside the captures and rejects truncated/dropped XE output.

Before/after SQL counter snapshots include index lock waits, log IO, server waits, deadlock
counts, and outbox state. Server-wide counters may include unrelated local activity.
Keep the host otherwise idle. Repeat identical runs to assess variance before accepting an
optimization. These local SQL Express results are not production capacity estimates.

Measured windows used for the engineering evidence were intentionally short on the local
machine. Some earlier before/after comparisons used different durations. Redis hit/miss
counters can include previous runs in the same container, and cold-cache results are not
intrinsically better than warm-cache results. Do not commit generated output directories;
write them under `$env:TEMP` as shown above.
