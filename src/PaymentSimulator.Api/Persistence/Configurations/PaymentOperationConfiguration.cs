using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PaymentSimulator.Api.Payments;

namespace PaymentSimulator.Api.Persistence.Configurations;

internal sealed class PaymentOperationConfiguration : IEntityTypeConfiguration<PaymentOperation>
{
    private const int OutcomeLength = 16;

    public void Configure(EntityTypeBuilder<PaymentOperation> builder)
    {
        builder.ToTable("PaymentOperations", table =>
        {
            table.HasCheckConstraint("CK_PaymentOperations_Amount", "[Amount] > 0");
            table.HasCheckConstraint("CK_PaymentOperations_Outcome", "[Outcome] IN ('Paid','Rejected','Cancelled')");
            table.HasCheckConstraint("CK_PaymentOperations_Requests", "[Requests] > 0");
        });
        builder.HasKey(operation => operation.OperationId);
        builder.Property(operation => operation.Amount).HasPrecision(PaymentOperation.AmountPrecision, PaymentOperation.AmountScale);
        builder.Property(operation => operation.Outcome).HasConversion<string>().HasMaxLength(OutcomeLength).IsRequired();
    }
}
