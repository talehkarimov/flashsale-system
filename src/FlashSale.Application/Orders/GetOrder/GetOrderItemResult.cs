namespace FlashSale.Application.Orders.GetOrder;

public sealed record GetOrderItemResult(Guid ProductId, int Quantity, decimal UnitPrice);
