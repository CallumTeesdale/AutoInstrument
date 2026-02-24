using System.Diagnostics;
using AutoInstrument;
using AspireSample.Api.Models;

namespace AspireSample.Api.Services;

public class OrderService
{
    [Tag] public string Region { get; set; } = "us-west-2";
    [Tag(Name = "deployment.environment")] public string Environment { get; set; } = "staging";
    public bool VerboseTracing { get; set; } = true;

    private readonly PaymentService _payments;
    private readonly ShippingService _shipping;
    private readonly AuditService _audit;

    public OrderService(PaymentService payments, ShippingService shipping, AuditService audit)
    {
        _payments = payments;
        _shipping = shipping;
        _audit = audit;
    }

    [Instrument(Skip = new[] { "order.CreditCard" })]
    public async Task<OrderResult> PlaceOrder(Order order, CancellationToken ct)
    {
        await _audit.RecordAction(new AuditEntry("PlaceOrder", order.Customer, DateTimeOffset.UtcNow));

        var paymentOk = await _payments.ChargeCard(
            new PaymentRequest(order.Id, order.CreditCard, "***", order.Total), ct);

        if (!paymentOk)
            throw new InvalidOperationException($"Payment failed for order {order.Id}");

        var tracking = await _shipping.Ship(
            new ShippingRequest(order.Id, "123 Main St", "Seattle", "US", 2.5), ct);

        return new OrderResult(order.Id, "Confirmed", tracking);
    }

    [Instrument(Fields = new[] { "orderId" }, RecordReturnValue = true)]
    public async Task<string> GetOrderStatus(int orderId, string internalToken)
    {
        await Task.Delay(15);
        return orderId % 2 == 0 ? "Shipped" : "Processing";
    }

    [Instrument(RecordSuccess = true, IgnoreCancellation = true)]
    public async Task<OrderResult> FulfillOrder(int orderId, CancellationToken ct)
    {
        await Task.Delay(200, ct);
        return new OrderResult(orderId, "Fulfilled", $"TRK-{orderId:D6}");
    }

    [Instrument(IgnoreCancellation = false, RecordSuccess = true)]
    public async Task CriticalSync(int orderId, CancellationToken ct)
    {
        await Task.Delay(500, ct);
    }

    [Instrument(Condition = nameof(VerboseTracing))]
    public void LogOrderMetrics(int orderId, int itemCount, decimal subtotal)
    {
        Thread.Sleep(5);
    }

    [Instrument]
    public async Task<bool> AuthenticateCustomer(
        string email, [NoInstrument] string password, string mfaCode)
    {
        await Task.Delay(50);
        return email.Contains("@") && mfaCode.Length == 6;
    }

    [Instrument]
    public async Task<bool> ValidatePayment(
        [NoInstrument("CardNumber", "Cvv")] PaymentRequest request)
    {
        await Task.Delay(30);
        return request.Amount > 0 && request.Amount < 10_000;
    }

    [Instrument(LinkTo = "parentContext")]
    public async Task ProcessLinkedOrder(int orderId, ActivityContext parentContext)
    {
        await Task.Delay(100);
    }

    [Instrument(Name = "orders.external_validation", Kind = 2)]
    public async Task<bool> CallExternalValidator(int orderId, string payload)
    {
        await Task.Delay(75);
        return true;
    }

    [Instrument(RecordException = false)]
    public void QuickCheck(int orderId)
    {
        if (orderId < 0)
            throw new ArgumentOutOfRangeException(nameof(orderId));
    }

    [Instrument(Name = "orders.compute_tax")]
    public static decimal ComputeTax(decimal subtotal, string region)
    {
        return region switch
        {
            "us-west-2" => subtotal * 0.10m,
            "eu-west-1" => subtotal * 0.20m,
            _ => subtotal * 0.05m,
        };
    }
}
