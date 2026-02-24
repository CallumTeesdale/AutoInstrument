using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace AutoInstrument.Generator;

/// <summary>
/// Emits a centralised ActivitySource holder and interceptor wrappers
/// for every call-site of an [Instrument]-decorated method.
/// </summary>
[Generator]
public sealed class InstrumentGenerator : IIncrementalGenerator
{
    private const string AttributeFqn = "AutoInstrument.InstrumentAttribute";
    private const string SourceAttributeFqn = "AutoInstrument.AutoInstrumentSourceAttribute";
    private const string MsBuildPropertyName = "build_property.AutoInstrumentSourceName";

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // 1. Collect instrumented methods
        var instrumentedMethods = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeFqn,
                predicate: static (node, _) => node is MethodDeclarationSyntax,
                transform: static (ctx, ct) => ExtractMethodInfo(ctx, ct))
            .Where(static m => m is not null)!;

        var collectedMethods = instrumentedMethods.Collect();

        // 2. Collect call-sites
        var callSites = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is InvocationExpressionSyntax,
                transform: static (ctx, ct) => TryGetInterceptCallSite(ctx, ct))
            .Where(static x => x is not null)!;

        var collectedCallSites = callSites.Collect();

        // 3. MSBuild property: <AutoInstrumentSourceName>
        var msBuildDefault = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) =>
            {
                provider.GlobalOptions.TryGetValue(MsBuildPropertyName, out var value);
                return string.IsNullOrWhiteSpace(value) ? null : value;
            });

        // 4. Assembly-level [AutoInstrumentSource("X")]
        var assemblyDefault = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                SourceAttributeFqn,
                predicate: static (node, _) => node is CompilationUnitSyntax,
                transform: static (ctx, _) =>
                {
                    var attr = ctx.Attributes.FirstOrDefault(a =>
                        a.AttributeClass?.ToDisplayString() == SourceAttributeFqn);
                    if (attr is null) return null;
                    var arg = attr.ConstructorArguments.FirstOrDefault();
                    return arg.Value as string;
                })
            .Where(static x => x is not null)
            .Collect()
            .Select(static (arr, _) => arr.IsDefaultOrEmpty ? null : arr[0]);

        // 5. Combine default name: MSBuild wins over assembly attribute
        var defaultName = msBuildDefault.Combine(assemblyDefault)
            .Select(static (pair, _) => pair.Left ?? pair.Right);

        // 6. Combine everything and emit
        var combined = collectedMethods
            .Combine(collectedCallSites)
            .Combine(defaultName);

        context.RegisterSourceOutput(combined, static (spc, pair) =>
        {
            var ((methods, sites), resolvedDefault) = pair;
            if (methods.IsDefaultOrEmpty && sites.IsDefaultOrEmpty) return;
            EmitCombinedFile(spc, methods, sites, resolvedDefault);
        });
    }

    private static InstrumentedMethodInfo? ExtractMethodInfo(
        GeneratorAttributeSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.TargetSymbol is not IMethodSymbol method) return null;
        var containingType = method.ContainingType;
        if (containingType is null) return null;

        var attr = ctx.Attributes.FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == AttributeFqn);
        if (attr is null) return null;

        string? spanName = null, activitySourceName = null;
        string[] skip = System.Array.Empty<string>(), fields = System.Array.Empty<string>();
        bool recordReturnValue = false, recordException = true;
        int kind = 0;

        foreach (var arg in attr.NamedArguments)
        {
            switch (arg.Key)
            {
                case "Name": spanName = arg.Value.Value as string; break;
                case "ActivitySourceName": activitySourceName = arg.Value.Value as string; break;
                case "RecordReturnValue": recordReturnValue = arg.Value.Value is true; break;
                case "RecordException": recordException = arg.Value.Value is not false; break;
                case "Kind": kind = (int)(arg.Value.Value ?? 0); break;
                case "Skip":
                    skip = arg.Value.Values.Select(v => v.Value as string).Where(v => v != null).ToArray()!;
                    break;
                case "Fields":
                    fields = arg.Value.Values.Select(v => v.Value as string).Where(v => v != null).ToArray()!;
                    break;
            }
        }

        var returnType = method.ReturnType;
        var returnTypeStr = returnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        bool isAsync = (ctx.TargetNode as MethodDeclarationSyntax)?.Modifiers.Any(SyntaxKind.AsyncKeyword) == true;
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

        var parameters = method.Parameters.Select(p => ExtractParameterInfo(p, refKind: p.RefKind switch
            {
                RefKind.Ref => "ref",
                RefKind.Out => "out",
                RefKind.In => "in",
                RefKind.RefReadOnlyParameter => "in",
                _ => null
            })).ToArray();

        return new InstrumentedMethodInfo
        {
            Namespace = containingType.ContainingNamespace?.IsGlobalNamespace == true
                ? "" : containingType.ContainingNamespace?.ToDisplayString() ?? "",
            ClassName = containingType.Name,
            MethodName = method.Name,
            FullyQualifiedClassName = containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            SpanName = spanName,
            AssemblyName = containingType.ContainingAssembly?.Name ?? "Unknown",
            ActivitySourceName = activitySourceName,
            ReturnType = returnTypeStr,
            IsAsync = isAsync,
            ReturnsVoid = returnsVoid,
            IsAwaitable = isAwaitable,
            HasGenericReturn = hasGenericReturn,
            InnerReturnType = innerReturnType,
            IsStatic = method.IsStatic,
            IsExtensionMethod = method.IsExtensionMethod,
            Parameters = new EquatableArray<ParameterInfo>(parameters),
            Skip = new EquatableArray<string>(skip),
            Fields = new EquatableArray<string>(fields),
            RecordReturnValue = recordReturnValue,
            RecordException = recordException,
            Kind = kind,
        };
    }

    private static InterceptCallSite? TryGetInterceptCallSite(
        GeneratorSyntaxContext ctx, CancellationToken ct)
    {
        if (ctx.Node is not InvocationExpressionSyntax invocation) return null;

        if (ctx.SemanticModel.GetSymbolInfo(invocation, ct).Symbol is not IMethodSymbol calledMethod)
            return null;

        var targetMethod = calledMethod.ReducedFrom ?? calledMethod.OriginalDefinition ?? calledMethod;
        var hasInstrument = targetMethod.GetAttributes().Any(a =>
            a.AttributeClass?.ToDisplayString() == AttributeFqn);
        if (!hasInstrument) return null;

#pragma warning disable RSEXPERIMENTAL002
        var location = ctx.SemanticModel.GetInterceptableLocation(invocation);
#pragma warning restore RSEXPERIMENTAL002
        if (location is null) return null;

        var attr = targetMethod.GetAttributes().First(a =>
            a.AttributeClass?.ToDisplayString() == AttributeFqn);

        string? spanName = null, activitySourceName = null;
        string[] skip = System.Array.Empty<string>(), fields = System.Array.Empty<string>();
        bool recordReturnValue = false, recordException = true;
        int kind = 0;

        foreach (var arg in attr.NamedArguments)
        {
            switch (arg.Key)
            {
                case "Name": spanName = arg.Value.Value as string; break;
                case "ActivitySourceName": activitySourceName = arg.Value.Value as string; break;
                case "RecordReturnValue": recordReturnValue = arg.Value.Value is true; break;
                case "RecordException": recordException = arg.Value.Value is not false; break;
                case "Kind": kind = (int)(arg.Value.Value ?? 0); break;
                case "Skip":
                    skip = arg.Value.Values.Select(v => v.Value as string).Where(v => v != null).ToArray()!;
                    break;
                case "Fields":
                    fields = arg.Value.Values.Select(v => v.Value as string).Where(v => v != null).ToArray()!;
                    break;
            }
        }

        var containingType = targetMethod.ContainingType;
        var returnType = calledMethod.ReturnType;
        var returnTypeStr = returnType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        bool isAsync = false;
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

        var parameters = calledMethod.Parameters.Select(p => ExtractParameterInfo(p, refKind: p.RefKind switch
            {
                RefKind.Ref => "ref",
                RefKind.Out => "out",
                RefKind.In => "in",
                _ => null
            })).ToArray();

        string receiverType = "";
        bool isStaticCall = calledMethod.IsStatic;
        if (!isStaticCall && containingType != null)
        {
            receiverType = containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        var methodInfo = new InstrumentedMethodInfo
        {
            Namespace = containingType?.ContainingNamespace?.IsGlobalNamespace == true
                ? "" : containingType?.ContainingNamespace?.ToDisplayString() ?? "",
            ClassName = containingType?.Name ?? "",
            MethodName = targetMethod.Name,
            FullyQualifiedClassName = containingType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "",
            SpanName = spanName,
            AssemblyName = containingType?.ContainingAssembly?.Name ?? "Unknown",
            ActivitySourceName = activitySourceName,
            ReturnType = returnTypeStr,
            IsAsync = isAsync,
            ReturnsVoid = returnsVoid,
            IsAwaitable = isAwaitable,
            HasGenericReturn = hasGenericReturn,
            InnerReturnType = innerReturnType,
            IsStatic = calledMethod.IsStatic,
            IsExtensionMethod = calledMethod.IsExtensionMethod,
            Parameters = new EquatableArray<ParameterInfo>(parameters),
            Skip = new EquatableArray<string>(skip),
            Fields = new EquatableArray<string>(fields),
            RecordReturnValue = recordReturnValue,
            RecordException = recordException,
            Kind = kind,
        };

        return new InterceptCallSite
        {
            Method = methodInfo,
#pragma warning disable RSEXPERIMENTAL002
            LocationVersion = location.Version,
            LocationData = location.Data,
            DisplayLocation = location.GetDisplayLocation(),
#pragma warning restore RSEXPERIMENTAL002
            ReceiverType = receiverType,
            IsStaticCall = isStaticCall,
        };
    }

    private static void EmitCombinedFile(
        SourceProductionContext spc,
        ImmutableArray<InstrumentedMethodInfo?> methods,
        ImmutableArray<InterceptCallSite?> sites,
        string? defaultActivitySourceName = null)
    {
        // Apply the resolved default name to all methods/sites that don't have per-method overrides
        if (defaultActivitySourceName is not null)
        {
            methods = methods.Select(m =>
                m is not null && m.DefaultActivitySourceName is null
                    ? m with { DefaultActivitySourceName = defaultActivitySourceName }
                    : m).ToImmutableArray();

            sites = sites.Select(s =>
                s is not null && s.Method.DefaultActivitySourceName is null
                    ? s with { Method = s.Method with { DefaultActivitySourceName = defaultActivitySourceName } }
                    : s).ToImmutableArray();
        }

        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated/>");
        sb.AppendLine("// Generated by AutoInstrument");
        sb.AppendLine("#nullable enable");
        sb.AppendLine("#pragma warning disable CS9113 // Parameter is unread");
        sb.AppendLine("#pragma warning disable CS1998 // Async method lacks 'await' operators");
        sb.AppendLine("#pragma warning disable CS8603 // Possible null reference return");
        sb.AppendLine();

        if (!sites.IsDefaultOrEmpty)
        {
            sb.AppendLine("namespace System.Runtime.CompilerServices");
            sb.AppendLine("{");
            sb.AppendLine("    [global::System.Diagnostics.Conditional(\"DEBUG\")]");
            sb.AppendLine("    [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]");
            sb.AppendLine("    sealed file class InterceptsLocationAttribute : global::System.Attribute");
            sb.AppendLine("    {");
            sb.AppendLine("        public InterceptsLocationAttribute(int version, string data)");
            sb.AppendLine("        {");
            sb.AppendLine("            _ = version;");
            sb.AppendLine("            _ = data;");
            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            sb.AppendLine();
        }

        sb.AppendLine("namespace AutoInstrument.Generated");
        sb.AppendLine("{");

        var sourceNames = new System.Collections.Generic.HashSet<string>();
        if (!methods.IsDefaultOrEmpty)
        {
            foreach (var m in methods)
                if (m is not null) sourceNames.Add(m.EffectiveActivitySourceName);
        }
        if (!sites.IsDefaultOrEmpty)
        {
            foreach (var s in sites)
                if (s is not null) sourceNames.Add(s.Method.EffectiveActivitySourceName);
        }

        if (sourceNames.Count > 0)
        {
            sb.AppendLine("    file static class ActivitySources");
            sb.AppendLine("    {");
            foreach (var name in sourceNames.OrderBy(n => n))
            {
                var fieldName = "__autoInstrumentSource_" + Sanitize(name);
                sb.AppendLine($"        internal static readonly global::System.Diagnostics.ActivitySource {fieldName}");
                sb.AppendLine($"            = new(\"{Escape(name)}\");");
            }
            sb.AppendLine("    }");
            sb.AppendLine();
        }

        if (!sites.IsDefaultOrEmpty)
        {
            var byMethod = sites.Where(s => s != null)
                .GroupBy(s => $"{s!.Method.FullyQualifiedClassName}.{s.Method.MethodName}")
                .ToArray();

            sb.AppendLine("    file static class AutoInstrumentInterceptors");
            sb.AppendLine("    {");

            int methodIndex = 0;
            foreach (var group in byMethod)
            {
                var siteList = group.ToList();
                var first = siteList[0]!;
                var method = first.Method;
                var sourceName = method.EffectiveActivitySourceName;
                var sourceField = "__autoInstrumentSource_" + Sanitize(sourceName);
                var spanName = method.EffectiveSpanName;
                var activityKind = KindToString(method.Kind);

                foreach (var site in siteList)
                {
                    sb.AppendLine($"        [global::System.Runtime.CompilerServices.InterceptsLocation({site!.LocationVersion}, \"{site.LocationData}\")] // {site.DisplayLocation}");
                }

                var wrapperName = $"__Intercept_{method.ClassName}_{method.MethodName}_{methodIndex}";
                var isInstance = !first.IsStaticCall;

                var paramParts = new System.Collections.Generic.List<string>();
                if (isInstance)
                {
                    paramParts.Add($"this {first.ReceiverType} __self");
                }
                foreach (var p in method.Parameters)
                {
                    var prefix = p.RefKind is not null ? p.RefKind + " " : "";
                    paramParts.Add($"{prefix}{p.Type} {p.Name}");
                }
                var paramStr = string.Join(", ", paramParts);

                var argParts = new System.Collections.Generic.List<string>();
                foreach (var p in method.Parameters)
                {
                    var prefix = p.RefKind switch
                    {
                        "ref" => "ref ",
                        "out" => "out ",
                        "in" => "in ",
                        _ => ""
                    };
                    argParts.Add($"{prefix}{p.Name}");
                }
                var argStr = string.Join(", ", argParts);

                var asyncKeyword = method.IsAwaitable ? "async " : "";
                var returnType = method.ReturnType;

                sb.AppendLine($"        internal static {asyncKeyword}{returnType} {wrapperName}({paramStr})");
                sb.AppendLine("        {");

                // HasListeners() guard — fast path when OTel isn't configured
                var callExpr = isInstance ? $"__self.{method.MethodName}({argStr})" : $"{method.FullyQualifiedClassName}.{method.MethodName}({argStr})";
                var awaitExpr = method.IsAwaitable ? "await " : "";

                sb.AppendLine($"            if (!ActivitySources.{sourceField}.HasListeners())");
                if (method.ReturnsVoid || (!method.HasGenericReturn && method.IsAwaitable))
                {
                    sb.AppendLine("            {");
                    sb.AppendLine($"                {awaitExpr}{callExpr};");
                    sb.AppendLine("                return;");
                    sb.AppendLine("            }");
                }
                else
                {
                    sb.AppendLine($"                return {awaitExpr}{callExpr};");
                }
                sb.AppendLine();

                sb.AppendLine($"            using var __activity = ActivitySources.{sourceField}");
                sb.AppendLine($"                .StartActivity(\"{Escape(spanName)}\", {activityKind});");
                sb.AppendLine();

                var taggable = method.Parameters
                    .Where(p => ShouldTagParameter(p, method) && p.RefKind != "out")
                    .ToList();

                if (taggable.Count > 0)
                {
                    sb.AppendLine("            if (__activity is not null)");
                    sb.AppendLine("            {");
                    foreach (var p in taggable)
                    {
                        if (p.IsComplex && p.Properties.Length > 0)
                        {
                            foreach (var prop in p.Properties)
                            {
                                if (!ShouldTagProperty(p, prop, method)) continue;
                                var tagName = $"{method.MethodName.ToLowerInvariant()}.{p.Name.ToLowerInvariant()}.{prop.Name.ToLowerInvariant()}";
                                sb.AppendLine($"                __activity.SetTag(\"{Escape(tagName)}\", {p.Name}?.{prop.Name});");
                            }
                        }
                        else
                        {
                            var tagName = $"{method.MethodName.ToLowerInvariant()}.{p.Name.ToLowerInvariant()}";
                            sb.AppendLine($"                __activity.SetTag(\"{Escape(tagName)}\", {p.Name});");
                        }
                    }
                    sb.AppendLine("            }");
                    sb.AppendLine();
                }

                if (method.RecordException)
                {
                    sb.AppendLine("            try");
                    sb.AppendLine("            {");

                    if (method.ReturnsVoid || (!method.HasGenericReturn && method.IsAwaitable))
                    {
                        sb.AppendLine($"                {awaitExpr}{callExpr};");
                    }
                    else
                    {
                        sb.AppendLine($"                var __result = {awaitExpr}{callExpr};");
                        if (method.RecordReturnValue)
                        {
                            sb.AppendLine($"                __activity?.SetTag(\"{method.MethodName.ToLowerInvariant()}.return_value\", __result?.ToString());");
                        }
                        sb.AppendLine("                return __result;");
                    }

                    sb.AppendLine("            }");
                    sb.AppendLine("            catch (global::System.Exception __ex)");
                    sb.AppendLine("            {");
                    sb.AppendLine("                if (__activity is not null)");
                    sb.AppendLine("                {");
                    sb.AppendLine("                    __activity.SetStatus(global::System.Diagnostics.ActivityStatusCode.Error, __ex.Message);");
                    sb.AppendLine("                    __activity.AddEvent(new global::System.Diagnostics.ActivityEvent(\"exception\",");
                    sb.AppendLine("                        tags: new global::System.Diagnostics.ActivityTagsCollection");
                    sb.AppendLine("                        {");
                    sb.AppendLine("                            { \"exception.type\", __ex.GetType().FullName },");
                    sb.AppendLine("                            { \"exception.message\", __ex.Message },");
                    sb.AppendLine("                            { \"exception.stacktrace\", __ex.ToString() }");
                    sb.AppendLine("                        }));");
                    sb.AppendLine("                }");
                    sb.AppendLine("                throw;");
                    sb.AppendLine("            }");
                }
                else
                {
                    if (method.ReturnsVoid || (!method.HasGenericReturn && method.IsAwaitable))
                    {
                        sb.AppendLine($"            {awaitExpr}{callExpr};");
                    }
                    else
                    {
                        if (method.RecordReturnValue)
                        {
                            sb.AppendLine($"            var __result = {awaitExpr}{callExpr};");
                            sb.AppendLine($"            __activity?.SetTag(\"{method.MethodName.ToLowerInvariant()}.return_value\", __result?.ToString());");
                            sb.AppendLine("            return __result;");
                        }
                        else
                        {
                            sb.AppendLine($"            return {awaitExpr}{callExpr};");
                        }
                    }
                }

                sb.AppendLine("        }");
                sb.AppendLine();
                methodIndex++;
            }

            sb.AppendLine("    }");
        }

        sb.AppendLine("}");

        spc.AddSource("AutoInstrumentInterceptors.g.cs", SourceText.From(sb.ToString(), Encoding.UTF8));
    }

    private static bool ShouldTagParameter(ParameterInfo p, InstrumentedMethodInfo m)
    {
        if (m.Fields.Length > 0)
        {
            // Include if Fields mentions this param directly or via dot-notation
            return m.Fields.Any(f => f == p.Name || f.StartsWith(p.Name + "."));
        }
        if (m.Skip.Length > 0)
        {
            // Exclude if Skip mentions this param directly (without dot — dot means skip specific property)
            return !m.Skip.Any(s => s == p.Name);
        }
        return true;
    }

    private static bool ShouldTagProperty(ParameterInfo p, PropertyMetadata prop, InstrumentedMethodInfo m)
    {
        var dotPath = $"{p.Name}.{prop.Name}";
        if (m.Fields.Length > 0)
        {
            // If Fields has dot-notation entries for this param, only include those specific properties
            var paramDotFields = m.Fields.Where(f => f.StartsWith(p.Name + ".")).ToList();
            if (paramDotFields.Count > 0)
                return paramDotFields.Any(f => f == dotPath);
            // Fields mentions param without dot → include all properties
            return m.Fields.Any(f => f == p.Name);
        }
        if (m.Skip.Length > 0)
        {
            return !m.Skip.Any(s => s == dotPath);
        }
        return true;
    }

    private static ParameterInfo ExtractParameterInfo(IParameterSymbol p, string? refKind)
    {
        var type = p.Type;
        var typeStr = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        bool isComplex = IsComplexType(type);
        var properties = System.Array.Empty<PropertyMetadata>();

        if (isComplex)
        {
            properties = type.GetMembers()
                .OfType<IPropertySymbol>()
                .Where(prop => prop.DeclaredAccessibility == Accessibility.Public
                    && !prop.IsStatic
                    && !prop.IsIndexer
                    && prop.GetMethod is not null)
                .Select(prop => new PropertyMetadata(
                    prop.Name,
                    prop.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)))
                .ToArray();
        }

        return new ParameterInfo(
            p.Name,
            typeStr,
            refKind,
            isComplex,
            new EquatableArray<PropertyMetadata>(properties));
    }

    private static bool IsComplexType(ITypeSymbol type)
    {
        if (type.SpecialType != SpecialType.None) return false;
        var name = type.ToDisplayString();
        if (name is "decimal" or "System.Decimal"
                or "System.DateTime" or "System.DateTimeOffset"
                or "System.TimeSpan" or "System.Guid") return false;
        if (type.TypeKind == TypeKind.Enum) return false;
        return true;
    }

    private static string KindToString(int kind) => kind switch
    {
        1 => "global::System.Diagnostics.ActivityKind.Server",
        2 => "global::System.Diagnostics.ActivityKind.Client",
        3 => "global::System.Diagnostics.ActivityKind.Producer",
        4 => "global::System.Diagnostics.ActivityKind.Consumer",
        _ => "global::System.Diagnostics.ActivityKind.Internal",
    };

    private static string Sanitize(string s) =>
        string.Concat(s.Select(c => char.IsLetterOrDigit(c) ? c : '_'));

    private static string Escape(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
