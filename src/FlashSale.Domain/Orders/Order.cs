namespace FlashSale.Domain.Orders;

public sealed class Order
{
    public const int MaxItems = 20;
    private readonly List<OrderItem> _items = [];

    private Order() { }

    public Order(
        Guid userId,
        IdempotencyKey idempotencyKey,
        string requestHash,
        IEnumerable<OrderItem> items,
        DateTimeOffset createdAt,
        TimeSpan reservationPeriod)
    {
        if (userId == Guid.Empty)
        {
            throw new ArgumentException("User is required.", nameof(userId));
        }

        ArgumentNullException.ThrowIfNull(idempotencyKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(requestHash);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(reservationPeriod, TimeSpan.Zero);
        ArgumentNullException.ThrowIfNull(items);

        _items.AddRange(items);
        if (_items.Count is < 1 or > MaxItems || _items.Any(item => item is null))
        {
            throw new ArgumentException($"An order requires 1-{MaxItems} items.", nameof(items));
        }

        if (_items.Select(item => item.ProductId).Distinct().Count() != _items.Count)
        {
            throw new ArgumentException("Order products must be distinct.", nameof(items));
        }

        Id = Guid.NewGuid();
        UserId = userId;
        IdempotencyKey = idempotencyKey;
        RequestHash = requestHash;
        CreatedAt = createdAt;
        ExpiresAt = createdAt.Add(reservationPeriod);
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public OrderStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public IdempotencyKey IdempotencyKey { get; private set; } = null!;
    public string RequestHash { get; private set; } = null!;
    public bool InventoryReleased { get; private set; }
    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();
    public decimal Total => _items.Sum(item => item.UnitPrice * item.Quantity);

    public void MarkAsPaid()
    {
        RequirePending();
        Status = OrderStatus.Paid;
    }

    public void MarkAsFailed()
    {
        RequirePending();
        Status = OrderStatus.Failed;
    }

    public void Expire(DateTimeOffset now)
    {
        RequirePending();
        if (now < ExpiresAt)
        {
            throw new InvalidOperationException("Reservation has not expired.");
        }

        Status = OrderStatus.Expired;
    }

    public bool ReleaseInventory()
    {
        if (Status is not (OrderStatus.Failed or OrderStatus.Expired))
        {
            throw new InvalidOperationException("Only unsuccessful orders can release inventory.");
        }

        if (InventoryReleased)
        {
            return false;
        }

        InventoryReleased = true;
        return true;
    }

    private void RequirePending()
    {
        if (Status != OrderStatus.Pending)
        {
            throw new InvalidOperationException("Order is already terminal.");
        }
    }
}
