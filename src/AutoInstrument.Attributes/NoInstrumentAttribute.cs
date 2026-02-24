using System;

namespace AutoInstrument;

/// <summary>
/// Applied to a parameter to skip it from span tags entirely,
/// or to skip specific properties of a complex parameter via dot-notation.
/// </summary>
/// <example>
/// <code>
/// // Skip entire parameter
/// public void Login(string user, [NoInstrument] string password) { }
///
/// // Skip specific properties
/// public void Process([NoInstrument("CreditCard", "SSN")] Order order) { }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Parameter, Inherited = false, AllowMultiple = false)]
public sealed class NoInstrumentAttribute : Attribute
{
    /// <summary>
    /// Property names to skip. If empty, the entire parameter is skipped.
    /// </summary>
    public string[] Properties { get; }

    public NoInstrumentAttribute(params string[] properties)
    {
        Properties = properties ?? Array.Empty<string>();
    }
}
