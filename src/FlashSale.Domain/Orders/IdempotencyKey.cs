namespace FlashSale.Domain.Orders;

public sealed record IdempotencyKey
{
    public const int MaxLength = 128;

    public IdempotencyKey(string value)
    {
        if (!IsValid(value))
        {
            throw new ArgumentException($"Idempotency key must contain 1-{MaxLength} visible ASCII characters.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public static bool IsValid(string? value) =>
        !string.IsNullOrEmpty(value) && value.Length <= MaxLength && value.All(character => character is >= '!' and <= '~');

    public override string ToString() => Value;
}
