using System.Linq;
using Microsoft.CodeAnalysis;

namespace AutoInstrument.Generator;

/// <summary>Parsed representation of [Instrument] attribute arguments.</summary>
internal sealed record ParsedInstrumentAttribute(
    string? SpanName,
    string? ActivitySourceName,
    string[] Skip,
    string[] Fields,
    bool RecordReturnValue,
    bool RecordException,
    int Kind,
    bool RecordSuccess,
    bool IgnoreCancellation,
    string? Condition,
    string? LinkTo,
    int Depth);

/// <summary>Parsed return type information.</summary>
internal sealed record ReturnTypeInfo(
    string TypeStr,
    bool IsAwaitable,
    bool ReturnsVoid,
    bool HasGenericReturn,
    string? InnerReturnType);

/// <summary>All type/property analysis helpers extracted from InstrumentGenerator.</summary>
internal static class TypeAnalysis
{
    private const string NoInstrumentAttributeFqn = "AutoInstrument.NoInstrumentAttribute";
    private const string TagAttributeFqn = "AutoInstrument.TagAttribute";
    private const int DefaultDepth = 1;

    internal static ParsedInstrumentAttribute ParseInstrumentAttribute(AttributeData attr)
    {
        string? spanName = null, activitySourceName = null, condition = null, linkTo = null;
        string[] skip = System.Array.Empty<string>(), fields = System.Array.Empty<string>();
        bool recordReturnValue = false, recordException = true, recordSuccess = false, ignoreCancellation = true;
        int kind = 0, depth = -1;

        foreach (var arg in attr.NamedArguments)
        {
            switch (arg.Key)
            {
                case "Name": spanName = arg.Value.Value as string; break;
                case "ActivitySourceName": activitySourceName = arg.Value.Value as string; break;
                case "RecordReturnValue": recordReturnValue = arg.Value.Value is true; break;
                case "RecordException": recordException = arg.Value.Value is not false; break;
                case "Kind": kind = (int)(arg.Value.Value ?? 0); break;
                case "RecordSuccess": recordSuccess = arg.Value.Value is true; break;
                case "IgnoreCancellation": ignoreCancellation = arg.Value.Value is not false; break;
                case "Condition": condition = arg.Value.Value as string; break;
                case "LinkTo": linkTo = arg.Value.Value as string; break;
                case "Depth": depth = (int)(arg.Value.Value ?? -1); break;
                case "Skip":
                    skip = arg.Value.Values.Select(v => v.Value as string).Where(v => v != null).ToArray()!;
                    break;
                case "Fields":
                    fields = arg.Value.Values.Select(v => v.Value as string).Where(v => v != null).ToArray()!;
                    break;
            }
        }

        return new ParsedInstrumentAttribute(spanName, activitySourceName, skip, fields,
            recordReturnValue, recordException, kind, recordSuccess, ignoreCancellation,
            condition, linkTo, depth);
    }

    internal static ReturnTypeInfo AnalyzeReturnType(ITypeSymbol returnType)
    {
        var returnTypeStr = returnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        bool returnsVoid = returnType.SpecialType == SpecialType.System_Void;
        bool isAwaitable = false;
        bool hasGenericReturn = false;
        string? innerReturnType = null;

        var rtName = returnType.ToDisplayString();
        if (rtName == "System.Threading.Tasks.Task" || rtName == "System.Threading.Tasks.ValueTask")
        {
            isAwaitable = true;
        }
        else if (returnType is INamedTypeSymbol namedRt && namedRt.IsGenericType)
        {
            var baseName = namedRt.ConstructedFrom.ToDisplayString();
            if (baseName.StartsWith("System.Threading.Tasks.Task<") ||
                baseName.StartsWith("System.Threading.Tasks.ValueTask<"))
            {
                isAwaitable = true;
                hasGenericReturn = true;
                innerReturnType = namedRt.TypeArguments[0].ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            }
        }

        return new ReturnTypeInfo(returnTypeStr, isAwaitable, returnsVoid, hasGenericReturn, innerReturnType);
    }

