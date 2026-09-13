using Microsoft.EntityFrameworkCore;
using PaymentSimulator.Api.Payments;

namespace PaymentSimulator.Api.Persistence;

public sealed class SqlPaymentLedger(PaymentDbContext db, TimeProvider clock)
{
    public async Task<RecordedPayment> GetOrRecordOutcomeAsync(
        PaymentRequest request,
        PaymentOutcome intendedOutcome,
        CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // The key-range lock also protects an absent operation from concurrent pay/close inserts.
        var operation = await db.Operations.FromSqlInterpolated($"""
            SELECT * FROM PaymentOperations WITH (UPDLOCK, HOLDLOCK) WHERE OperationId = {request.OperationId}
            """).SingleOrDefaultAsync(cancellationToken);

        var created = operation is null;
        if (operation is null)
        {
            var outcome = clock.GetUtcNow() >= request.ExpiresAt ? PaymentOutcome.Cancelled : intendedOutcome;
            operation = new PaymentOperation(request, outcome);
            db.Operations.Add(operation);
        }

        operation.RecordRequest(request);
        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new RecordedPayment(operation.Outcome, created);
    }
}
