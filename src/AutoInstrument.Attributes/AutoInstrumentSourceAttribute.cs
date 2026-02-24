using System;

namespace AutoInstrument;

/// <summary>
/// Sets the default ActivitySource name for all [Instrument] methods in this assembly.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class AutoInstrumentSourceAttribute : Attribute
{
    public AutoInstrumentSourceAttribute(string name) => Name = name;
    public string Name { get; }
}
