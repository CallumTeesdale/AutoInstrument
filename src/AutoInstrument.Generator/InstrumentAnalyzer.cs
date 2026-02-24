using System.Collections.Immutable;
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

    public static readonly DiagnosticDescriptor InvalidPropertyPath = new(
        id: "AUTOINST003",
        title: "Invalid property in dot-notation path",
        messageFormat: "'{0}' is not a public property of parameter '{1}' (type '{2}') in '{3}'",
        category: "AutoInstrument",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [InvalidSkipParameter, InvalidFieldsParameter, InvalidPropertyPath];

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

        var paramsByName = method.Parameters.ToDictionary(p => p.Name, p => p);
        var methodName = $"{method.ContainingType?.Name}.{method.Name}";

        foreach (var arg in attr.NamedArguments)
        {
            DiagnosticDescriptor? paramDescriptor = arg.Key switch
            {
                "Skip" => InvalidSkipParameter,
                "Fields" => InvalidFieldsParameter,
                _ => null
            };
            if (paramDescriptor is null) continue;

            foreach (var value in arg.Value.Values)
            {
                if (value.Value is not string name) continue;

                var dotIndex = name.IndexOf('.');
                var paramName = dotIndex >= 0 ? name.Substring(0, dotIndex) : name;
                var propertyName = dotIndex >= 0 ? name.Substring(dotIndex + 1) : null;

                var location = attr.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                    ?? Location.None;

                if (!paramsByName.TryGetValue(paramName, out var param))
                {
                    context.ReportDiagnostic(Diagnostic.Create(paramDescriptor, location, name, methodName));
                    continue;
                }

                if (propertyName is not null)
                {
                    var paramType = param.Type;
                    var hasProperty = paramType.GetMembers()
                        .OfType<IPropertySymbol>()
                        .Any(p => p.Name == propertyName
                                  && p is { DeclaredAccessibility: Accessibility.Public, IsStatic: false, GetMethod: not null });

                    if (!hasProperty)
                    {
                        var typeName = paramType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                        context.ReportDiagnostic(Diagnostic.Create(
                            InvalidPropertyPath, location, propertyName, paramName, typeName, methodName));
                    }
                }
            }
        }
    }
}
