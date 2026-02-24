using System.Linq;

namespace AutoInstrument.Generator;

/// <summary>Skip/Fields/NoInstrument filtering and tag name formatting.</summary>
internal static class TagFiltering
{
    internal static bool ShouldTagParameter(ParameterInfo p, InstrumentedMethodInfo m)
    {
        if (p.HasNoInstrument && p.NoInstrumentProperties.Length == 0)
            return false;

        if (m.Fields.Length > 0)
        {
            return m.Fields.Any(f => f == p.Name || f.StartsWith(p.Name + "."));
        }
        if (m.Skip.Length > 0)
        {
            return !m.Skip.Any(s => s == p.Name);
        }
        return true;
    }

    internal static bool ShouldTagProperty(string dotPath, string paramName, EquatableArray<string> skip, EquatableArray<string> fields,
        EquatableArray<string> noInstrumentProperties = default)
    {
        if (noInstrumentProperties.Length > 0)
        {
            var relativePath = dotPath.StartsWith(paramName + ".")
                ? dotPath.Substring(paramName.Length + 1) : dotPath;
            if (noInstrumentProperties.Any(np => string.Equals(np, relativePath, System.StringComparison.OrdinalIgnoreCase)))
                return false;
        }

        if (fields.Length > 0)
        {
            var paramDotFields = fields.Where(f => f.StartsWith(paramName + ".", System.StringComparison.OrdinalIgnoreCase)).ToList();
            if (paramDotFields.Count > 0)
                return paramDotFields.Any(f => string.Equals(f, dotPath, System.StringComparison.OrdinalIgnoreCase)
                    || f.StartsWith(dotPath + ".", System.StringComparison.OrdinalIgnoreCase));
            return fields.Any(f => string.Equals(f, paramName, System.StringComparison.OrdinalIgnoreCase));
        }
        if (skip.Length > 0)
        {
            return !skip.Any(s => string.Equals(s, dotPath, System.StringComparison.OrdinalIgnoreCase));
        }
        return true;
    }

    internal static bool ShouldTagMemberProperty(string dotPath, EquatableArray<string> skip, EquatableArray<string> fields)
    {
        if (fields.Length > 0)
            return fields.Any(f => string.Equals(f, dotPath, System.StringComparison.OrdinalIgnoreCase)
                || f.StartsWith(dotPath + ".", System.StringComparison.OrdinalIgnoreCase));
        if (skip.Length > 0)
            return !skip.Any(s => string.Equals(s, dotPath, System.StringComparison.OrdinalIgnoreCase));
        return true;
    }

    internal static string FormatTagName(string methodName, string paramName, string[]? propPath, int convention)
    {
        var method = methodName.ToLowerInvariant();
        var param = paramName.ToLowerInvariant();
        var suffix = propPath is { Length: > 0 }
            ? "." + string.Join(".", propPath.Select(s => s.ToLowerInvariant()))
            : "";

        return convention switch
        {
            1 => $"{param}{suffix}",
            _ => $"{method}.{param}{suffix}",
        };
    }
}
