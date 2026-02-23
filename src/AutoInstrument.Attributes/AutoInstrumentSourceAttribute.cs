using System;

namespace AutoInstrument;

/// <summary>
/// Sets the default ActivitySource name for all <c>[Instrument]</c> methods in this assembly.
/// Can be overridden per-method via <see cref="InstrumentAttribute.ActivitySourceName"/>.
/// </summary>
/// <example>
/// <code>
/// [assembly: AutoInstrumentSource("MyApp")]
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class AutoInstrumentSourceAttribute : Attribute
{
    public AutoInstrumentSourceAttribute(string name) => Name = name;

    /// <summary>
    /// The default ActivitySource name for all instrumented methods in this assembly.
    /// </summary>
    public string Name { get; }
}
