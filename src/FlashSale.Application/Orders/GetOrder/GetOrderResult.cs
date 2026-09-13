using FlashSale.Domain.Orders;

namespace FlashSale.Application.Orders.GetOrder;

public sealed record GetOrderResult(
    Guid Id,
    Guid UserId,
    OrderStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt,
    decimal Total,
    IReadOnlyList<GetOrderItemResult> Items)
{
    public static GetOrderResult From(Order order) => new(
        order.Id,
        order.UserId,
        order.Status,
        order.CreatedAt,
        order.ExpiresAt,
        order.Total,
        order.Items.Select(item => new GetOrderItemResult(item.ProductId, item.Quantity, item.UnitPrice)).ToArray());
}
