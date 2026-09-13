namespace FlashSale.Application.Orders.Errors;

public sealed class OrderRequestException(OrderErrorCode error, string message) : Exception(message)
{
    public OrderErrorCode Error { get; } = error;
}
