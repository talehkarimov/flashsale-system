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

    /// <summary>Reserves inventory and queues asynchronous payment.</summary>
    /// <remarks>Requires one Idempotency-Key header: 1-128 visible ASCII characters, scoped to the user.
    /// Supply 1-20 distinct products with quantities of 1-10000. Replays match products and quantities
    /// regardless of item order. Both success responses include a Location header for polling.</remarks>
    /// <response code="201">A new Pending order was created.</response>
    /// <response code="200">The matching order's current state was replayed.</response>
    /// <response code="400">Invalid body or idempotency header.</response>
    /// <response code="404">A requested product does not exist.</response>
    /// <response code="409">Insufficient inventory or the key was reused with different items.</response>
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

    /// <summary>Returns the current order state; poll to observe asynchronous payment completion.</summary>
    /// <response code="404">The order does not exist or the route ID is not a GUID.</response>
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
