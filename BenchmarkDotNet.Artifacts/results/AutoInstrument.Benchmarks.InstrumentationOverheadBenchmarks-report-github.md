```

BenchmarkDotNet v0.14.0, macOS 26.2 (25C56) [Darwin 25.2.0]
Apple M3, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.100
  [Host]   : .NET 10.0.0 (10.0.25.52411), Arm64 RyuJIT AdvSIMD
  ShortRun : .NET 10.0.0 (10.0.25.52411), Arm64 RyuJIT AdvSIMD

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                    | Mean       | Error     | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|-------------------------- |-----------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| AsyncTask_Baseline        |  0.0000 ns | 0.0000 ns | 0.0000 ns |     ? |       ? |      - |         - |           ? |
| AsyncTask_Instrumented    |  4.2182 ns | 0.0266 ns | 0.0015 ns |     ? |       ? |      - |         - |           ? |
|                           |            |           |           |       |         |        |           |             |
| AsyncTaskOfT_Baseline     |  3.1152 ns | 0.2479 ns | 0.0136 ns |  1.00 |    0.01 | 0.0086 |      72 B |        1.00 |
| AsyncTaskOfT_Instrumented | 10.9167 ns | 0.2948 ns | 0.0162 ns |  3.50 |    0.01 | 0.0172 |     144 B |        2.00 |
|                           |            |           |           |       |         |        |           |             |
| SyncReturn_Baseline       |  0.0000 ns | 0.0000 ns | 0.0000 ns |     ? |       ? |      - |         - |           ? |
| SyncReturn_Instrumented   |  0.7683 ns | 0.2962 ns | 0.0162 ns |     ? |       ? |      - |         - |           ? |
|                           |            |           |           |       |         |        |           |             |
| SyncVoid_Baseline         |  0.2881 ns | 0.1297 ns | 0.0071 ns |  1.00 |    0.03 |      - |         - |          NA |
| SyncVoid_Instrumented     |  0.9661 ns | 0.2064 ns | 0.0113 ns |  3.35 |    0.08 |      - |         - |          NA |
