using System.Data;
using FlashSale.Infrastructure.Outbox;
using FlashSale.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FlashSale.Infrastructure.Outbox.Persistence;

public sealed class SqlPaymentOutboxStore(SaleDbContext db, IOptions<OutboxOptions> options)
{
    public async Task<PaymentOutboxClaim?> ClaimNextAsync(CancellationToken cancellationToken)
    {
        var token = Guid.NewGuid();
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = db.Database.GetDbConnection().CreateCommand();
            if (db.Database.GetCommandTimeout() is { } commandTimeout)
            {
                command.CommandTimeout = commandTimeout;
            }

            // READCOMMITTEDLOCK keeps READPAST valid when the database enables read-committed snapshot isolation.
            command.CommandText = """
                ;WITH candidate AS (
                    SELECT TOP (1) Id, OrderId, NextAttemptAt, LeaseUntil, LeaseToken, Attempts, ProcessedAt
                    FROM OutboxMessages WITH (UPDLOCK, READPAST, READCOMMITTEDLOCK)
                    WHERE ProcessedAt IS NULL AND NextAttemptAt <= SYSDATETIMEOFFSET()
                      AND (LeaseUntil IS NULL OR LeaseUntil <= SYSDATETIMEOFFSET())
                    ORDER BY NextAttemptAt, Id
                )
                UPDATE candidate
                SET LeaseToken = @token,
                    LeaseUntil = DATEADD(second, @leaseSeconds, SYSDATETIMEOFFSET()),
                    Attempts = Attempts + 1
                OUTPUT inserted.Id, inserted.OrderId, inserted.Attempts;
                """;
            var tokenParameter = command.CreateParameter();
            tokenParameter.ParameterName = "@token";
            tokenParameter.DbType = DbType.Guid;
            tokenParameter.IsNullable = false;
            tokenParameter.Value = token;
            command.Parameters.Add(tokenParameter);

            var leaseParameter = command.CreateParameter();
            leaseParameter.ParameterName = "@leaseSeconds";
            leaseParameter.DbType = DbType.Int32;
            leaseParameter.IsNullable = false;
            leaseParameter.Value = checked((int)Math.Ceiling(options.Value.LeaseDuration.TotalSeconds));
            command.Parameters.Add(leaseParameter);

            await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
            return await reader.ReadAsync(cancellationToken)
                ? new PaymentOutboxClaim(reader.GetGuid(0), reader.GetGuid(1), token, reader.GetInt32(2))
                : null;
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    public async Task<bool> ScheduleRetryAsync(PaymentOutboxClaim claim, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        var delaySeconds = checked((int)Math.Ceiling(Math.Min(
            options.Value.MaxRetryDelay.TotalSeconds,
            options.Value.RetryDelay.TotalSeconds * Math.Pow(2, claim.Attempts - 1))));

        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE OutboxMessages
            SET NextAttemptAt = DATEADD(second, {delaySeconds}, SYSDATETIMEOFFSET()),
                LeaseToken = NULL, LeaseUntil = NULL
            WHERE Id = {claim.MessageId} AND LeaseToken = {claim.LeaseToken} AND ProcessedAt IS NULL
            """, cancellationToken);
        return affected == 1;
    }

    public async Task CompleteForOrderAsync(Guid orderId, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Outbox completion requires the terminal-order transaction.");
        }

        var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE OutboxMessages
            SET ProcessedAt = SYSDATETIMEOFFSET(), LeaseToken = NULL, LeaseUntil = NULL
            WHERE OrderId = {orderId}
            """, cancellationToken);
        if (affected != 1)
        {
            throw new InvalidOperationException("The order's Outbox message is missing.");
        }
    }
}
