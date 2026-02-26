window.BENCHMARK_DATA = {
  "lastUpdate": 1772144081140,
  "repoUrl": "https://github.com/CallumTeesdale/AutoInstrument",
  "entries": {
    "AutoInstrument Benchmarks": [
      {
        "commit": {
          "author": {
            "email": "callumjamesteesdale@gmail.com",
            "name": "Callum Teesdale",
            "username": "CallumTeesdale"
          },
          "committer": {
            "email": "callumjamesteesdale@gmail.com",
            "name": "Callum Teesdale",
            "username": "CallumTeesdale"
          },
          "distinct": true,
          "id": "19b01a27cc1c5698d88a6edb87fbe86ce3ea60d5",
          "message": "chore(ci): remove step to create gh-pages branch from workflow",
          "timestamp": "2026-02-26T22:04:12Z",
          "tree_id": "5ec81c594da78a595ed383caf089ce14569750cd",
          "url": "https://github.com/CallumTeesdale/AutoInstrument/commit/19b01a27cc1c5698d88a6edb87fbe86ce3ea60d5"
        },
        "date": 1772144080175,
        "tool": "benchmarkdotnet",
        "benches": [
          {
            "name": "AutoInstrument.Benchmarks.InstrumentationOverheadBenchmarks.AsyncTask_Baseline",
            "value": 0.3121701292693615,
            "unit": "ns",
            "range": "± 0.001981776868088575"
          },
          {
            "name": "AutoInstrument.Benchmarks.InstrumentationOverheadBenchmarks.AsyncTask_Instrumented",
            "value": 8.875722042643106,
            "unit": "ns",
            "range": "± 0.014279378070087903"
          },
          {
            "name": "AutoInstrument.Benchmarks.InstrumentationOverheadBenchmarks.AsyncTaskOfT_Baseline",
            "value": 6.955600079562929,
            "unit": "ns",
            "range": "± 0.23430181439148848"
          },
          {
            "name": "AutoInstrument.Benchmarks.InstrumentationOverheadBenchmarks.AsyncTaskOfT_Instrumented",
            "value": 22.47662158962339,
            "unit": "ns",
            "range": "± 0.740133770321086"
          },
          {
            "name": "AutoInstrument.Benchmarks.InstrumentationOverheadBenchmarks.SyncReturn_Baseline",
            "value": 0.0006644730097972429,
            "unit": "ns",
            "range": "± 0.00047925884958430664"
          },
          {
            "name": "AutoInstrument.Benchmarks.InstrumentationOverheadBenchmarks.SyncReturn_Instrumented",
            "value": 1.2086919762194157,
            "unit": "ns",
            "range": "± 0.0034458140725774694"
          },
          {
            "name": "AutoInstrument.Benchmarks.InstrumentationOverheadBenchmarks.SyncVoid_Baseline",
            "value": 0.0009071362706331107,
            "unit": "ns",
            "range": "± 0.0008211543820596677"
          },
          {
            "name": "AutoInstrument.Benchmarks.InstrumentationOverheadBenchmarks.SyncVoid_Instrumented",
            "value": 1.1778499443943684,
            "unit": "ns",
            "range": "± 0.00948365376927015"
          }
        ]
      }
    ]
  }
}