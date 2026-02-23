using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace AutoInstrument.Generator;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InstrumentAnalyzer : DiagnosticAnalyzer
{
    private const string AttributeFqn = "AutoInstrument.InstrumentAttribute";

    public static readonly DiagnosticDescriptor InvalidSkipParameter = new(
        id: "AUTOINST001",
        title: "Invalid parameter name in Skip",
        messageFormat: "'{0}' in Skip is not a parameter of '{1}'",
        category: "AutoInstrument",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidFieldsParameter = new(
        id: "AUTOINST002",
        title: "Invalid parameter name in Fields",
        messageFormat: "'{0}' in Fields is not a parameter of '{1}'",
        category: "AutoInstrument",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        ImmutableArray.Create(InvalidSkipParameter, InvalidFieldsParameter);

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
    }

    private static void AnalyzeMethod(SymbolAnalysisContext context)
    {
        if (context.Symbol is not IMethodSymbol method) return;

        var attr = method.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == AttributeFqn);
        if (attr is null) return;

        var paramNames = new HashSet<string>(method.Parameters.Select(p => p.Name));
        var methodName = $"{method.ContainingType?.Name}.{method.Name}";

        foreach (var arg in attr.NamedArguments)
        {
            DiagnosticDescriptor? descriptor = arg.Key switch
            {
                "Skip" => InvalidSkipParameter,
                "Fields" => InvalidFieldsParameter,
                _ => null
            };
            if (descriptor is null) continue;

            foreach (var value in arg.Value.Values)
            {
                if (value.Value is string name && !paramNames.Contains(name))
                {
                    var location = attr.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                        ?? Location.None;
                    context.ReportDiagnostic(Diagnostic.Create(descriptor, location, name, methodName));
                }
            }
        }
    }
}
