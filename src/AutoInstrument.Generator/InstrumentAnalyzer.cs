using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace AutoInstrument.Generator;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InstrumentAnalyzer : DiagnosticAnalyzer
{
    private const string AttributeFqn = "AutoInstrument.InstrumentAttribute";
    private const string NoInstrumentAttributeFqn = "AutoInstrument.NoInstrumentAttribute";

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

    public static readonly DiagnosticDescriptor InvalidCondition = new(
        id: "AUTOINST004",
        title: "Invalid Condition member",
        messageFormat: "'{0}' is not a boolean property or field on '{1}'",
        category: "AutoInstrument",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidLinkTo = new(
        id: "AUTOINST005",
        title: "Invalid LinkTo parameter",
        messageFormat: "'{0}' is not a parameter of type ActivityContext on '{1}'",
        category: "AutoInstrument",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidNoInstrumentProperty = new(
        id: "AUTOINST006",
        title: "Invalid property in [NoInstrument]",
        messageFormat: "'{0}' is not a public property of parameter '{1}' (type '{2}') in '{3}'",
        category: "AutoInstrument",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [InvalidSkipParameter, InvalidFieldsParameter, InvalidPropertyPath,
         InvalidCondition, InvalidLinkTo, InvalidNoInstrumentProperty];

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
        var location = attr.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
            ?? Location.None;

        foreach (var arg in attr.NamedArguments)
        {
            switch (arg.Key)
            {
                case "Skip":
                case "Fields":
                {
                    var paramDescriptor = arg.Key == "Skip" ? InvalidSkipParameter : InvalidFieldsParameter;
                    foreach (var value in arg.Value.Values)
                    {
                        if (value.Value is not string name) continue;

                        var dotIndex = name.IndexOf('.');
                        var paramName = dotIndex >= 0 ? name.Substring(0, dotIndex) : name;
                        var propertyName = dotIndex >= 0 ? name.Substring(dotIndex + 1) : null;

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
                    break;
                }

                case "Condition":
                {
                    if (arg.Value.Value is not string conditionName) break;
                    var containingType = method.ContainingType;
                    if (containingType is null) break;

                    var isBoolMember = containingType.GetMembers(conditionName)
                        .Any(m => m switch
                        {
                            IPropertySymbol p => p.Type.SpecialType == SpecialType.System_Boolean,
                            IFieldSymbol f => f.Type.SpecialType == SpecialType.System_Boolean,
                            _ => false,
                        });

                    if (!isBoolMember)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            InvalidCondition, location, conditionName, containingType.Name));
                    }
                    break;
                }

                case "LinkTo":
                {
                    if (arg.Value.Value is not string linkToName) break;

                    if (!paramsByName.TryGetValue(linkToName, out var linkParam))
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            InvalidLinkTo, location, linkToName, methodName));
                        break;
                    }

                    var typeName = linkParam.Type.ToDisplayString();
                    if (typeName != "System.Diagnostics.ActivityContext")
                    {
                        context.ReportDiagnostic(Diagnostic.Create(
                            InvalidLinkTo, location, linkToName, methodName));
                    }
                    break;
                }
            }
        }

        // Validate [NoInstrument] property names on parameters
        foreach (var param in method.Parameters)
        {
            var noInstrAttr = param.GetAttributes().FirstOrDefault(a =>
                a.AttributeClass?.ToDisplayString() == NoInstrumentAttributeFqn);
            if (noInstrAttr is null) continue;

            if (noInstrAttr.ConstructorArguments.Length > 0)
            {
                var ctorArg = noInstrAttr.ConstructorArguments[0];
                if (ctorArg.Kind == TypedConstantKind.Array)
                {
                    foreach (var propVal in ctorArg.Values)
                    {
                        if (propVal.Value is not string propName) continue;

                        var hasProperty = param.Type.GetMembers()
                            .OfType<IPropertySymbol>()
                            .Any(p => string.Equals(p.Name, propName, System.StringComparison.OrdinalIgnoreCase)
                                      && p is { DeclaredAccessibility: Accessibility.Public, IsStatic: false, GetMethod: not null });

                        if (!hasProperty)
                        {
                            var paramLocation = noInstrAttr.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
                                ?? location;
                            var typeName = param.Type.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                            context.ReportDiagnostic(Diagnostic.Create(
                                InvalidNoInstrumentProperty, paramLocation, propName, param.Name, typeName, methodName));
                        }
                    }
                }
            }
        }
    }
}
