using System;

namespace AutoInstrument;

/// <summary>
/// Assembly-level attribute to configure AutoInstrument behavior.
/// </summary>
/// <example>
/// <code>
/// [assembly: AutoInstrumentConfig(TagNaming = TagNamingConvention.Flat)]
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class AutoInstrumentConfigAttribute : Attribute
{
    /// <summary>
    /// The tag naming convention for all instrumented methods. Default: <see cref="TagNamingConvention.Method"/>.
    /// </summary>
    public TagNamingConvention TagNaming { get; set; } = TagNamingConvention.Method;
}
