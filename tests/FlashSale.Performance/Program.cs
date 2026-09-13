using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FlashSale.Application.Orders.CreateOrder;
using FlashSale.Domain.Orders;
using FlashSale.Domain.Products;
using FlashSale.Infrastructure;
using FlashSale.Infrastructure.Outbox.Persistence;
using FlashSale.Infrastructure.Outbox.Processing;
using FlashSale.Infrastructure.Orders.Persistence;
using FlashSale.Infrastructure.Inventory;
using FlashSale.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PaymentSimulator.Api.Persistence;
using FlashSale.Infrastructure.Outbox;

namespace FlashSale.Performance;

internal static class Program
{
    private static readonly Guid User = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid[] Products = Enumerable.Range(1, 2001).Select(Id).ToArray();
    private static string sale = "";
    private static string payment = "";
    private static string output = "";
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static async Task Main(string[] args)
    {
        var root = Path.GetFullPath(args.ElementAtOrDefault(0) ?? ".");
        output = Path.GetFullPath(args.ElementAtOrDefault(1) ?? Path.Combine(Path.GetTempPath(), "FlashSaleBaseline", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")));
        var mode = args.ElementAtOrDefault(2) ?? "all";
        var warmup = int.Parse(args.ElementAtOrDefault(3) ?? "10");
        var seconds = int.Parse(args.ElementAtOrDefault(4) ?? "30");
        var onlyWorkload = args.ElementAtOrDefault(5);
        string[] workloads = ["product", "get-20", "create-1", "create-20", "sold-out", "spike", "outbox", "payment-unavailable", "payment-slow", "payment-reject", "mixed"];
        var cacheMode = args.ElementAtOrDefault(6) ?? "disabled";
        var cacheConnection = args.ElementAtOrDefault(7) ?? "localhost:6379,connectTimeout=100,syncTimeout=100,abortConnect=false";
        var paymentScenario = args.ElementAtOrDefault(8) ?? "Success";
        var paymentDelay = args.ElementAtOrDefault(9) ?? "00:00:05";
        if (mode is not ("all" or "load" or "probe") || warmup < 0 || seconds < 1)
            throw new ArgumentException("Use all/load/probe, nonnegative warm-up, and positive measurement seconds.");
        if (onlyWorkload is not null && !workloads.Contains(onlyWorkload))
            throw new ArgumentException("Unknown workload: " + onlyWorkload);
        if (cacheMode is not ("disabled" or "redis" or "redis-cold" or "redis-down"))
            throw new ArgumentException("Cache mode must be disabled, redis, redis-cold, or redis-down.");
        if (Path.GetRelativePath(root, output) is var relative && !relative.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(relative))
            throw new ArgumentException("Measurement output must be outside the repository.");
        Directory.CreateDirectory(output);
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var baseConnection = Environment.GetEnvironmentVariable("FLASHSALE_TEST_SQL") ?? @"Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True";
        sale = Connection(baseConnection, "FlashSaleBaseline_" + suffix, "FlashSaleBaseline");
        payment = Connection(baseConnection, "FlashSaleBaselinePayments_" + suffix, "FlashSaleBaselinePayment");
        Console.WriteLine($"Output: {output}");
        Console.WriteLine($"Isolated databases: {new SqlConnectionStringBuilder(sale).InitialCatalog}, {new SqlConnectionStringBuilder(payment).InitialCatalog}");
        var processes = new List<Process>();
        try
        {
            await using (var db = Db()) { await db.Database.MigrateAsync(); }
            await using (var db = new PaymentDbContext(new DbContextOptionsBuilder<PaymentDbContext>().UseSqlServer(payment).Options)) { await db.Database.MigrateAsync(); }
            SqlConnection.ClearAllPools();
            await Sql(Connection(baseConnection, "master", "FlashSaleBaselineAdmin"), $"ALTER DATABASE [{new SqlConnectionStringBuilder(sale).InitialCatalog}] SET READ_COMMITTED_SNAPSHOT ON; ALTER DATABASE [{new SqlConnectionStringBuilder(payment).InitialCatalog}] SET READ_COMMITTED_SNAPSHOT ON;");
            await EnvironmentRecord(warmup, seconds);
            if (mode is "all" or "probe") await Probe();
            if (mode is "all" or "load")
            {
                var apiPort = FreePort();
                var paymentPort = FreePort();
                var simulatorScenario = paymentScenario;
                if (onlyWorkload == "payment-unavailable") simulatorScenario = "Unavailable";
                if (onlyWorkload == "payment-slow") simulatorScenario = "ChargeThenDelay";
                if (onlyWorkload == "payment-reject") simulatorScenario = "Reject";
                processes.Add(Start(root, "PaymentSimulator.Api", paymentPort, new() { ["ConnectionStrings__Payments"] = payment, ["Simulator__Scenario"] = simulatorScenario, ["Simulator__Delay"] = paymentDelay }));
                var apiSettings = new Dictionary<string, string> { ["ConnectionStrings__FlashSale"] = sale, ["Outbox__WorkerEnabled"] = "false", ["Payment__BaseAddress"] = $"http://localhost:{paymentPort}/" };
                if (workloadCache(cacheMode, onlyWorkload))
                {
                    apiSettings["ProductCache__Enabled"] = "true";
                    apiSettings["ProductCache__ConnectionString"] = cacheMode == "redis-down" ? "localhost:6399,connectTimeout=50,syncTimeout=50,abortConnect=false" : cacheConnection;
                    apiSettings["ProductCache__OperationTimeout"] = "00:00:00.050";
                }
                processes.Add(Start(root, "FlashSale.Api", apiPort, apiSettings));
                using var client = new HttpClient(new SocketsHttpHandler { MaxConnectionsPerServer = 200 }) { BaseAddress = new Uri($"http://localhost:{apiPort}/"), Timeout = TimeSpan.FromSeconds(60) };
                using var paymentClient = new HttpClient { BaseAddress = new Uri($"http://localhost:{paymentPort}/") };
                await Ready(client); await Ready(paymentClient);
                var services = new ServiceCollection();
                services.AddLogging(b => b.SetMinimumLevel(LogLevel.None));
                var processorErrors = new ProcessorErrorLogger();
                services.AddSingleton<ILogger<PaymentOutboxProcessor>>(processorErrors);
                services.AddFlashSale(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:FlashSale"] = sale,
                    ["Payment:BaseAddress"] = $"http://localhost:{paymentPort}/",
                    ["Outbox:WorkerEnabled"] = "false"
                }).Build());
                await using var provider = services.BuildServiceProvider();
                foreach (var workload in workloads.Where(w => onlyWorkload is null || w == onlyWorkload))
                {
                var levels = onlyWorkload == "spike" ? new[] { 10, 50, 100, 500 } : new[] { 10, 50, 100 };
                foreach (var concurrency in levels)
                {
                    var outboxWorkload = workload is "outbox" or "payment-unavailable" or "payment-slow" or "payment-reject";
                    await Seed(outboxWorkload ? 100000 : 10000, outboxWorkload);
                    var getIds = await ReadIds();
                    var bodies = Enumerable.Range(0, concurrency).Select(worker => JsonSerializer.Serialize(new
                    {
                        userId = User,
                        items = (workload is "sold-out" or "spike" ? new[] { Products[^1] } : Products.Skip(worker * 20).Take(workload == "create-20" ? 20 : 1))
                            .Select(productId => new { productId, quantity = 1 }).ToArray()
                    })).ToArray();
                    var sequence = 0L;
                    async Task<int> Operation(int worker)
                    {
                        if (outboxWorkload)
                        {
                            var attempt = processorErrors.StartAttempt();
                            await using var scope = provider.CreateAsyncScope();
                            var claimed = await scope.ServiceProvider.GetRequiredService<PaymentOutboxProcessor>().ProcessNextAsync(default);
                            return attempt.Failed ? 500 : claimed ? 200 : 204;
                        }
                        if (workload == "product")
                        {
                            using var response = await client.GetAsync($"products/{Products[(int)(Interlocked.Increment(ref sequence) % Products.Length)]}");
                            await response.Content.LoadIntoBufferAsync();
                            return (int)response.StatusCode;
                        }
                        if (workload.StartsWith("get", StringComparison.Ordinal))
                        {
                            using var response = await client.GetAsync($"orders/{getIds[(int)(Interlocked.Increment(ref sequence) % getIds.Length)]}");
                            await response.Content.LoadIntoBufferAsync();
                            return (int)response.StatusCode;
                        }
                        if (workload == "mixed")
                        {
                            var choice = (int)(Interlocked.Increment(ref sequence) % 20);
                            if (choice < 10) { using var product = await client.GetAsync($"products/{Products[choice]}"); await product.Content.LoadIntoBufferAsync(); return (int)product.StatusCode; }
                            if (choice < 14) { using var order = await client.GetAsync($"orders/{getIds[choice]}"); await order.Content.LoadIntoBufferAsync(); return (int)order.StatusCode; }
                        }
                        using var request = new HttpRequestMessage(HttpMethod.Post, "orders") { Content = new StringContent(bodies[worker], System.Text.Encoding.UTF8, "application/json") };
                        request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString("N"));
                        using var result = await client.SendAsync(request);
                        await result.Content.LoadIntoBufferAsync();
                        return (int)result.StatusCode;
                    }
                    Console.WriteLine($"Warmup {workload} c={concurrency}");
                    if (warmup > 0) await Run(workload, concurrency, warmup, Operation, false);
                    var before = await Counters();
                    var backlogBefore = await Scalar("SELECT COUNT(*) FROM OutboxMessages WHERE ProcessedAt IS NULL");
                    var result = await Run(workload, concurrency, seconds, Operation, true);
                    var after = await Counters();
                    var backlogAfter = await Scalar("SELECT COUNT(*) FROM OutboxMessages WHERE ProcessedAt IS NULL");
                    await File.WriteAllTextAsync(Path.Combine(output, $"{workload}-{concurrency}.json"), JsonSerializer.Serialize(new { result, before, after, backlogBefore, backlogAfter }, Json));
                    Console.WriteLine(JsonSerializer.Serialize(result));
                }
                }
            }
        }
        finally
        {
            foreach (var process in processes)
            {
                if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
                process.Dispose();
            }
            SqlConnection.ClearAllPools();
            await using (var db = Db()) { await db.Database.EnsureDeletedAsync(); }
            await using (var db = new PaymentDbContext(new DbContextOptionsBuilder<PaymentDbContext>().UseSqlServer(payment).Options)) { await db.Database.EnsureDeletedAsync(); }
        }
    }

    private static bool workloadCache(string mode, string? workload) => workload is "product" or "mixed" && mode != "disabled";

    private static Guid Id(int value) => Guid.Parse($"10000000-0000-0000-0000-{value:000000000000}");
    private static string Connection(string source, string database, string app) => new SqlConnectionStringBuilder(source) { InitialCatalog = database, ApplicationName = app, MaxPoolSize = 100 }.ConnectionString;
    private static SaleDbContext Db(string? connection = null) => new(new DbContextOptionsBuilder<SaleDbContext>().UseSqlServer(connection ?? sale).Options);
    private static async Task Sql(string connection, string sql)
    {
        await using var cn = new SqlConnection(connection); await cn.OpenAsync();
        await using var command = new SqlCommand(sql, cn) { CommandTimeout = 180 }; await command.ExecuteNonQueryAsync();
    }
    private static async Task<int> Scalar(string sql)
    {
        await using var cn = new SqlConnection(sale); await cn.OpenAsync();
        await using var command = new SqlCommand(sql, cn); return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static async Task Seed(int orders, bool due)
    {
        await Sql(payment, "DELETE FROM PaymentOperations;");
        await Sql(sale, "DELETE FROM OutboxMessages; DELETE FROM OrderItems; DELETE FROM Orders; DELETE FROM Inventory; DELETE FROM Products;");
        await using (var db = Db())
        {
            db.Products.AddRange(Products.Select((id, i) => new Product(id, $"Baseline product {i:D4}", 19.99m)));
            db.Inventory.AddRange(Products.Select((id, i) => new FlashSale.Domain.Inventory.Inventory(id, i == Products.Length - 1 ? 0 : 1000000000)));
            await db.SaveChangesAsync();
        }
        await Sql(sale, $"""
            SELECT TOP ({orders}) NEWID() AS Id, ROW_NUMBER() OVER (ORDER BY (SELECT NULL)) AS n INTO #seed
            FROM sys.all_objects a CROSS JOIN sys.all_objects b;
            INSERT Orders (Id,UserId,Status,CreatedAt,ExpiresAt,IdempotencyKey,RequestHash,InventoryReleased)
            SELECT Id,'{User}','{(due ? "Pending" : "Paid")}',SYSDATETIMEOFFSET(),DATEADD(day,1,SYSDATETIMEOFFSET()),CONCAT('seed-',n),REPLICATE('0',64),0 FROM #seed;
            INSERT OrderItems (OrderId,ProductId,Quantity,UnitPrice)
            SELECT s.Id,p.Id,1,p.Price FROM #seed s CROSS JOIN (SELECT TOP ({(due ? 1 : 20)}) Id,Price FROM Products ORDER BY Id) p;
            INSERT OutboxMessages (Id,OrderId,NextAttemptAt,Attempts,ProcessedAt)
            SELECT NEWID(),Id,DATEADD(minute,-1,SYSDATETIMEOFFSET()),0,{(due ? "NULL" : "SYSDATETIMEOFFSET()")} FROM #seed;
            UPDATE STATISTICS Orders WITH FULLSCAN; UPDATE STATISTICS OrderItems WITH FULLSCAN;
            UPDATE STATISTICS Products WITH FULLSCAN; UPDATE STATISTICS Inventory WITH FULLSCAN; UPDATE STATISTICS OutboxMessages WITH FULLSCAN;
            """);
    }

    private static async Task<Guid[]> ReadIds()
    {
        await using var db = Db(); return await db.Orders.AsNoTracking().OrderBy(o => o.Id).Select(o => o.Id).Take(10000).ToArrayAsync();
    }

    private static async Task<object> Run(string workload, int concurrency, int seconds, Func<int, Task<int>> operation, bool collect)
    {
        var samples = new ConcurrentBag<(double Ms, int Status)>();
        var errors = new ConcurrentDictionary<string, int>();
        var clock = Stopwatch.StartNew();
        await Task.WhenAll(Enumerable.Range(0, concurrency).Select(async worker =>
        {
            while (clock.Elapsed.TotalSeconds < seconds)
            {
                var start = Stopwatch.GetTimestamp();
                var status = 0;
                try { status = await operation(worker); }
                catch (Exception exception) { errors.AddOrUpdate(exception.GetType().Name + ": " + exception.Message, 1, (_, count) => count + 1); }
                if (collect) samples.Add((Stopwatch.GetElapsedTime(start).TotalMilliseconds, status));
            }
        }));
        var elapsed = clock.Elapsed.TotalSeconds;
        var values = samples.ToArray();
        object Group(IEnumerable<(double Ms, int Status)> source)
        {
            var sorted = source.Select(s => s.Ms).Order().ToArray();
            double? Percentile(double fraction) => sorted.Length == 0 ? null : sorted[Math.Clamp((int)Math.Ceiling(sorted.Length * fraction) - 1, 0, sorted.Length - 1)];
            return new { count = sorted.Length, throughput = sorted.Length / elapsed, p50 = Percentile(.50), p95 = Percentile(.95), p99 = Percentile(.99) };
        }
        bool Success(int status) => status is 200 or 201;
        bool Business(int status) => status == 409;
        bool Idle(int status) => workload == "outbox" && status == 204;
        return new
        {
            workload, concurrency, requestedSeconds = seconds, elapsedSeconds = elapsed,
            all = Group(values), success = Group(values.Where(s => Success(s.Status))), business = Group(values.Where(s => Business(s.Status))),
            idle = Group(values.Where(s => Idle(s.Status))),
            technical = Group(values.Where(s => !Success(s.Status) && !Business(s.Status) && !Idle(s.Status))),
            technicalErrorRate = values.Length == 0 ? 0 : values.Count(s => !Success(s.Status) && !Business(s.Status) && !Idle(s.Status)) / (double)values.Length,
            statuses = values.GroupBy(s => s.Status).ToDictionary(g => g.Key, g => g.Count()), errors
        };
    }

    private static int FreePort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0); listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop(); return port;
    }
    private static Process Start(string root, string project, int port, Dictionary<string, string> settings)
    {
        var info = new ProcessStartInfo("dotnet") { WorkingDirectory = Path.Combine(root, "src", project), UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add(Path.Combine(root, "src", project, "bin", "Release", "net8.0", project + ".dll"));
        info.ArgumentList.Add("--urls"); info.ArgumentList.Add($"http://localhost:{port}");
        info.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        info.Environment["Logging__LogLevel__Default"] = "None";
        info.Environment["Logging__LogLevel__Microsoft.AspNetCore"] = "None";
        info.Environment["Logging__LogLevel__Microsoft.EntityFrameworkCore.Database.Command"] = "None";
        foreach (var pair in settings) info.Environment[pair.Key] = pair.Value;
        var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start " + project);
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) Console.WriteLine(project + ": " + e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) Console.WriteLine(project + ": " + e.Data); };
        process.BeginOutputReadLine(); process.BeginErrorReadLine(); return process;
    }
    private static async Task Ready(HttpClient client)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            try { using var response = await client.GetAsync("/"); return; }
            catch (HttpRequestException) { await Task.Delay(100); }
        }
        throw new TimeoutException("Server startup failed: " + client.BaseAddress);
    }

    private static async Task<object[]> Rows(string sql, string? connection = null)
    {
        await using var cn = new SqlConnection(connection ?? sale); await cn.OpenAsync();
        await using var command = new SqlCommand(sql, cn) { CommandTimeout = 60 };
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<object>();
        do
        {
            while (await reader.ReadAsync())
            {
                var row = new Dictionary<string, object?>();
                for (var i = 0; i < reader.FieldCount; i++) row[reader.GetName(i)] = reader.IsDBNull(i) ? null : reader.GetValue(i);
                rows.Add(row);
            }
        } while (await reader.NextResultAsync());
        return rows.ToArray();
    }
    private static Task<object[]> Counters() => Rows("""
        SELECT 'wait' AS kind,wait_type,waiting_tasks_count,wait_time_ms FROM sys.dm_os_wait_stats
        WHERE wait_type LIKE 'LCK%' OR wait_type IN ('WRITELOG','PAGEIOLATCH_SH','PAGELATCH_EX','THREADPOOL','SOS_SCHEDULER_YIELD');
        SELECT 'index' AS kind,OBJECT_NAME(object_id) AS table_name,index_id,leaf_insert_count,leaf_update_count,leaf_allocation_count,
            row_lock_wait_count,row_lock_wait_in_ms,page_lock_wait_count,page_lock_wait_in_ms
        FROM sys.dm_db_index_operational_stats(DB_ID(),NULL,NULL,NULL);
        SELECT 'log' AS kind,num_of_writes,num_of_bytes_written,io_stall_write_ms FROM sys.dm_io_virtual_file_stats(DB_ID(),NULL) WHERE file_id=2;
        SELECT 'outbox' AS kind,COUNT(*) AS total,SUM(CASE WHEN ProcessedAt IS NOT NULL THEN 1 ELSE 0 END) AS processed,
            SUM(CASE WHEN Attempts>1 THEN 1 ELSE 0 END) AS retried FROM OutboxMessages;
        SELECT 'deadlock' AS kind,cntr_value FROM sys.dm_os_performance_counters WHERE counter_name='Number of Deadlocks/sec' AND instance_name='_Total';
        """);
    private static async Task EnvironmentRecord(int warmup, int seconds)
    {
        var rows = await Rows("""
            SELECT @@VERSION AS sql_version,SERVERPROPERTY('Edition') AS edition;
            SELECT name,compatibility_level,is_read_committed_snapshot_on FROM sys.databases WHERE database_id=DB_ID();
            SELECT * FROM __EFMigrationsHistory;
            SELECT cpu_count,physical_memory_kb,committed_kb FROM sys.dm_os_sys_info;
            SELECT COUNT(*) AS visible_online_schedulers FROM sys.dm_os_schedulers WHERE status='VISIBLE ONLINE';
            SELECT name,value_in_use FROM sys.configurations WHERE name IN ('max degree of parallelism','max server memory (MB)');
            """);
        await File.WriteAllTextAsync(Path.Combine(output,"environment.json"),JsonSerializer.Serialize(new
        {
            runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
            os = System.Runtime.InteropServices.RuntimeInformation.OSDescription, processors = Environment.ProcessorCount,
            memoryAvailable = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, configuration = "Release", poolSize = 100,
            hostedOutboxWorkers = 0, processorConcurrency = new[] { 10, 50, 100 }, simulator = "Success; real HTTP and SQL ledger; no artificial delay",
            warmupSeconds = warmup, measurementSeconds = seconds, logging = "Disabled during timed load", rows,
            paymentMigrations = await Rows("SELECT * FROM __EFMigrationsHistory", payment)
        },Json));
    }

    private static async Task Probe()
    {
        await Seed(100000, true);
        var probe = new SqlConnectionStringBuilder(sale) { ApplicationName = "FlashSaleBaselineProbe" }.ConnectionString;
        var ids = await ReadIds();
        var transactions = new TransactionTimings();
        await using var db = new SaleDbContext(new DbContextOptionsBuilder<SaleDbContext>().UseSqlServer(probe).AddInterceptors(transactions).Options);
        var reader = new SqlOrderReader(db);
        var inventory = new SqlInventoryReservation(db);
        var store = new SqlOrderCreationStore(db, inventory);
        var handler = new CreateOrderHandler(store, TimeProvider.System, TimeSpan.FromMinutes(2));
        var outbox = new SqlPaymentOutboxStore(db, Options.Create(new OutboxOptions()));
        var timings = new List<object>();
        async Task Capture(string name, Func<Task> action, bool plans = true)
        {
            var session = "FlashSaleBaseline_" + Guid.NewGuid().ToString("N");
            await Sql(sale, $"""
                CREATE EVENT SESSION [{session}] ON SERVER
                ADD EVENT sqlserver.rpc_completed(ACTION(sqlserver.sql_text,sqlserver.session_id) WHERE ([sqlserver].[client_app_name]=N'FlashSaleBaselineProbe')),
                ADD EVENT sqlserver.sql_batch_completed(ACTION(sqlserver.sql_text,sqlserver.session_id) WHERE ([sqlserver].[client_app_name]=N'FlashSaleBaselineProbe')),
                ADD EVENT sqlserver.sql_statement_completed(ACTION(sqlserver.sql_text,sqlserver.session_id) WHERE ([sqlserver].[client_app_name]=N'FlashSaleBaselineProbe'))
                {(plans ? ",ADD EVENT sqlserver.query_post_execution_showplan(ACTION(sqlserver.sql_text,sqlserver.session_id) WHERE ([sqlserver].[client_app_name]=N'FlashSaleBaselineProbe'))" : "")}
                ADD TARGET package0.ring_buffer(SET max_memory=16384) WITH (MAX_MEMORY=16384 KB,MAX_DISPATCH_LATENCY=1 SECONDS);
                ALTER EVENT SESSION [{session}] ON SERVER STATE=START;
                """);
            try
            {
                var watch = Stopwatch.StartNew(); await action(); timings.Add(new { name, elapsedMs = watch.Elapsed.TotalMilliseconds });
                await Task.Delay(1200);
                await using var cn = new SqlConnection(sale); await cn.OpenAsync();
                await using var command = new SqlCommand($"SELECT CAST(t.target_data AS nvarchar(max)) FROM sys.dm_xe_session_targets t JOIN sys.dm_xe_sessions s ON s.address=t.event_session_address WHERE s.name='{session}'", cn);
                var xml = (string?)await command.ExecuteScalarAsync() ?? throw new InvalidOperationException("Missing XE target");
                await File.WriteAllTextAsync(Path.Combine(output, name + ".xml"),xml);
            }
            finally { await Sql(sale, $"DROP EVENT SESSION [{session}] ON SERVER;"); }
        }
        await Capture("claim-due-initial", async () => { await outbox.ClaimNextAsync(default); });
        await reader.FindAsync(ids[0], default);
        await store.ReadProductsAsync(Products.Take(20).ToArray(), default);
        await store.FindByKeyAsync(User, new IdempotencyKey("seed-1"), default);
        await Capture("product-20",async () => { await store.ReadProductsAsync(Products.Take(20).ToArray(), default); });
        await Capture("get-order",async () => { await reader.FindAsync(ids[0], default); });
        await Capture("idempotency",async () => { await store.FindByKeyAsync(User, new IdempotencyKey("seed-1"), default); });
        await Capture("reserve",async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            var watch = Stopwatch.StartNew(); await inventory.ReserveAsync(new[] { new OrderItem(Products[0], 1, 19.99m) }, default);
            await transaction.RollbackAsync(); timings.Add(new { name = "reserve-transaction", elapsedMs = watch.Elapsed.TotalMilliseconds });
        });
        Guid createdId = default;
        foreach (var count in new[] { 1, 20 })
        {
            await Capture("create-" + count,async () => { createdId = (await handler.HandleAsync(new CreateOrderCommand(User,Products.Take(count).Select(p => new CreateOrderItem(p,1)).ToArray()),new IdempotencyKey(Guid.NewGuid().ToString("N")),default)).Order.Id; });
            db.ChangeTracker.Clear();
        }
        await Capture("get-order-20",async () => { await reader.FindAsync(createdId, default); });
        async Task Repeated(string name, Func<Task> action)
        {
            await action();
            await Capture(name + "-timing", async () => { for (var i = 0; i < 20; i++) await action(); }, plans: false);
        }
        await Repeated("product-20",async () => { await store.ReadProductsAsync(Products.Take(20).ToArray(), default); });
        await Repeated("get-order-20",async () => { await reader.FindAsync(createdId, default); });
        await Repeated("idempotency",async () => { await store.FindByKeyAsync(User, new IdempotencyKey("seed-1"), default); });
        await Repeated("reserve",async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync();
            await inventory.ReserveAsync(new[] { new OrderItem(Products[0], 1, 19.99m) },default);
            await transaction.RollbackAsync();
        });
        foreach (var count in new[] { 1, 20 })
        {
            transactions.Label = "create-" + count;
            await Repeated("create-" + count,async () =>
            {
                await handler.HandleAsync(new CreateOrderCommand(User,Products.Take(count).Select(p => new CreateOrderItem(p,1)).ToArray()),new IdempotencyKey(Guid.NewGuid().ToString("N")),default);
                db.ChangeTracker.Clear();
            });
        }
        transactions.Label = "other";
        await outbox.ClaimNextAsync(default);
        await Capture("claim-due",async () => { await outbox.ClaimNextAsync(default); });
        await Repeated("claim-due",async () => { await outbox.ClaimNextAsync(default); });
        await Sql(sale,"UPDATE OutboxMessages SET LeaseToken=NEWID(),LeaseUntil=DATEADD(minute,10,SYSDATETIMEOFFSET()) WHERE ProcessedAt IS NULL;");
        await outbox.ClaimNextAsync(default);
        await Capture("claim-leased",async () => { await outbox.ClaimNextAsync(default); });
        await Repeated("claim-leased",async () => { await outbox.ClaimNextAsync(default); });
        await File.WriteAllTextAsync(Path.Combine(output,"probe-timings.json"),JsonSerializer.Serialize(timings,Json));
        await File.WriteAllTextAsync(Path.Combine(output,"transaction-timings.json"),JsonSerializer.Serialize(transactions.Samples,Json));
    }
}
