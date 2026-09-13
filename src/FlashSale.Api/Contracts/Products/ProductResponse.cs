using FlashSale.Application.Products.GetProduct;

namespace FlashSale.Api.Contracts.Products;

public sealed record ProductResponse(Guid Id, string Name, decimal Price)
{
    public static ProductResponse From(ProductReadModel product) => new(product.Id, product.Name, product.Price);
}
