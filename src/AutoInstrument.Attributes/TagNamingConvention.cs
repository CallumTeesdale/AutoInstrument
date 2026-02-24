namespace AutoInstrument;

/// <summary>
/// Controls how parameter tag names are formatted.
/// </summary>
public enum TagNamingConvention
{
    /// <summary>methodname.param</summary>
    Method = 0,

    /// <summary>param (no method prefix)</summary>
    Flat = 1,

    /// <summary>Alias for <see cref="Method"/>.</summary>
    OTel = 2,
}
