namespace FlashSale.Application.Products.GetProduct;

public sealed class GetProductHandler(IProductReader products)
{
    public Task<ProductReadModel?> HandleAsync(Guid id, CancellationToken cancellationToken) =>
        products.FindAsync(id, cancellationToken);
}
