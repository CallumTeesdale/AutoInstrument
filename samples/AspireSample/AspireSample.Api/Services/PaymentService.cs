using AutoInstrument;
using AspireSample.Api.Models;

namespace AspireSample.Api.Services;

public class PaymentService
{
    [Tag(Name = "payment.provider")] public string Provider { get; set; } = "stripe";

    [Instrument(Fields = new[] { "request.OrderId", "request.Amount" }, RecordSuccess = true)]
    public async Task<bool> ChargeCard(PaymentRequest request, CancellationToken ct)
    {
        await Task.Delay(100, ct);
        if (request.Amount > 9999)
            throw new InvalidOperationException("Payment declined: amount too high");
        return true;
    }

    [Instrument(RecordReturnValue = true)]
    public async Task<string> RefundOrder(int orderId, decimal amount)
    {
        await Task.Delay(80);
        return $"REFUND-{orderId}-{amount:F2}";
    }
}
