using FlashSale.Domain.Orders;
using FlashSale.Domain.Products;

namespace FlashSale.UnitTests.Domain.Orders;

public sealed class OrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static Order Create() => new(Guid.NewGuid(), new IdempotencyKey("key"), "hash",
        [new OrderItem(Guid.NewGuid(), 2, 12.50m)], Now, TimeSpan.FromMinutes(2));

    [Fact]
    public void New_order_is_pending_with_snapshot_total()
    {
        var order = Create();
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(25m, order.Total);
        Assert.False(order.InventoryReleased);
        Assert.Equal(Now.AddMinutes(2), order.ExpiresAt);
    }

    [Theory]
    [InlineData(OrderStatus.Paid)]
    [InlineData(OrderStatus.Failed)]
    [InlineData(OrderStatus.Expired)]
    public void Terminal_orders_reject_all_further_transitions(OrderStatus status)
    {
        var order = Create();
        switch (status)
        {
            case OrderStatus.Paid: order.MarkAsPaid(); break;
            case OrderStatus.Failed: order.MarkAsFailed(); break;
            case OrderStatus.Expired: order.Expire(Now.AddMinutes(2)); break;
        }
        Assert.Equal(status, order.Status);
        Assert.Throws<InvalidOperationException>(order.MarkAsPaid);
        Assert.Throws<InvalidOperationException>(order.MarkAsFailed);
        Assert.Throws<InvalidOperationException>(() => order.Expire(Now.AddMinutes(3)));
    }

    [Fact]
    public void Reservation_cannot_expire_early() =>
        Assert.Throws<InvalidOperationException>(() => Create().Expire(Now));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_and_expired_orders_release_once(bool expired)
    {
        var order = Create();
        if (expired) order.Expire(Now.AddMinutes(2)); else order.MarkAsFailed();
        Assert.True(order.ReleaseInventory());
        Assert.False(order.ReleaseInventory());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Pending_and_paid_orders_cannot_release_inventory(bool paid)
    {
        var order = Create();
        if (paid) order.MarkAsPaid();
        Assert.Throws<InvalidOperationException>(() => order.ReleaseInventory());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10001)]
    public void Invalid_quantities_are_rejected(int quantity) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new OrderItem(Guid.NewGuid(), quantity, 1m));

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("0.001")]
    [InlineData("1000000000")]
    public void Invalid_prices_are_rejected(string value)
    {
        var price = decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Throws<ArgumentOutOfRangeException>(() => new Product(Guid.NewGuid(), "Product", price));
        Assert.Throws<ArgumentOutOfRangeException>(() => new OrderItem(Guid.NewGuid(), 1, price));
    }

    [Fact]
    public void Empty_and_duplicate_items_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new Order(Guid.NewGuid(), new IdempotencyKey("key"), "hash", [], Now, TimeSpan.FromMinutes(1)));
        var item = new OrderItem(Guid.NewGuid(), 1, 1m);
        Assert.Throws<ArgumentException>(() => new Order(Guid.NewGuid(), new IdempotencyKey("key"), "hash", [item, item], Now, TimeSpan.FromMinutes(1)));
    }

    [Fact]
    public void Invalid_identity_and_reservation_are_rejected()
    {
        var items = new[] { new OrderItem(Guid.NewGuid(), 1, 1m) };
        Assert.Throws<ArgumentException>(() => new Order(Guid.Empty, new IdempotencyKey("key"), "hash", items, Now, TimeSpan.FromMinutes(1)));
        Assert.Throws<ArgumentException>(() => new IdempotencyKey(""));
        Assert.Throws<ArgumentOutOfRangeException>(() => new Order(Guid.NewGuid(), new IdempotencyKey("key"), "hash", items, Now, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FlashSale.Domain.Inventory.Inventory(Guid.NewGuid(), -1));
    }
}
