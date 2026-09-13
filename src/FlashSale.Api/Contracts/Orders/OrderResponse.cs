using FlashSale.Application.Orders.GetOrder;
using FlashSale.Domain.Orders;

namespace FlashSale.Api.Contracts.Orders;

public sealed record OrderResponse(
    Guid Id, Guid UserId, OrderStatus Status, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt,
    decimal Total, IReadOnlyList<OrderItemResponse> Items)
{
    public static OrderResponse From(GetOrderResult order) => new(
        order.Id, order.UserId, order.Status, order.CreatedAt, order.ExpiresAt, order.Total,
        order.Items.Select(item => new OrderItemResponse(item.ProductId, item.Quantity, item.UnitPrice)).ToArray());
}

public sealed record OrderItemResponse(Guid ProductId, int Quantity, decimal UnitPrice);
