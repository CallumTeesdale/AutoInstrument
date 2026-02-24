using System;

namespace AutoInstrument;

/// <summary>
/// Marks a method for automatic OpenTelemetry instrumentation.
/// Intercepts all call-sites at compile time, wrapping each in an Activity span.
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class InstrumentAttribute : Attribute
{
    /// <summary>Override the span name. Defaults to "ClassName.MethodName".</summary>
    public string? Name { get; set; }

    /// <summary>Parameter names to exclude from span tags. Supports dot-notation.</summary>
    public string[]? Skip { get; set; }

    /// <summary>Whitelist of parameters to capture as span tags. Takes precedence over <see cref="Skip"/>.</summary>
    public string[]? Fields { get; set; }

    /// <summary>Override the ActivitySource name. Defaults to the assembly name.</summary>
    public string? ActivitySourceName { get; set; }

    /// <summary>Record the return value as a span tag. Default: false.</summary>
    public bool RecordReturnValue { get; set; }

    /// <summary>Record exceptions on the span. Default: true.</summary>
    public bool RecordException { get; set; } = true;

    /// <summary>The ActivityKind for the span. 0=Internal, 1=Server, 2=Client, 3=Producer, 4=Consumer.</summary>
    public int Kind { get; set; }

    /// <summary>Set activity status to Ok on success. Default: false.</summary>
    public bool RecordSuccess { get; set; }

    /// <summary>Ignore <see cref="OperationCanceledException"/> (don't mark as error). Default: true.</summary>
    public bool IgnoreCancellation { get; set; } = true;

    /// <summary>Boolean property/field name on the containing type for conditional instrumentation.</summary>
    public string? Condition { get; set; }

    /// <summary>Parameter name of type ActivityContext to add as an ActivityLink.</summary>
    public string? LinkTo { get; set; }
}
