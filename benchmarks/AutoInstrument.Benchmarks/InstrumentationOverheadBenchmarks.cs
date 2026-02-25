using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

namespace AutoInstrument.Benchmarks;

[MemoryDiagnoser]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class InstrumentationOverheadBenchmarks
{
    private TargetService _service = null!;

    [GlobalSetup]
    public void Setup()
    {
        _service = new TargetService();
    }
    
    [BenchmarkCategory("SyncVoid"), Benchmark(Baseline = true)]
    public void SyncVoid_Baseline() => _service.SyncVoid_Baseline();

    [BenchmarkCategory("SyncVoid"), Benchmark]
    public void SyncVoid_Instrumented() => _service.SyncVoid_Instrumented();
    
    [BenchmarkCategory("SyncReturn"), Benchmark(Baseline = true)]
    public int SyncReturn_Baseline() => _service.SyncReturn_Baseline(42);

    [BenchmarkCategory("SyncReturn"), Benchmark]
    public int SyncReturn_Instrumented() => _service.SyncReturn_Instrumented(42);
    
    [BenchmarkCategory("AsyncTask"), Benchmark(Baseline = true)]
    public Task AsyncTask_Baseline() => _service.AsyncTask_Baseline();

    [BenchmarkCategory("AsyncTask"), Benchmark]
    public Task AsyncTask_Instrumented() => _service.AsyncTask_Instrumented();
    
    [BenchmarkCategory("AsyncTaskOfT"), Benchmark(Baseline = true)]
    public Task<int> AsyncTaskOfT_Baseline() => _service.AsyncTaskOfT_Baseline(42);

    [BenchmarkCategory("AsyncTaskOfT"), Benchmark]
    public Task<int> AsyncTaskOfT_Instrumented() => _service.AsyncTaskOfT_Instrumented(42);
}
