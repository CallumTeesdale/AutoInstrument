using AutoInstrument;

namespace AutoInstrument.Benchmarks;

public class TargetService
{
    public void SyncVoid_Baseline()
    {
    }

    [Instrument]
    public void SyncVoid_Instrumented()
    {
    }
    
    public int SyncReturn_Baseline(int x)
    {
        return x + 1;
    }

    [Instrument]
    public int SyncReturn_Instrumented(int x)
    {
        return x + 1;
    }
    
    public Task AsyncTask_Baseline()
    {
        return Task.CompletedTask;
    }

    [Instrument]
    public Task AsyncTask_Instrumented()
    {
        return Task.CompletedTask;
    }
    
    public Task<int> AsyncTaskOfT_Baseline(int x)
    {
        return Task.FromResult(x + 1);
    }

    [Instrument]
    public Task<int> AsyncTaskOfT_Instrumented(int x)
    {
        return Task.FromResult(x + 1);
    }
}
