# AutoInstrument

`[Instrument]` for .NET - add an attribute, get OpenTelemetry spans with tagged parameters at every call-site. Inspired by Rust's `#[instrument]`.

```csharp
[Instrument]
public async Task<string> ShaveYak(int yakId, string style)
{
    await Task.Delay(50);
    return $"Yak #{yakId} shaved with {style}";
}
```

Under the hood, the source generator emits C# interceptors that wrap each call-site with an `Activity`, tag parameters, and record exceptions. Your method is never modified.

## Setup

Add the package and enable interceptors:

```xml
<PropertyGroup>
  <InterceptorsNamespaces>$(InterceptorsNamespaces);AutoInstrument.Generated</InterceptorsNamespaces>
</PropertyGroup>
```

```csharp
using AutoInstrument;

[Instrument]
public async Task<Order> ProcessOrder(int orderId, string customer) { ... }

[Instrument(Skip = new[] { "creditCard" })]
public async Task ChargeCustomer(int orderId, string creditCard) { ... }

[Instrument(Name = "orders.compute_total", RecordReturnValue = true)]
public decimal ComputeTotal(List<LineItem> items) => items.Sum(i => i.Price * i.Quantity);
```

## Options

| Property | Default | Description                                                           |
|---|---|-----------------------------------------------------------------------|
| `Name` | `Class.Method` | Span name                                                             |
| `Skip` | — | Parameters/properties to exclude from tags                            |
| `Fields` | — | Whitelist -D only these become tags                                   |
| `ActivitySourceName` | Assembly name | Custom ActivitySource name                                            |
| `RecordReturnValue` | `false` | Tag the return value                                                  |
| `RecordException` | `true` | Record exceptions on the span                                         |
| `Kind` | `0` (Internal) | ActivityKind (0=Internal, 1=Server, 2=Client, 3=Producer, 4=Consumer) |
| `RecordSuccess` | `false` | Set status `Ok` on success                                            |
| `IgnoreCancellation` | `true` | Don't record `OperationCanceledException` as errors                   |
| `Condition` | — | Boolean member name - skip instrumentation when `false`               |
| `LinkTo` | — | Parameter name (must be `ActivityContext`) to attach as ActivityLink  |
| `Depth` | Global default (1) | Max depth for expanding complex type properties                       |

`Skip` and `Fields` support dot-notation at any depth: `Skip = new[] { "order.Address.City" }`.

## Parameter attributes

`[NoInstrument]` on a parameter skips it from tagging. Pass property names to skip specific properties of a complex type:

```csharp
public void Login(string user, [NoInstrument] string password) { ... }

public void Process([NoInstrument("CreditCard", "SSN")] Order order) { ... }
```

## Member tags

`[Tag]` on a field or property includes it as a span tag on all `[Instrument]` methods in the class:

```csharp
public class OrderService
{
    [Tag] public string Region { get; set; }
    [Tag(Name = "env")] public string Environment { get; set; }

    [Instrument]
    public void Process(int orderId) { ... }
    // Tags: process.orderid, region, env
}
```

Complex types are expanded recursively, just like method parameters. Use `Skip`, `Fields`, or `Depth` to control expansion:

```csharp
public class OrderService
{
    [Tag(Skip = new[] { "Secret" })] public Config Settings { get; set; }
    [Tag(Fields = new[] { "Region" })] public Config Backup { get; set; }
    [Tag(Depth = 1)] public Config Shallow { get; set; }
    // Settings → settings.region, settings.timeout (Secret excluded)
    // Backup   → backup.region (only Region included)
    // Shallow  → one level only
}
```

## Complex types

Classes and structs are expanded one level deep by default. Dot-notation in `Skip` or `Fields` automatically resolves deeper — no need to increase `Depth` manually:

```csharp
[Instrument]
public void Process(Order order) { ... }
// Tags: process.order.id, process.order.address (1 level deep)

[Instrument(Fields = new[] { "order.Address.City" })]
public void Targeted(Order order) { ... }
// Tags: process.order.address.city (auto-resolved to reach City)

[Instrument(Depth = 3)]
public void Deep(Order order) { ... }
// Tags: process.order.id, process.order.address.city, process.order.address.zip (fully expanded)
```

Circular references are detected and stopped automatically. Collections (arrays, `List<T>`, etc.) are not expanded — they are tagged as-is.

## Configuration

The ActivitySource name defaults to the assembly name. Override at three levels (highest priority wins):

1. Per-method: `[Instrument(ActivitySourceName = "X")]`
2. MSBuild: `<AutoInstrumentSourceName>X</AutoInstrumentSourceName>`
3. Assembly attribute: `[assembly: AutoInstrumentSource("X")]`

Tag naming defaults to `methodname.param`. Set it to `Flat` for just `param`:

```csharp
[assembly: AutoInstrumentConfig(TagNaming = TagNamingConvention.Flat)]
```

Or via MSBuild: `<AutoInstrumentTagNaming>Flat</AutoInstrumentTagNaming>`

The default expansion depth is 1. Dot-notation in `Skip`/`Fields` auto-resolves deeper. Override globally:

```csharp
[assembly: AutoInstrumentConfig(Depth = 2)]
```

Or via MSBuild: `<AutoInstrumentDepth>2</AutoInstrumentDepth>`

## Diagnostics

| ID | Description |
|---|---|
| AUTOINST001 | Skip references unknown parameter |
| AUTOINST002 | Fields references unknown parameter |
| AUTOINST003 | Dot-notation references unknown property |
| AUTOINST004 | Condition is not a boolean member |
| AUTOINST005 | LinkTo is not an ActivityContext parameter |
| AUTOINST006 | `[NoInstrument]` references unknown property |
| AUTOINST007 | `[Tag]` Skip references unknown property |
| AUTOINST008 | `[Tag]` Fields references unknown property |

## Limitations

- Interceptors only work within the same compilation — cross-project calls aren't intercepted.
- Recursive calls create nested spans.
- Complex types expand up to the configured depth (default 1). Dot-notation auto-resolves deeper. Collections are not expanded.

## Requirements

.NET 10

## License

MIT
