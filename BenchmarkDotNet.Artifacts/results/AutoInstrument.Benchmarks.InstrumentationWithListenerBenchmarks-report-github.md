```

BenchmarkDotNet v0.14.0, macOS 26.2 (25C56) [Darwin 25.2.0]
Apple M3, 1 CPU, 8 logical and 8 physical cores
.NET SDK 10.0.100
  [Host]   : .NET 10.0.0 (10.0.25.52411), Arm64 RyuJIT AdvSIMD
  ShortRun : .NET 10.0.0 (10.0.25.52411), Arm64 RyuJIT AdvSIMD

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                 | Mean        | Error      | StdDev    | Ratio  | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------------------- |------------:|-----------:|----------:|-------:|--------:|-------:|----------:|------------:|
| AsyncTask_Baseline     |   0.0000 ns |  0.0000 ns | 0.0000 ns |      ? |       ? |      - |         - |           ? |
| AsyncTask_Manual       |  96.3440 ns | 10.5952 ns | 0.5808 ns |      ? |       ? | 0.0497 |     416 B |           ? |
| AsyncTask_Generated    |  95.0481 ns |  5.5902 ns | 0.3064 ns |      ? |       ? | 0.0497 |     416 B |           ? |
|                        |             |            |           |        |         |        |           |             |
| AsyncTaskOfT_Baseline  |   3.1661 ns |  1.5460 ns | 0.0847 ns |   1.00 |    0.03 | 0.0086 |      72 B |        1.00 |
| AsyncTaskOfT_Manual    | 116.5577 ns |  9.7975 ns | 0.5370 ns |  36.83 |    0.86 | 0.0792 |     664 B |        9.22 |
| AsyncTaskOfT_Generated | 118.5156 ns | 24.8448 ns | 1.3618 ns |  37.45 |    0.94 | 0.0792 |     664 B |        9.22 |
|                        |             |            |           |        |         |        |           |             |
| SyncReturn_Baseline    |   0.0000 ns |  0.0000 ns | 0.0000 ns |      ? |       ? |      - |         - |           ? |
| SyncReturn_Manual      | 103.7254 ns | 13.1630 ns | 0.7215 ns |      ? |       ? | 0.0621 |     520 B |           ? |
| SyncReturn_Generated   | 104.3949 ns | 15.7906 ns | 0.8655 ns |      ? |       ? | 0.0621 |     520 B |           ? |
|                        |             |            |           |        |         |        |           |             |
| SyncVoid_Baseline      |   0.3288 ns |  0.1018 ns | 0.0056 ns |   1.00 |    0.02 |      - |         - |          NA |
| SyncVoid_Manual        |  91.1015 ns |  0.5756 ns | 0.0315 ns | 277.12 |    4.09 | 0.0497 |     416 B |          NA |
| SyncVoid_Generated     |  92.7520 ns | 29.7684 ns | 1.6317 ns | 282.14 |    5.99 | 0.0497 |     416 B |          NA |
