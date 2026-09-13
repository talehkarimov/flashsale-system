using FlashSale.Domain.Orders;

namespace FlashSale.Application.Orders.GetOrder;

public interface IOrderReader
{
    Task<Order?> FindAsync(Guid id, CancellationToken cancellationToken);
}
