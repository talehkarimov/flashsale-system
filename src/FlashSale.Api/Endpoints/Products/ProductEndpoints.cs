using FlashSale.Api.Contracts.Products;
using FlashSale.Application.Products.GetProduct;

namespace FlashSale.Api.Endpoints.Products;

internal static class ProductEndpoints
{
    /// <summary>Returns product details without guaranteeing stock availability.</summary>
    /// <response code="404">The product does not exist or the route ID is not a GUID.</response>
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
