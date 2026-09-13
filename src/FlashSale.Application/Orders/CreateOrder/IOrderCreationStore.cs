using FlashSale.Domain.Orders;
using FlashSale.Domain.Products;

namespace FlashSale.Application.Orders.CreateOrder;

public interface IOrderCreationStore
{
    Task<Order?> FindByKeyAsync(Guid userId, IdempotencyKey key, CancellationToken cancellationToken);
    Task<IReadOnlyDictionary<Guid, Product>> ReadProductsAsync(IReadOnlyCollection<Guid> ids, CancellationToken cancellationToken);

    // Atomically claims the key, reserves inventory, and enqueues payment; returns the winning order on a duplicate key.
    Task<Order> CommitAsync(Order order, CancellationToken cancellationToken);
}