    internal static InstrumentedMethodInfo BuildMethodInfo(
        IMethodSymbol method, INamedTypeSymbol? containingType,
        ParsedInstrumentAttribute parsed, ReturnTypeInfo returnInfo,
        bool isAsync, ParameterInfo[] parameters, TagMemberInfo[] tagMembers)
    {
        return new InstrumentedMethodInfo
        {
            Namespace = containingType?.ContainingNamespace?.IsGlobalNamespace == true
                ? "" : containingType?.ContainingNamespace?.ToDisplayString() ?? "",
            ClassName = containingType?.Name ?? "",
            MethodName = method.Name,
            FullyQualifiedClassName = containingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "",
            SpanName = parsed.SpanName,
            AssemblyName = containingType?.ContainingAssembly?.Name ?? "Unknown",
            ActivitySourceName = parsed.ActivitySourceName,
            ReturnType = returnInfo.TypeStr,
            IsAsync = isAsync,
            ReturnsVoid = returnInfo.ReturnsVoid,
            IsAwaitable = returnInfo.IsAwaitable,
            HasGenericReturn = returnInfo.HasGenericReturn,
            InnerReturnType = returnInfo.InnerReturnType,
            IsStatic = method.IsStatic,
            IsExtensionMethod = method.IsExtensionMethod,
            Parameters = new EquatableArray<ParameterInfo>(parameters),
            Skip = new EquatableArray<string>(parsed.Skip),
            Fields = new EquatableArray<string>(parsed.Fields),
            RecordReturnValue = parsed.RecordReturnValue,
            RecordException = parsed.RecordException,
            Kind = parsed.Kind,
            RecordSuccess = parsed.RecordSuccess,
            IgnoreCancellation = parsed.IgnoreCancellation,
            Condition = parsed.Condition,
            LinkTo = parsed.LinkTo,
            TagMembers = new EquatableArray<TagMemberInfo>(tagMembers),
            Depth = parsed.Depth,
        };
    }

