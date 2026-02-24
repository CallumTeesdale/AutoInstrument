using AutoInstrument;
using AspireSample.Api.Models;

namespace AspireSample.Api.Services;

public class AuditService
{
    [Instrument(Name = "audit.record")]
    public async Task RecordAction(AuditEntry entry)
    {
        await Task.Delay(10);
    }

    [Instrument(RecordException = false, RecordReturnValue = true)]
    public string GetLastAction(string userId)
    {
        return $"PlaceOrder by {userId} at {DateTimeOffset.UtcNow:HH:mm:ss}";
    }
}
