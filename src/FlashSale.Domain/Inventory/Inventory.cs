namespace FlashSale.Domain.Inventory;

public sealed class Inventory
{
    private Inventory() { }

    public Inventory(Guid productId, int availableQuantity)
    {
        if (productId == Guid.Empty)
        {
            throw new ArgumentException("Product is required.", nameof(productId));
        }

        ArgumentOutOfRangeException.ThrowIfNegative(availableQuantity);
        ProductId = productId;
        AvailableQuantity = availableQuantity;
    }

    public Guid ProductId { get; private set; }
    public int AvailableQuantity { get; private set; }
}
