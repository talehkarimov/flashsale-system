using Microsoft.Extensions.Options;
using PaymentSimulator.Api.Configuration;
using PaymentSimulator.Api.Persistence;

namespace PaymentSimulator.Api.Payments;

public sealed class PaymentProcessor(SqlPaymentLedger ledger, IOptions<SimulatorOptions> options, TimeProvider clock)
{
    public async Task<PaymentResponse> ExecutePaymentAsync(PaymentRequest request, CancellationToken cancellationToken)
    {
        PaymentRequestValidator.Validate(request);
        var scenario = options.Value.Scenario;
        if (scenario == SimulatorScenario.Unavailable)
        {
            throw new PaymentException(PaymentError.Unavailable, "Payment provider is temporarily unavailable.");
        }

        var outcome = scenario == SimulatorScenario.Reject ? PaymentOutcome.Rejected : PaymentOutcome.Paid;
        var payment = await ledger.GetOrRecordOutcomeAsync(request, outcome, cancellationToken);
        if (payment.Created && scenario == SimulatorScenario.ChargeThenDelay)
        {
            await Task.Delay(options.Value.Delay, clock, cancellationToken);
        }

        return new PaymentResponse(payment.Outcome);
    }

    public async Task<PaymentResponse> ClosePaymentAsync(PaymentRequest request, CancellationToken cancellationToken)
    {
        PaymentRequestValidator.Validate(request);
        var payment = await ledger.GetOrRecordOutcomeAsync(request, PaymentOutcome.Cancelled, cancellationToken);
        return new PaymentResponse(payment.Outcome);
    }
}
