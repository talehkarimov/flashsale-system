using FlashSale.Domain.Products;

namespace FlashSale.Domain.Orders;

public sealed class OrderItem
{
    public const int MaxQuantity = 10000;

    private OrderItem() { }

    public OrderItem(Guid productId, int quantity, decimal unitPrice)
    {
        if (productId == Guid.Empty)
        {
            throw new ArgumentException("Product is required.", nameof(productId));
        }

        if (!IsValidQuantity(quantity))
        {
            throw new ArgumentOutOfRangeException(nameof(quantity));
        }

        if (!PriceRules.IsValid(unitPrice))
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice), "Price must be positive with at most two decimal places.");
        }

        ProductId = productId;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public Guid ProductId { get; private set; }
    public int Quantity { get; private set; }
    public decimal UnitPrice { get; private set; }

    public static bool IsValidQuantity(int quantity) => quantity is > 0 and <= MaxQuantity;
}
