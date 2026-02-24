using AutoInstrument;
using AspireSample.Api.Models;

namespace AspireSample.Api.Services;

public class ShippingService
{
    [Tag] public string Warehouse { get; set; } = "SEA-01";
    public bool DetailedLogging { get; set; } = true;

    [Instrument(RecordSuccess = true, RecordReturnValue = true)]
    public async Task<string> Ship(ShippingRequest request, CancellationToken ct)
    {
        await Task.Delay(150, ct);
        return $"TRK-{request.OrderId:D6}";
    }

    [Instrument(Condition = nameof(DetailedLogging))]
    public void CalculateRoute(string origin, string destination)
    {
        Thread.Sleep(10);
    }
}
