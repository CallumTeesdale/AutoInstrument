namespace AutoInstrument;

/// <summary>
/// Controls how parameter tag names are formatted.
/// </summary>
public enum TagNamingConvention
{
    /// <summary>
    /// Default: <c>methodname.param</c> / <c>methodname.param.prop</c>
    /// </summary>
    Method = 0,

    /// <summary>
    /// Flat: <c>param</c> / <c>param.prop</c> (no method prefix)
    /// </summary>
    Flat = 1,

    /// <summary>
    /// Alias for <see cref="Method"/> for OpenTelemetry clarity.
    /// </summary>
    OTel = 2,
}
