using FlashSale.Application.Payments.ProcessOrderPayment;
using FlashSale.Domain.Orders;
using FlashSale.Infrastructure.Inventory;
using FlashSale.Infrastructure.Outbox.Persistence;
using FlashSale.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlashSale.Infrastructure.Orders.Persistence;

public sealed class SqlOrderPaymentCompletion(
    SaleDbContext db, SqlInventoryReservation inventory, SqlPaymentOutboxStore outbox) : IOrderPaymentCompletion
{
    public async Task CompleteAsync(Guid orderId, Func<Order, bool> transition, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var order = await db.Orders.FromSqlInterpolated($"""
            SELECT * FROM Orders WITH (UPDLOCK, HOLDLOCK) WHERE Id = {orderId}
            """).SingleAsync(cancellationToken);

        if (transition(order))
        {
            await inventory.ReleaseAsync(order.Items, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);
        await outbox.CompleteForOrderAsync(orderId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
