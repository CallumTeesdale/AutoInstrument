using System.Collections.Immutable;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace AutoInstrument.Generator;

/// <summary>Incremental generator that emits interceptors for [Instrument]-decorated methods.</summary>
[Generator]
public sealed class InstrumentGenerator : IIncrementalGenerator
{
    private const string AttributeFqn = "AutoInstrument.InstrumentAttribute";
    private const string SourceAttributeFqn = "AutoInstrument.AutoInstrumentSourceAttribute";
    private const string ConfigAttributeFqn = "AutoInstrument.AutoInstrumentConfigAttribute";
    private const string NoInstrumentAttributeFqn = "AutoInstrument.NoInstrumentAttribute";
    private const string TagAttributeFqn = "AutoInstrument.TagAttribute";
    private const string MsBuildPropertyName = "build_property.AutoInstrumentSourceName";
    private const string MsBuildTagNamingPropertyName = "build_property.AutoInstrumentTagNaming";
    private const string MsBuildDepthPropertyName = "build_property.AutoInstrumentDepth";
    private const int DefaultDepth = 1;

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var instrumentedMethods = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeFqn,
                predicate: static (node, _) => node is MethodDeclarationSyntax,
                transform: static (ctx, ct) => ExtractMethodInfo(ctx, ct))
            .Where(static m => m is not null)!;

        var collectedMethods = instrumentedMethods.Collect();

        var callSites = context.SyntaxProvider
            .CreateSyntaxProvider(
                predicate: static (node, _) => node is InvocationExpressionSyntax,
                transform: static (ctx, ct) => TryGetInterceptCallSite(ctx, ct))
            .Where(static x => x is not null)!;

        var collectedCallSites = callSites.Collect();

        var msBuildDefault = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) =>
            {
                provider.GlobalOptions.TryGetValue(MsBuildPropertyName, out var value);
                return string.IsNullOrWhiteSpace(value) ? null : value;
            });

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

        var defaultName = msBuildDefault.Combine(assemblyDefault)
            .Select(static (pair, _) => pair.Left ?? pair.Right);

        var assemblyTagNaming = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ConfigAttributeFqn,
                predicate: static (node, _) => node is CompilationUnitSyntax,
                transform: static (ctx, _) =>
                {
                    var attr = ctx.Attributes.FirstOrDefault(a =>
                        a.AttributeClass?.ToDisplayString() == ConfigAttributeFqn);
                    if (attr is null) return (int?)null;
                    foreach (var arg in attr.NamedArguments)
                    {
                        if (arg.Key == "TagNaming" && arg.Value.Value is int val)
                            return val;
                    }
                    return null;
                })
            .Where(static x => x is not null)
            .Collect()
            .Select(static (arr, _) => arr.IsDefaultOrEmpty ? (int?)null : arr[0]);

        var msBuildTagNaming = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) =>
            {
                provider.GlobalOptions.TryGetValue(MsBuildTagNamingPropertyName, out var value);
                if (string.IsNullOrWhiteSpace(value)) return (int?)null;
                return value!.Trim().ToLowerInvariant() switch
                {
                    "flat" or "1" => 1,
                    "otel" or "2" => 2,
                    "method" or "0" => 0,
                    _ => (int?)null,
                };
            });

        var tagNaming = msBuildTagNaming.Combine(assemblyTagNaming)
            .Select(static (pair, _) => pair.Left ?? pair.Right ?? 0);

        var assemblyDepth = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                ConfigAttributeFqn,
                predicate: static (node, _) => node is CompilationUnitSyntax,
                transform: static (ctx, _) =>
                {
                    var attr = ctx.Attributes.FirstOrDefault(a =>
                        a.AttributeClass?.ToDisplayString() == ConfigAttributeFqn);
                    if (attr is null) return (int?)null;
                    foreach (var arg in attr.NamedArguments)
                    {
                        if (arg.Key == "Depth" && arg.Value.Value is int val)
                            return val;
                    }
                    return null;
                })
            .Where(static x => x is not null)
            .Collect()
            .Select(static (arr, _) => arr.IsDefaultOrEmpty ? (int?)null : arr[0]);

        var msBuildDepth = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) =>
            {
                provider.GlobalOptions.TryGetValue(MsBuildDepthPropertyName, out var value);
                if (string.IsNullOrWhiteSpace(value)) return (int?)null;
                return int.TryParse(value!.Trim(), out var d) ? d : (int?)null;
            });

        var resolvedDepth = msBuildDepth.Combine(assemblyDepth)
            .Select(static (pair, _) => pair.Left ?? pair.Right ?? DefaultDepth);

        var combined = collectedMethods
            .Combine(collectedCallSites)
            .Combine(defaultName)
            .Combine(tagNaming)
            .Combine(resolvedDepth);

        context.RegisterSourceOutput(combined, static (spc, pair) =>
        {
            var ((((methods, sites), resolvedDefault), resolvedTagNaming), resolvedGlobalDepth) = pair;
            if (methods.IsDefaultOrEmpty && sites.IsDefaultOrEmpty) return;
            EmitCombinedFile(spc, methods, sites, resolvedDefault, resolvedTagNaming, resolvedGlobalDepth);
        });
    }

    private static (string? spanName, string? activitySourceName, string[] skip, string[] fields,
        bool recordReturnValue, bool recordException, int kind, bool recordSuccess,
        bool ignoreCancellation, string? condition, string? linkTo, int depth) ParseInstrumentAttribute(AttributeData attr)
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

        return (spanName, activitySourceName, skip, fields, recordReturnValue, recordException, kind,
            recordSuccess, ignoreCancellation, condition, linkTo, depth);
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

        var parsed = ParseInstrumentAttribute(attr);

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

        var methodDepth = parsed.depth >= 0 ? parsed.depth : DefaultDepth;
        var parameters = method.Parameters.Select(p => ExtractParameterInfo(p, refKind: p.RefKind switch
            {
                RefKind.Ref => "ref",
                RefKind.Out => "out",
                RefKind.In => "in",
                RefKind.RefReadOnlyParameter => "in",
                _ => null
            }, depth: methodDepth, skip: parsed.skip, fields: parsed.fields)).ToArray();

        var tagMembers = ExtractTagMembers(containingType);

        return new InstrumentedMethodInfo
        {
            Namespace = containingType.ContainingNamespace?.IsGlobalNamespace == true
                ? "" : containingType.ContainingNamespace?.ToDisplayString() ?? "",
            ClassName = containingType.Name,
            MethodName = method.Name,
            FullyQualifiedClassName = containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
            SpanName = parsed.spanName,
            AssemblyName = containingType.ContainingAssembly?.Name ?? "Unknown",
            ActivitySourceName = parsed.activitySourceName,
            ReturnType = returnTypeStr,
            IsAsync = isAsync,
            ReturnsVoid = returnsVoid,
            IsAwaitable = isAwaitable,
            HasGenericReturn = hasGenericReturn,
            InnerReturnType = innerReturnType,
            IsStatic = method.IsStatic,
            IsExtensionMethod = method.IsExtensionMethod,
            Parameters = new EquatableArray<ParameterInfo>(parameters),
            Skip = new EquatableArray<string>(parsed.skip),
            Fields = new EquatableArray<string>(parsed.fields),
            RecordReturnValue = parsed.recordReturnValue,
            RecordException = parsed.recordException,
            Kind = parsed.kind,
            RecordSuccess = parsed.recordSuccess,
            IgnoreCancellation = parsed.ignoreCancellation,
            Condition = parsed.condition,
            LinkTo = parsed.linkTo,
            TagMembers = new EquatableArray<TagMemberInfo>(tagMembers),
            Depth = parsed.depth,
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
        
        var location = ctx.SemanticModel.GetInterceptableLocation(invocation);
        if (location is null) return null;

        var attr = targetMethod.GetAttributes().First(a =>
            a.AttributeClass?.ToDisplayString() == AttributeFqn);

        var parsed = ParseInstrumentAttribute(attr);

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

        var callSiteDepth = parsed.depth >= 0 ? parsed.depth : DefaultDepth;
        var parameters = calledMethod.Parameters.Select(p => ExtractParameterInfo(p, refKind: p.RefKind switch
            {
                RefKind.Ref => "ref",
                RefKind.Out => "out",
                RefKind.In => "in",
                _ => null
            }, depth: callSiteDepth, skip: parsed.skip, fields: parsed.fields)).ToArray();

        var tagMembers = containingType is not null ? ExtractTagMembers(containingType) : System.Array.Empty<TagMemberInfo>();

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
            SpanName = parsed.spanName,
            AssemblyName = containingType?.ContainingAssembly?.Name ?? "Unknown",
            ActivitySourceName = parsed.activitySourceName,
            ReturnType = returnTypeStr,
            IsAsync = isAsync,
            ReturnsVoid = returnsVoid,
            IsAwaitable = isAwaitable,
            HasGenericReturn = hasGenericReturn,
            InnerReturnType = innerReturnType,
            IsStatic = calledMethod.IsStatic,
            IsExtensionMethod = calledMethod.IsExtensionMethod,
            Parameters = new EquatableArray<ParameterInfo>(parameters),
            Skip = new EquatableArray<string>(parsed.skip),
            Fields = new EquatableArray<string>(parsed.fields),
            RecordReturnValue = parsed.recordReturnValue,
            RecordException = parsed.recordException,
            Kind = parsed.kind,
            RecordSuccess = parsed.recordSuccess,
            IgnoreCancellation = parsed.ignoreCancellation,
            Condition = parsed.condition,
            LinkTo = parsed.linkTo,
            TagMembers = new EquatableArray<TagMemberInfo>(tagMembers),
            Depth = parsed.depth,
        };

        return new InterceptCallSite
        {
            Method = methodInfo,
            LocationVersion = location.Version,
            LocationData = location.Data,
            DisplayLocation = location.GetDisplayLocation(),
            ReceiverType = receiverType,
            IsStaticCall = isStaticCall,
        };
    }

    private static void EmitCombinedFile(
        SourceProductionContext spc,
        ImmutableArray<InstrumentedMethodInfo?> methods,
        ImmutableArray<InterceptCallSite?> sites,
        string? defaultActivitySourceName = null,
        int tagNamingConvention = 0,
        int globalDepth = DefaultDepth)
    {
        {
            methods = methods.Select(m =>
            {
                if (m is null) return m;
                var updated = m;
                if (defaultActivitySourceName is not null && m.DefaultActivitySourceName is null)
                    updated = updated with { DefaultActivitySourceName = defaultActivitySourceName };
                if (tagNamingConvention != 0)
                    updated = updated with { TagNamingConvention = tagNamingConvention };
                if (m.Depth < 0)
                    updated = updated with { Depth = globalDepth };
                return updated;
            }).ToImmutableArray();

            sites = sites.Select(s =>
            {
                if (s is null) return s;
                var updatedMethod = s.Method;
                if (defaultActivitySourceName is not null && s.Method.DefaultActivitySourceName is null)
                    updatedMethod = updatedMethod with { DefaultActivitySourceName = defaultActivitySourceName };
                if (tagNamingConvention != 0)
                    updatedMethod = updatedMethod with { TagNamingConvention = tagNamingConvention };
                if (s.Method.Depth < 0)
                    updatedMethod = updatedMethod with { Depth = globalDepth };
                return updatedMethod == s.Method ? s : s with { Method = updatedMethod };
            }).ToImmutableArray();
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

                var callExpr = isInstance ? $"__self.{method.MethodName}({argStr})" : $"{method.FullyQualifiedClassName}.{method.MethodName}({argStr})";
                var awaitExpr = method.IsAwaitable ? "await " : "";

                if (method.Condition is not null)
                {
                    var condAccess = isInstance ? $"__self.{method.Condition}" : $"{method.FullyQualifiedClassName}.{method.Condition}";
                    sb.AppendLine($"            if (!{condAccess})");
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
                }

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

                if (method.LinkTo is not null)
                {
                    sb.AppendLine($"            var __links = new[] {{ new global::System.Diagnostics.ActivityLink({method.LinkTo}) }};");
                    sb.AppendLine($"            using var __activity = ActivitySources.{sourceField}");
                    sb.AppendLine($"                .StartActivity(\"{Escape(spanName)}\", {activityKind}, default(global::System.Diagnostics.ActivityContext), links: __links);");
                }
                else
                {
                    sb.AppendLine($"            using var __activity = ActivitySources.{sourceField}");
                    sb.AppendLine($"                .StartActivity(\"{Escape(spanName)}\", {activityKind});");
                }
                sb.AppendLine();

                var taggable = method.Parameters
                    .Where(p => ShouldTagParameter(p, method) && p.RefKind != "out")
                    .ToList();

                var tagMembers = method.TagMembers.Where(_ => isInstance).ToList();

                if (taggable.Count > 0 || tagMembers.Count > 0)
                {
                    sb.AppendLine("            if (__activity is not null)");
                    sb.AppendLine("            {");
                    foreach (var p in taggable)
                    {
                        if (p.IsComplex && p.Properties.Length > 0)
                        {
                            EmitPropertyTags(sb, p.Name, p.Name, p.Properties,
                                method.Skip, method.Fields, p.NoInstrumentProperties,
                                method.MethodName, method.TagNamingConvention);
                        }
                        else
                        {
                            var tagName = FormatTagName(method.MethodName, p.Name, null, method.TagNamingConvention);
                            sb.AppendLine($"                __activity.SetTag(\"{Escape(tagName)}\", {p.Name});");
                        }
                    }
                    foreach (var tm in tagMembers)
                    {
                        if (tm.IsComplex && tm.Properties.Length > 0)
                        {
                            var baseName = tm.TagName ?? tm.MemberName.ToLowerInvariant();
                            EmitTagMemberPropertyTags(sb, $"__self.{tm.MemberName}", baseName,
                                tm.Properties, tm.Skip, tm.Fields);
                        }
                        else
                        {
                            var tagName = tm.TagName ?? tm.MemberName.ToLowerInvariant();
                            sb.AppendLine($"                __activity.SetTag(\"{Escape(tagName)}\", __self.{tm.MemberName});");
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
                        if (method.RecordSuccess)
                        {
                            sb.AppendLine("                __activity?.SetStatus(global::System.Diagnostics.ActivityStatusCode.Ok);");
                        }
                    }
                    else
                    {
                        sb.AppendLine($"                var __result = {awaitExpr}{callExpr};");
                        if (method.RecordReturnValue)
                        {
                            sb.AppendLine($"                __activity?.SetTag(\"{method.MethodName.ToLowerInvariant()}.return_value\", __result?.ToString());");
                        }
                        if (method.RecordSuccess)
                        {
                            sb.AppendLine("                __activity?.SetStatus(global::System.Diagnostics.ActivityStatusCode.Ok);");
                        }
                        sb.AppendLine("                return __result;");
                    }

                    sb.AppendLine("            }");
                    sb.AppendLine("            catch (global::System.Exception __ex)");
                    sb.AppendLine("            {");

                    if (method.IgnoreCancellation)
                    {
                        sb.AppendLine("                if (__ex is not global::System.OperationCanceledException)");
                        sb.AppendLine("                {");
                        sb.AppendLine("                    if (__activity is not null)");
                        sb.AppendLine("                    {");
                        sb.AppendLine("                        __activity.SetStatus(global::System.Diagnostics.ActivityStatusCode.Error, __ex.Message);");
                        sb.AppendLine("                        __activity.AddEvent(new global::System.Diagnostics.ActivityEvent(\"exception\",");
                        sb.AppendLine("                            tags: new global::System.Diagnostics.ActivityTagsCollection");
                        sb.AppendLine("                            {");
                        sb.AppendLine("                                { \"exception.type\", __ex.GetType().FullName },");
                        sb.AppendLine("                                { \"exception.message\", __ex.Message },");
                        sb.AppendLine("                                { \"exception.stacktrace\", __ex.ToString() }");
                        sb.AppendLine("                            }));");
                        sb.AppendLine("                    }");
                        sb.AppendLine("                }");
                    }
                    else
                    {
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
                    }

                    sb.AppendLine("                throw;");
                    sb.AppendLine("            }");
                }
                else
                {
                    if (method.ReturnsVoid || (!method.HasGenericReturn && method.IsAwaitable))
                    {
                        sb.AppendLine($"            {awaitExpr}{callExpr};");
                        if (method.RecordSuccess)
                        {
                            sb.AppendLine("            __activity?.SetStatus(global::System.Diagnostics.ActivityStatusCode.Ok);");
                        }
                    }
                    else
                    {
                        if (method.RecordReturnValue)
                        {
                            sb.AppendLine($"            var __result = {awaitExpr}{callExpr};");
                            sb.AppendLine($"            __activity?.SetTag(\"{method.MethodName.ToLowerInvariant()}.return_value\", __result?.ToString());");
                            if (method.RecordSuccess)
                            {
                                sb.AppendLine("            __activity?.SetStatus(global::System.Diagnostics.ActivityStatusCode.Ok);");
                            }
                            sb.AppendLine("            return __result;");
                        }
                        else
                        {
                            if (method.RecordSuccess)
                            {
                                sb.AppendLine($"            var __result = {awaitExpr}{callExpr};");
                                sb.AppendLine("            __activity?.SetStatus(global::System.Diagnostics.ActivityStatusCode.Ok);");
                                sb.AppendLine("            return __result;");
                            }
                            else
                            {
                                sb.AppendLine($"            return {awaitExpr}{callExpr};");
                            }
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

    private static bool ShouldTagProperty(string dotPath, string paramName, EquatableArray<string> skip, EquatableArray<string> fields,
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

    private static void EmitPropertyTags(StringBuilder sb, string accessPrefix, string paramName,
        EquatableArray<PropertyMetadata> properties, EquatableArray<string> skip, EquatableArray<string> fields,
        EquatableArray<string> noInstrumentProperties, string methodName, int convention,
        System.Collections.Generic.List<string>? pathSegments = null)
    {
        pathSegments ??= new System.Collections.Generic.List<string>();
        foreach (var prop in properties)
        {
            pathSegments.Add(prop.Name);
            var dotPath = $"{paramName}.{string.Join(".", pathSegments)}";
            var access = $"{accessPrefix}?.{prop.Name}";

            if (prop.IsComplex && prop.Properties.Length > 0)
            {
                if (ShouldTagProperty(dotPath, paramName, skip, fields, noInstrumentProperties))
                {
                    EmitPropertyTags(sb, access, paramName, prop.Properties, skip, fields,
                        noInstrumentProperties, methodName, convention, pathSegments);
                }
            }
            else
            {
                if (ShouldTagProperty(dotPath, paramName, skip, fields, noInstrumentProperties))
                {
                    var tagName = FormatTagName(methodName, paramName, pathSegments.ToArray(), convention);
                    sb.AppendLine($"                __activity.SetTag(\"{Escape(tagName)}\", {access});");
                }
            }
            pathSegments.RemoveAt(pathSegments.Count - 1);
        }
    }

    private static void EmitTagMemberPropertyTags(StringBuilder sb, string accessPrefix, string namePrefix,
        EquatableArray<PropertyMetadata> properties, EquatableArray<string> skip, EquatableArray<string> fields,
        System.Collections.Generic.List<string>? pathSegments = null)
    {
        pathSegments ??= new System.Collections.Generic.List<string>();
        foreach (var prop in properties)
        {
            pathSegments.Add(prop.Name);
            var dotPath = string.Join(".", pathSegments);
            var access = $"{accessPrefix}?.{prop.Name}";

            if (prop.IsComplex && prop.Properties.Length > 0)
            {
                if (ShouldTagMemberProperty(dotPath, skip, fields))
                {
                    EmitTagMemberPropertyTags(sb, access, namePrefix, prop.Properties, skip, fields, pathSegments);
                }
            }
            else
            {
                if (ShouldTagMemberProperty(dotPath, skip, fields))
                {
                    var tagName = $"{namePrefix}.{string.Join(".", pathSegments.Select(s => s.ToLowerInvariant()))}";
                    sb.AppendLine($"                __activity.SetTag(\"{Escape(tagName)}\", {access});");
                }
            }
            pathSegments.RemoveAt(pathSegments.Count - 1);
        }
    }

    private static bool ShouldTagMemberProperty(string dotPath, EquatableArray<string> skip, EquatableArray<string> fields)
    {
        if (fields.Length > 0)
            return fields.Any(f => string.Equals(f, dotPath, System.StringComparison.OrdinalIgnoreCase)
                || f.StartsWith(dotPath + ".", System.StringComparison.OrdinalIgnoreCase));
        if (skip.Length > 0)
            return !skip.Any(s => string.Equals(s, dotPath, System.StringComparison.OrdinalIgnoreCase));
        return true;
    }

    private static PropertyMetadata[] ExtractProperties(ITypeSymbol type, int remainingDepth,
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

    private static ParameterInfo ExtractParameterInfo(IParameterSymbol p, string? refKind,
        int depth = DefaultDepth, string[]? skip = null, string[]? fields = null)
    {
        var type = p.Type;
        var typeStr = type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        bool isComplex = IsComplexType(type);

        string[]? forcedPaths = null;
        if (isComplex)
        {
            var allPaths = (skip ?? System.Array.Empty<string>())
                .Concat(fields ?? System.Array.Empty<string>())
                .Where(f => f.StartsWith(p.Name + ".", System.StringComparison.OrdinalIgnoreCase))
                .Select(f => f.Substring(p.Name.Length + 1))
                .ToArray();
            if (allPaths.Length > 0) forcedPaths = allPaths;
        }

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

    private static TagMemberInfo[] ExtractTagMembers(INamedTypeSymbol containingType)
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

            string[]? forcedPaths = null;
            if (isComplex)
            {
                var allPaths = (skip ?? System.Array.Empty<string>())
                    .Concat(fields ?? System.Array.Empty<string>())
                    .ToArray();
                if (allPaths.Length > 0) forcedPaths = allPaths;
            }

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

    private static string FormatTagName(string methodName, string paramName, string[]? propPath, int convention)
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

    private static bool IsComplexType(ITypeSymbol type)
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

    private static bool IsCollectionType(ITypeSymbol type)
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
