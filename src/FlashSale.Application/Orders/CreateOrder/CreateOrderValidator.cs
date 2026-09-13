using FlashSale.Application.Orders.Errors;
using FlashSale.Domain.Orders;

namespace FlashSale.Application.Orders.CreateOrder;

public static class CreateOrderValidator
{
    public static void Validate(CreateOrderCommand request)
    {
        if (request.UserId == Guid.Empty)
        {
            throw InvalidRequest("UserId is required.");
        }

        if (request.Items is null || request.Items.Count is < 1 or > Order.MaxItems)
        {
            throw InvalidRequest($"Provide 1-{Order.MaxItems} order items.");
        }

        var productIds = new HashSet<Guid>();
        foreach (var item in request.Items)
        {
            if (item is null || item.ProductId == Guid.Empty)
            {
                throw InvalidRequest("Every item requires a ProductId.");
            }

            if (!OrderItem.IsValidQuantity(item.Quantity))
            {
                throw InvalidRequest($"Item quantities must be between 1 and {OrderItem.MaxQuantity}.");
            }

            if (!productIds.Add(item.ProductId))
            {
                throw InvalidRequest("Order products must be distinct.");
            }
        }
    }

    private static OrderRequestException InvalidRequest(string message) => new(OrderErrorCode.InvalidRequest, message);
}
