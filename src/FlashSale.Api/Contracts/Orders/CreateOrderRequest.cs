namespace FlashSale.Api.Contracts.Orders;

public sealed record CreateOrderRequest(Guid UserId, IReadOnlyList<CreateOrderItemRequest?>? Items);
