using FlashSale.Domain.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FlashSale.Infrastructure.Outbox.Persistence;

internal sealed class OrderPaymentOutboxMessageConfiguration : IEntityTypeConfiguration<OrderPaymentOutboxMessage>
{
    public void Configure(EntityTypeBuilder<OrderPaymentOutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages", table =>
        {
            table.HasCheckConstraint("CK_OutboxMessages_Attempts", "[Attempts] >= 0");
            table.HasCheckConstraint("CK_OutboxMessages_Lease",
                "([LeaseToken] IS NULL AND [LeaseUntil] IS NULL) OR ([LeaseToken] IS NOT NULL AND [LeaseUntil] IS NOT NULL)");
        });
        builder.HasKey(message => message.Id);
        builder.HasOne<Order>().WithOne().HasForeignKey<OrderPaymentOutboxMessage>(message => message.OrderId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(message => message.NextAttemptAt).HasDefaultValueSql("SYSDATETIMEOFFSET()");
        builder.HasIndex(message => new { message.NextAttemptAt, message.Id })
            .IncludeProperties(message => new { message.LeaseUntil, message.LeaseToken, message.OrderId, message.Attempts })
            .HasFilter("[ProcessedAt] IS NULL");
    }
}
