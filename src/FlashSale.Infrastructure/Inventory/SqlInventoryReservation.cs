using FlashSale.Application.Orders.Errors;
using FlashSale.Domain.Orders;
using FlashSale.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using System.Data;

namespace FlashSale.Infrastructure.Inventory;

public sealed class SqlInventoryReservation(SaleDbContext db)
{
    public async Task ReserveAsync(IEnumerable<OrderItem> items, CancellationToken cancellationToken)
    {
        RequireTransaction();
        var ordered = items.OrderBy(item => item.ProductId).ToArray();
        if (ordered.Length == 0)
        {
            return;
        }

        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = db.Database.CurrentTransaction!.GetDbTransaction();
        if (db.Database.GetCommandTimeout() is { } timeout)
        {
            command.CommandTimeout = timeout;
        }

        command.CommandText = "SET NOCOUNT ON; ";
        for (var i = 0; i < ordered.Length; i++)
        {
            var item = ordered[i];
            var product = command.CreateParameter();
            product.ParameterName = $"@product{i}";
            product.DbType = DbType.Guid;
            product.Value = item.ProductId;
            command.Parameters.Add(product);
            var quantity = command.CreateParameter();
            quantity.ParameterName = $"@quantity{i}";
            quantity.DbType = DbType.Int32;
            quantity.Value = item.Quantity;
            command.Parameters.Add(quantity);
            command.CommandText += $"UPDATE Inventory SET AvailableQuantity = AvailableQuantity - @quantity{i} WHERE ProductId = @product{i} AND AvailableQuantity >= @quantity{i}; SELECT CAST(CASE WHEN @@ROWCOUNT = 1 THEN 1 ELSE 0 END AS bit);";
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        for (var i = 0; i < ordered.Length; i++)
        {
            if (!await reader.ReadAsync(cancellationToken) || !reader.GetBoolean(0))
            {
                throw new OrderRequestException(OrderErrorCode.InsufficientInventory, "Insufficient inventory.");
            }
            await reader.NextResultAsync(cancellationToken);
        }
    }

    public async Task ReleaseAsync(IEnumerable<OrderItem> items, CancellationToken cancellationToken)
    {
        RequireTransaction();
        foreach (var item in items.OrderBy(item => item.ProductId))
        {
            var affected = await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE Inventory
                SET AvailableQuantity = AvailableQuantity + {item.Quantity}
                WHERE ProductId = {item.ProductId}
                """, cancellationToken);

            if (affected != 1)
            {
                throw new InvalidOperationException("Reserved inventory is missing.");
            }
        }
    }

    private void RequireTransaction()
    {
        if (db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException("Inventory mutations require the caller's order transaction.");
        }
    }
}
