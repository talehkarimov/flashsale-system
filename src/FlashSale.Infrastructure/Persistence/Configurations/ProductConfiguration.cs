using FlashSale.Domain.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlashSale.Infrastructure.Persistence.Configurations;

internal sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products", table => table.HasCheckConstraint("CK_Products_Price", "[Price] > 0"));
        builder.HasKey(product => product.Id);
        builder.Property(product => product.Name).HasMaxLength(Product.MaxNameLength).IsRequired();
        builder.Property(product => product.Price).HasPrecision(PriceRules.Precision, PriceRules.Scale);
    }
}
