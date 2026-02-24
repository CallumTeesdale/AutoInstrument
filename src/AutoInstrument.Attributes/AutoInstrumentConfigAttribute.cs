using System;

namespace AutoInstrument;

/// <summary>
/// Assembly-level configuration for AutoInstrument.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class AutoInstrumentConfigAttribute : Attribute
{
    /// <summary>Tag naming convention. Default: <see cref="TagNamingConvention.Method"/>.</summary>
    public TagNamingConvention TagNaming { get; set; } = TagNamingConvention.Method;
}
