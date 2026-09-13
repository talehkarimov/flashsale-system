using FlashSale.Application.Orders.GetOrder;
using FlashSale.Domain.Orders;
using FlashSale.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlashSale.Infrastructure.Orders.Persistence;

public sealed class SqlOrderReader(SaleDbContext db) : IOrderReader
{
    public Task<Order?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Orders.AsNoTracking().SingleOrDefaultAsync(order => order.Id == id, cancellationToken);
}
