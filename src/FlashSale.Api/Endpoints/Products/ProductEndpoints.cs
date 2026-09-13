using FlashSale.Api.Contracts.Products;
using FlashSale.Application.Products.GetProduct;

namespace FlashSale.Api.Endpoints.Products;

internal static class ProductEndpoints
{
    public static IEndpointRouteBuilder MapProductEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/products/{id:guid}", async (Guid id, GetProductHandler handler, CancellationToken cancellationToken) =>
        {
            var product = await handler.HandleAsync(id, cancellationToken);
            return product is null ? Results.NotFound() : Results.Ok(ProductResponse.From(product));
        });
        return endpoints;
    }
}
