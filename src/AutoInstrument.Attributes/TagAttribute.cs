using System;

namespace AutoInstrument;

/// <summary>
/// Applied to a field or property to include it as a span tag
/// on all <c>[Instrument]</c> methods in the containing type.
/// </summary>
/// <example>
/// <code>
/// public class OrderService
/// {
///     [Tag] public string Region { get; set; }
///     [Tag(Name = "env")] public string Environment { get; set; }
///
///     [Instrument]
///     public void Process(int orderId) { }
///     // Tags: process.orderid, region, env
/// }
/// </code>
/// </example>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, Inherited = false, AllowMultiple = false)]
public sealed class TagAttribute : Attribute
{
    /// <summary>
    /// Override the tag name. Defaults to the member name in lowercase.
    /// </summary>
    public string? Name { get; set; }
}
