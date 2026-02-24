using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace AutoInstrument.Generator;

/// <summary>Incremental generator that emits interceptors for [Instrument]-decorated methods.</summary>
[Generator]
public sealed class InstrumentGenerator : IIncrementalGenerator
{
    private const string AttributeFqn = "AutoInstrument.InstrumentAttribute";
    private const string SourceAttributeFqn = "AutoInstrument.AutoInstrumentSourceAttribute";
    private const string ConfigAttributeFqn = "AutoInstrument.AutoInstrumentConfigAttribute";
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
            CodeEmitter.EmitCombinedFile(spc, methods, sites, resolvedDefault, resolvedTagNaming, resolvedGlobalDepth);
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

        var parsed = TypeAnalysis.ParseInstrumentAttribute(attr);
        var returnInfo = TypeAnalysis.AnalyzeReturnType(method.ReturnType);
        bool isAsync = (ctx.TargetNode as MethodDeclarationSyntax)?.Modifiers.Any(SyntaxKind.AsyncKeyword) == true;

        var methodDepth = parsed.Depth >= 0 ? parsed.Depth : DefaultDepth;
        var parameters = method.Parameters.Select(p => TypeAnalysis.ExtractParameterInfo(p, refKind: p.RefKind switch
            {
                RefKind.Ref => "ref",
                RefKind.Out => "out",
                RefKind.In => "in",
                RefKind.RefReadOnlyParameter => "in",
                _ => null
            }, depth: methodDepth, skip: parsed.Skip, fields: parsed.Fields)).ToArray();

        var tagMembers = TypeAnalysis.ExtractTagMembers(containingType);

        return TypeAnalysis.BuildMethodInfo(method, containingType, parsed, returnInfo, isAsync, parameters, tagMembers);
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

        var parsed = TypeAnalysis.ParseInstrumentAttribute(attr);
        var returnInfo = TypeAnalysis.AnalyzeReturnType(calledMethod.ReturnType);

        var containingType = targetMethod.ContainingType;

        var callSiteDepth = parsed.Depth >= 0 ? parsed.Depth : DefaultDepth;
        var parameters = calledMethod.Parameters.Select(p => TypeAnalysis.ExtractParameterInfo(p, refKind: p.RefKind switch
            {
                RefKind.Ref => "ref",
                RefKind.Out => "out",
                RefKind.In => "in",
                _ => null
            }, depth: callSiteDepth, skip: parsed.Skip, fields: parsed.Fields)).ToArray();

        var tagMembers = containingType is not null ? TypeAnalysis.ExtractTagMembers(containingType) : System.Array.Empty<TagMemberInfo>();

        string receiverType = "";
        bool isStaticCall = calledMethod.IsStatic;
        if (!isStaticCall && containingType != null)
        {
            receiverType = containingType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        }

        var methodInfo = TypeAnalysis.BuildMethodInfo(calledMethod, containingType, parsed, returnInfo,
            isAsync: false, parameters, tagMembers);

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
}
