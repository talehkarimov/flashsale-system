namespace FlashSale.Domain.Products;

public static class PriceRules
{
    public const int Precision = 11;
    public const int Scale = 2;
    public const decimal Maximum = 999999999.99m;

    public static bool IsValid(decimal value) =>
        value > 0 && value <= Maximum && decimal.Round(value, Scale) == value;
}
