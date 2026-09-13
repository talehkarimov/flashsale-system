using FlashSale.Application.Orders.CreateOrder;
using FlashSale.Domain.Orders;
using FlashSale.Domain.Products;
using FlashSale.Infrastructure.Inventory;
using FlashSale.Infrastructure.Outbox.Persistence;
using FlashSale.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlashSale.Infrastructure.Orders.Persistence;

public sealed class SqlOrderCreationStore(SaleDbContext db, SqlInventoryReservation inventory) : IOrderCreationStore
{
    public Task<Order?> FindByKeyAsync(Guid userId, IdempotencyKey key, CancellationToken cancellationToken) =>
        db.Orders.AsNoTracking().SingleOrDefaultAsync(order => order.UserId == userId && order.IdempotencyKey == key, cancellationToken);

    public async Task<IReadOnlyDictionary<Guid, Product>> ReadProductsAsync(
        IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken) =>
        await db.Products.AsNoTracking().Where(product => ids.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id, cancellationToken);

    public async Task<Order> CommitAsync(Order order, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            // Claim the unique operation before touching stock so concurrent retries wait on the key.
            db.Orders.Add(order);
            await db.SaveChangesAsync(cancellationToken);
            await inventory.ReserveAsync(order.Items, cancellationToken);
            db.Outbox.Add(new OrderPaymentOutboxMessage(order.Id));
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return order;
        }
        catch (DbUpdateException exception) when (SqlServerErrors.IsUniqueViolation(exception))
        {
            await transaction.RollbackAsync(cancellationToken);
            db.ChangeTracker.Clear();
            var existing = await FindByKeyAsync(order.UserId, order.IdempotencyKey, cancellationToken);
            if (existing is null)
            {
                throw;
            }

            return existing;
        }
    }
}