    internal static string[]? CalculateForcedPaths(string prefix, string[]? skip, string[]? fields)
    {
        var allPaths = (skip ?? System.Array.Empty<string>())
            .Concat(fields ?? System.Array.Empty<string>());

        string[] result;
        if (prefix.Length > 0)
        {
            result = allPaths
                .Where(f => f.StartsWith(prefix + ".", System.StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Substring(prefix.Length + 1))
                .ToArray();
        }
        else
        {
            result = allPaths.ToArray();
        }

        return result.Length > 0 ? result : null;
    }

    internal static bool IsComplexType(ITypeSymbol type)
    {
        if (type.SpecialType != SpecialType.None) return false;
        var name = type.ToDisplayString();
        if (name is "decimal" or "System.Decimal"
                or "System.DateTime" or "System.DateTimeOffset"
                or "System.TimeSpan" or "System.Guid"
                or "System.Threading.CancellationToken"
                or "System.Diagnostics.ActivityContext") return false;
        if (type.TypeKind == TypeKind.Enum) return false;
        if (type.TypeKind == TypeKind.Array) return false;
        if (IsCollectionType(type)) return false;
        return true;
    }

    internal static bool IsCollectionType(ITypeSymbol type)
    {
        if (type is not INamedTypeSymbol named) return false;
        foreach (var iface in named.AllInterfaces)
        {
            if (iface.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>")
                return true;
        }
        if (named.OriginalDefinition.ToDisplayString() == "System.Collections.Generic.IEnumerable<T>")
            return true;
        return false;
    }

    internal static PropertyMetadata[] ExtractProperties(ITypeSymbol type, int remainingDepth,
        System.Collections.Generic.HashSet<string>? visited = null, string[]? forcedPaths = null)
    {
        bool hasForcedChildren = forcedPaths is { Length: > 0 };
        if (remainingDepth <= 0 && !hasForcedChildren) return System.Array.Empty<PropertyMetadata>();

        visited ??= new System.Collections.Generic.HashSet<string>();
        var typeKey = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        if (!visited.Add(typeKey)) return System.Array.Empty<PropertyMetadata>();

        var result = type.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(prop => prop.DeclaredAccessibility == Accessibility.Public
                && !prop.IsStatic
                && !prop.IsIndexer
                && prop.GetMethod is not null)
            .Select(prop =>
            {
                var propType = prop.Type;
                var propTypeStr = propType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                bool propIsComplex = IsComplexType(propType);

                var childForced = forcedPaths?
                    .Where(fp => fp.StartsWith(prop.Name + ".", System.StringComparison.OrdinalIgnoreCase))
                    .Select(fp => fp.Substring(prop.Name.Length + 1))
                    .ToArray();
                bool hasForced = childForced is { Length: > 0 };

                var children = propIsComplex && (remainingDepth > 1 || hasForced)
                    ? ExtractProperties(propType, System.Math.Max(remainingDepth - 1, 0), visited,
                        hasForced ? childForced : null)
                    : System.Array.Empty<PropertyMetadata>();
                return new PropertyMetadata(
                    prop.Name,
                    propTypeStr,
                    propIsComplex && children.Length > 0,
                    new EquatableArray<PropertyMetadata>(children));
            })
            .ToArray();

        visited.Remove(typeKey);
        return result;
    }

    internal static ParameterInfo ExtractParameterInfo(IParameterSymbol p, string? refKind,
        int depth = DefaultDepth, string[]? skip = null, string[]? fields = null)
    {
        var type = p.Type;
        var typeStr = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        bool isComplex = IsComplexType(type);

        var forcedPaths = isComplex ? CalculateForcedPaths(p.Name, skip, fields) : null;

        var properties = isComplex
            ? ExtractProperties(type, depth, forcedPaths: forcedPaths)
            : System.Array.Empty<PropertyMetadata>();

        bool hasNoInstrument = false;
        var noInstrumentProperties = System.Array.Empty<string>();
        var noInstrAttr = p.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == NoInstrumentAttributeFqn);
        if (noInstrAttr is not null)
        {
            hasNoInstrument = true;
            if (noInstrAttr.ConstructorArguments.Length > 0)
            {
                var arg = noInstrAttr.ConstructorArguments[0];
                if (arg.Kind == TypedConstantKind.Array)
                {
                    noInstrumentProperties = arg.Values
                        .Select(v => v.Value as string)
                        .Where(v => v != null)
                        .ToArray()!;
                }
            }
        }

        return new ParameterInfo(
            p.Name,
            typeStr,
            refKind,
            isComplex,
            new EquatableArray<PropertyMetadata>(properties),
            hasNoInstrument,
            new EquatableArray<string>(noInstrumentProperties));
    }

    internal static TagMemberInfo[] ExtractTagMembers(INamedTypeSymbol containingType)
    {
        var tagMembers = new System.Collections.Generic.List<TagMemberInfo>();
        foreach (var member in containingType.GetMembers())
        {
            var tagAttr = member.GetAttributes().FirstOrDefault(a =>
                a.AttributeClass?.ToDisplayString() == TagAttributeFqn);
            if (tagAttr is null) continue;

            string? tagName = null;
            string[]? skip = null;
            string[]? fields = null;
            int depth = -1;
            foreach (var arg in tagAttr.NamedArguments)
            {
                switch (arg.Key)
                {
                    case "Name":
                        tagName = arg.Value.Value as string;
                        break;
                    case "Skip":
                        skip = arg.Value.Values.Select(v => v.Value as string).Where(v => v != null).ToArray()!;
                        break;
                    case "Fields":
                        fields = arg.Value.Values.Select(v => v.Value as string).Where(v => v != null).ToArray()!;
                        break;
                    case "Depth":
                        depth = (int)(arg.Value.Value ?? -1);
                        break;
                }
            }

            ITypeSymbol typeSymbol;
            if (member is IPropertySymbol prop)
                typeSymbol = prop.Type;
            else if (member is IFieldSymbol field)
                typeSymbol = field.Type;
            else
                continue;

            var memberType = typeSymbol.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            bool isComplex = IsComplexType(typeSymbol);
            var effectiveDepth = depth >= 0 ? depth : DefaultDepth;

            var forcedPaths = isComplex ? CalculateForcedPaths("", skip, fields) : null;

            var properties = isComplex
                ? ExtractProperties(typeSymbol, effectiveDepth, forcedPaths: forcedPaths)
                : System.Array.Empty<PropertyMetadata>();

            tagMembers.Add(new TagMemberInfo(
                member.Name,
                tagName,
                memberType,
                isComplex && properties.Length > 0,
                new EquatableArray<PropertyMetadata>(properties),
                new EquatableArray<string>(skip ?? System.Array.Empty<string>()),
                new EquatableArray<string>(fields ?? System.Array.Empty<string>()),
                depth));
        }
        return tagMembers.ToArray();
    }
}
