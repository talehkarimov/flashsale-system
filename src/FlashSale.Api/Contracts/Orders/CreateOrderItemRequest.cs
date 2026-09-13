namespace FlashSale.Api.Contracts.Orders;

public sealed record CreateOrderItemRequest(Guid ProductId, int Quantity);
