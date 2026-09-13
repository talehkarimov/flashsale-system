using FlashSale.Domain.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlashSale.Infrastructure.Persistence.Configurations;

internal sealed class InventoryConfiguration : IEntityTypeConfiguration<FlashSale.Domain.Inventory.Inventory>
{
    public void Configure(EntityTypeBuilder<FlashSale.Domain.Inventory.Inventory> builder)
    {
        builder.ToTable("Inventory", table => table.HasCheckConstraint("CK_Inventory_Available", "[AvailableQuantity] >= 0"));
        builder.HasKey(inventory => inventory.ProductId);
        builder.HasOne<Product>().WithOne().HasForeignKey<FlashSale.Domain.Inventory.Inventory>(inventory => inventory.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
