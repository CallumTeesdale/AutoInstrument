using System.Diagnostics;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace AutoInstrument.Benchmarks;

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class InstrumentationWithListenerBenchmarks
{
    private TargetService _service = null!;
    private ActivityListener _listener = null!;

    [GlobalSetup]
    public void Setup()
    {
        _service = new TargetService();

        _listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded
        };
        ActivitySource.AddActivityListener(_listener);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _listener.Dispose();
    }


    [BenchmarkCategory("SyncVoid"), Benchmark(Baseline = true)]
    public void SyncVoid_Baseline() => _service.SyncVoid_Baseline();

    [BenchmarkCategory("SyncVoid"), Benchmark]
    public void SyncVoid_Manual() => ManualInstrumentation.SyncVoid(_service);

    [BenchmarkCategory("SyncVoid"), Benchmark]
    public void SyncVoid_Generated() => _service.SyncVoid_Instrumented();
    
    [BenchmarkCategory("SyncReturn"), Benchmark(Baseline = true)]
    public int SyncReturn_Baseline() => _service.SyncReturn_Baseline(42);

    [BenchmarkCategory("SyncReturn"), Benchmark]
    public int SyncReturn_Manual() => ManualInstrumentation.SyncReturn(_service, 42);

    [BenchmarkCategory("SyncReturn"), Benchmark]
    public int SyncReturn_Generated() => _service.SyncReturn_Instrumented(42);
    
    [BenchmarkCategory("AsyncTask"), Benchmark(Baseline = true)]
    public Task AsyncTask_Baseline() => _service.AsyncTask_Baseline();

    [BenchmarkCategory("AsyncTask"), Benchmark]
    public Task AsyncTask_Manual() => ManualInstrumentation.AsyncTask(_service);

    [BenchmarkCategory("AsyncTask"), Benchmark]
    public Task AsyncTask_Generated() => _service.AsyncTask_Instrumented();
    
    [BenchmarkCategory("AsyncTaskOfT"), Benchmark(Baseline = true)]
    public Task<int> AsyncTaskOfT_Baseline() => _service.AsyncTaskOfT_Baseline(42);

    [BenchmarkCategory("AsyncTaskOfT"), Benchmark]
    public Task<int> AsyncTaskOfT_Manual() => ManualInstrumentation.AsyncTaskOfT(_service, 42);

    [BenchmarkCategory("AsyncTaskOfT"), Benchmark]
    public Task<int> AsyncTaskOfT_Generated() => _service.AsyncTaskOfT_Instrumented(42);
}
