using System;

namespace AutoInstrument;

/// <summary>
/// Marks a method for automatic OpenTelemetry instrumentation.
/// At compile time, intercepts all call-sites of this method
/// and wraps each call in an Activity span - capturing parameters as tags
/// and recording exceptions automatically.
///
/// Inspired by Rust's <c>#[tracing::instrument]</c>.
/// </summary>
/// <example>
/// <code>
/// // Just add the attribute
/// [Instrument]
/// public async Task&lt;Order&gt; ProcessOrder(int orderId, string customerName)
/// {
///     // your logic here
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class InstrumentAttribute : Attribute
{
    /// <summary>
    /// Override the span name. Defaults to "ClassName.MethodName".
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Parameter names to skip capturing as span tags.
    /// Use for sensitive data like passwords, tokens, connection strings.
    /// </summary>
    public string[]? Skip { get; set; }

    /// <summary>
    /// If set, only these parameters will be captured as span tags.
    /// Takes precedence over <see cref="Skip"/>.
    /// </summary>
    public string[]? Fields { get; set; }

    /// <summary>
    /// The ActivitySource name. Defaults to the assembly name.
    /// </summary>
    public string? ActivitySourceName { get; set; }

    /// <summary>
    /// Whether to record the return value as a span tag.
    /// Default is false. Be careful with large return types.
    /// </summary>
    public bool RecordReturnValue { get; set; }

    /// <summary>
    /// Whether to record exceptions on the span. Default is true.
    /// </summary>
    public bool RecordException { get; set; } = true;

    /// <summary>
    /// The <see cref="System.Diagnostics.ActivityKind"/> for the span.
    /// Defaults to Internal (0).
    /// </summary>
    public int Kind { get; set; } = 0; // 0=Internal, 1=Server, 2=Client, 3=Producer, 4=Consumer

    /// <summary>
    /// Set the activity status to Ok when the method completes without exceptions.
    /// Default is false.
    /// </summary>
    public bool RecordSuccess { get; set; }

    /// <summary>
    /// Don't record <see cref="OperationCanceledException"/> as errors on the span.
    /// Default is true — cancellations are silently ignored.
    /// </summary>
    public bool IgnoreCancellation { get; set; } = true;

    /// <summary>
    /// Name of a boolean property or field on the containing type.
    /// When set, instrumentation is only active if the condition evaluates to true at runtime.
    /// </summary>
    public string? Condition { get; set; }

    /// <summary>
    /// Name of a parameter of type <c>ActivityContext</c> to link to the span as an <c>ActivityLink</c>.
    /// </summary>
    public string? LinkTo { get; set; }
}
