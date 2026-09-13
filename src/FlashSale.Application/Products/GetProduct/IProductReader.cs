namespace FlashSale.Application.Products.GetProduct;

public interface IProductReader
{
    Task<ProductReadModel?> FindAsync(Guid id, CancellationToken cancellationToken);
}
