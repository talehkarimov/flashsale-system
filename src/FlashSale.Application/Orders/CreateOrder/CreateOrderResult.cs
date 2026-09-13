using FlashSale.Application.Orders.GetOrder;

namespace FlashSale.Application.Orders.CreateOrder;

public sealed record CreateOrderResult(GetOrderResult Order, bool Replayed);
