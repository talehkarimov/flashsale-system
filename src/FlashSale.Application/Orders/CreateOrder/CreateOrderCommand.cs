namespace FlashSale.Application.Orders.CreateOrder;

public sealed record CreateOrderCommand(Guid UserId, IReadOnlyList<CreateOrderItem> Items);
