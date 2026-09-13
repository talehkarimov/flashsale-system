using FlashSale.Application.Orders.CreateOrder;
using FlashSale.Domain.Orders;
using FlashSale.Domain.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlashSale.Infrastructure.Persistence.Configurations;

internal sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    private const int StatusLength = 16;
    private const string OrderIdColumn = "OrderId";

    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders", table =>
        {
            table.HasCheckConstraint("CK_Orders_Status", "[Status] IN ('Pending','Paid','Failed','Expired')");
            table.HasCheckConstraint("CK_Orders_Release",
                "([Status] IN ('Pending','Paid') AND [InventoryReleased] = 0) OR ([Status] IN ('Failed','Expired') AND [InventoryReleased] = 1)");
            table.HasCheckConstraint("CK_Orders_Expiry", "[ExpiresAt] > [CreatedAt]");
        });

        builder.HasKey(order => order.Id);
        builder.Property(order => order.Status).HasConversion<string>().HasMaxLength(StatusLength);
        builder.Property(order => order.IdempotencyKey)
            .HasConversion(key => key.Value, value => new IdempotencyKey(value))
            .HasMaxLength(IdempotencyKey.MaxLength)
            .UseCollation("Latin1_General_100_BIN2")
            .IsRequired();
        builder.Property(order => order.RequestHash).HasMaxLength(OrderRequestFingerprint.Length).IsFixedLength().IsRequired();
        builder.HasIndex(order => new { order.UserId, order.IdempotencyKey }).IsUnique();
        builder.Ignore(order => order.Total);

        builder.OwnsMany(order => order.Items, item =>
        {
            item.ToTable("OrderItems", table =>
            {
                table.HasCheckConstraint("CK_OrderItems_Quantity", $"[Quantity] > 0 AND [Quantity] <= {OrderItem.MaxQuantity}");
                table.HasCheckConstraint("CK_OrderItems_Price", "[UnitPrice] > 0");
            });
            item.WithOwner().HasForeignKey(OrderIdColumn);
            item.HasKey(OrderIdColumn, nameof(OrderItem.ProductId));
            item.Property(value => value.UnitPrice).HasPrecision(PriceRules.Precision, PriceRules.Scale);
            item.HasOne<Product>().WithMany().HasForeignKey(value => value.ProductId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Navigation(order => order.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
