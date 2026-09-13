using FlashSale.Api.Contracts.Orders;
using FlashSale.Application.Orders.CreateOrder;
using FlashSale.Application.Orders.Errors;
using FlashSale.Application.Orders.GetOrder;
using FlashSale.Domain.Orders;

namespace FlashSale.Api.Endpoints.Orders;

internal static class OrderEndpoints
{
    private const string CollectionRoute = "/orders";
    private const string IdempotencyHeader = "Idempotency-Key";

    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var orders = endpoints.MapGroup(CollectionRoute);
        orders.MapPost(string.Empty, CreateOrderAsync);
        orders.MapGet("/{id:guid}", GetOrderAsync);
        return endpoints;
    }

    private static async Task<IResult> CreateOrderAsync(
        CreateOrderRequest request,
        HttpContext context,
        CreateOrderHandler handler,
        CancellationToken cancellationToken)
    {
        var key = ReadIdempotencyKey(context.Request);
        var command = new CreateOrderCommand(request.UserId,
            request.Items?.Select(item => item is null ? null! : new CreateOrderItem(item.ProductId, item.Quantity)).ToArray()!);
        var result = await handler.HandleAsync(command, key, cancellationToken);
        var location = $"{CollectionRoute}/{result.Order.Id}";
        context.Response.Headers.Location = location;
        var response = OrderResponse.From(result.Order);
        return result.Replayed ? Results.Ok(response) : Results.Created(location, response);
    }

    private static async Task<IResult> GetOrderAsync(Guid id, GetOrderHandler handler, CancellationToken cancellationToken)
    {
        var order = await handler.HandleAsync(new GetOrderQuery(id), cancellationToken);
        return order is null ? Results.NotFound() : Results.Ok(OrderResponse.From(order));
    }

    private static IdempotencyKey ReadIdempotencyKey(HttpRequest request)
    {
        if (!request.Headers.TryGetValue(IdempotencyHeader, out var values) || values.Count != 1)
        {
            throw new OrderRequestException(OrderErrorCode.InvalidRequest, $"A single {IdempotencyHeader} header is required.");
        }

        if (!IdempotencyKey.IsValid(values[0]))
        {
            throw new OrderRequestException(OrderErrorCode.InvalidRequest,
                $"{IdempotencyHeader} must contain 1-{IdempotencyKey.MaxLength} visible ASCII characters.");
        }

        return new IdempotencyKey(values[0]!);
    }
}
