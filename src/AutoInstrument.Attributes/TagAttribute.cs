using System;

namespace AutoInstrument;

/// <summary>
/// Includes a field or property as a span tag on all [Instrument] methods in the containing type.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class TagAttribute : Attribute
{
    /// <summary>Override the tag name. Defaults to the member name in lowercase.</summary>
    public string? Name { get; set; }

    /// <summary>Properties to exclude from tags when the member is a complex type.</summary>
    public string[]? Skip { get; set; }

    /// <summary>Whitelist of properties to include as tags when the member is a complex type. Takes precedence over <see cref="Skip"/>.</summary>
    public string[]? Fields { get; set; }
}
