namespace FlashSale.Domain.Products;

public sealed class Product
{
    public const int MaxNameLength = 200;

    private Product() { }

    public Product(Guid id, string name, decimal price)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Product is required.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name) || name.Length > MaxNameLength)
        {
            throw new ArgumentException("Invalid product name.", nameof(name));
        }

        if (!PriceRules.IsValid(price))
        {
            throw new ArgumentOutOfRangeException(nameof(price), "Price must be positive with at most two decimal places.");
        }

        Id = id;
        Name = name;
        Price = price;
    }

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public decimal Price { get; private set; }
}
