using AutoInstrument;

namespace SampleApp.Services;

public class YakShavingService
{
    [Instrument]
    public async Task<string> ShaveYak(int yakId, string style)
    {
        await Task.Delay(50);

        if (yakId == 13)
            throw new InvalidOperationException("Yak #13 is unshaveable!");

        return $"Yak #{yakId} shaved with {style} style";
    }

    [Instrument(RecordReturnValue = true)]
    public string GetYakInfo(int yakId)
    {
        return $"Yak #{yakId}: healthy, woolly, needs shaving";
    }

    [Instrument(Skip = new[] { "password", "token" })]
    public async Task Authenticate(int userId, string password, string token)
    {
        await Task.Delay(30);
    }

    [Instrument]
    public async Task<string> ShaveNamedYak(YakInfo yakInfo)
    {
        await Task.Delay(50);
        await ShaveYak(yakInfo.id, "wooly");
        return $"Shaved {yakInfo.name}";
    }

    public record YakInfo(int id, string name);
}