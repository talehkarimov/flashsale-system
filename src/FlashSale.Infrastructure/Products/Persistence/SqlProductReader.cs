using FlashSale.Application.Products.GetProduct;
using FlashSale.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FlashSale.Infrastructure.Products.Persistence;

public sealed class SqlProductReader(SaleDbContext db) : IProductReader
{
    public Task<ProductReadModel?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Products.AsNoTracking()
            .Where(product => product.Id == id)
            .Select(product => new ProductReadModel(product.Id, product.Name, product.Price))
            .SingleOrDefaultAsync(cancellationToken);
}
