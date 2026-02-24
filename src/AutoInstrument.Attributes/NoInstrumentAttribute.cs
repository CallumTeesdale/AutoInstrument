using System;

namespace AutoInstrument;

/// <summary>
/// Skips a parameter from span tags, or specific properties via constructor args.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class NoInstrumentAttribute : Attribute
{
    /// <summary>Property names to skip. If empty, the entire parameter is skipped.</summary>
    public string[] Properties { get; }

    public NoInstrumentAttribute(params string[] properties)
    {
        Properties = properties ?? Array.Empty<string>();
    }
}
