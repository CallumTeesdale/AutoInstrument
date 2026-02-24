using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace AutoInstrument.Generator;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class InstrumentAnalyzer : DiagnosticAnalyzer
{
    private const string AttributeFqn = "AutoInstrument.InstrumentAttribute";
    private const string NoInstrumentAttributeFqn = "AutoInstrument.NoInstrumentAttribute";
    private const string TagAttributeFqn = "AutoInstrument.TagAttribute";

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

    public static readonly DiagnosticDescriptor InvalidTagSkipProperty = new(
        id: "AUTOINST007",
        title: "Invalid property in [Tag] Skip",
        messageFormat: "'{0}' is not a public property of '{1}' (type '{2}')",
        category: "AutoInstrument",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor InvalidTagFieldsProperty = new(
        id: "AUTOINST008",
        title: "Invalid property in [Tag] Fields",
        messageFormat: "'{0}' is not a public property of '{1}' (type '{2}')",
        category: "AutoInstrument",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [InvalidSkipParameter, InvalidFieldsParameter, InvalidPropertyPath,
         InvalidCondition, InvalidLinkTo, InvalidNoInstrumentProperty,
         InvalidTagSkipProperty, InvalidTagFieldsProperty];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterSymbolAction(AnalyzeMethod, SymbolKind.Method);
        context.RegisterSymbolAction(AnalyzeTagMember, SymbolKind.Property, SymbolKind.Field);
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
                        var propertyPath = dotIndex >= 0 ? name.Substring(dotIndex + 1) : null;

                        if (!paramsByName.TryGetValue(paramName, out var param))
                        {
                            context.ReportDiagnostic(Diagnostic.Create(paramDescriptor, location, name, methodName));
                            continue;
                        }

                        if (propertyPath is not null)
                        {
                            ValidatePropertyPath(context, param.Type, paramName, propertyPath, methodName, location);
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

    private static void AnalyzeTagMember(SymbolAnalysisContext context)
    {
        var attr = context.Symbol.GetAttributes().FirstOrDefault(a =>
            a.AttributeClass?.ToDisplayString() == TagAttributeFqn);
        if (attr is null) return;

        ITypeSymbol memberType;
        if (context.Symbol is IPropertySymbol prop)
            memberType = prop.Type;
        else if (context.Symbol is IFieldSymbol field)
            memberType = field.Type;
        else
            return;

        var location = attr.ApplicationSyntaxReference?.GetSyntax(context.CancellationToken).GetLocation()
            ?? Location.None;
        var memberName = context.Symbol.Name;
        var typeName = memberType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);

        foreach (var arg in attr.NamedArguments)
        {
            if (arg.Key is not ("Skip" or "Fields")) continue;

            foreach (var value in arg.Value.Values)
            {
                if (value.Value is not string propPath) continue;
                ValidateTagMemberPropertyPath(context, memberType, memberName, propPath, location,
                    arg.Key == "Skip" ? InvalidTagSkipProperty : InvalidTagFieldsProperty);
            }
        }
    }

    private static void ValidatePropertyPath(SymbolAnalysisContext context, ITypeSymbol rootType,
        string rootName, string propertyPath, string methodName, Location location)
    {
        var segments = propertyPath.Split('.');
        var currentType = rootType;
        var currentPath = rootName;

        foreach (var segment in segments)
        {
            currentPath = $"{currentPath}.{segment}";
            var prop = currentType.GetMembers()
                .OfType<IPropertySymbol>()
                .FirstOrDefault(p => string.Equals(p.Name, segment, System.StringComparison.OrdinalIgnoreCase)
                    && p is { DeclaredAccessibility: Accessibility.Public, IsStatic: false, GetMethod: not null });

            if (prop is null)
            {
                var typeName = currentType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                context.ReportDiagnostic(Diagnostic.Create(
                    InvalidPropertyPath, location, segment, currentPath.Substring(0, currentPath.LastIndexOf('.')), typeName, methodName));
                return;
            }

            currentType = prop.Type;
        }
    }

    private static void ValidateTagMemberPropertyPath(SymbolAnalysisContext context, ITypeSymbol rootType,
        string memberName, string propertyPath, Location location, DiagnosticDescriptor descriptor)
    {
        var segments = propertyPath.Split('.');
        var currentType = rootType;

        foreach (var segment in segments)
        {
            var prop = currentType.GetMembers()
                .OfType<IPropertySymbol>()
                .FirstOrDefault(p => string.Equals(p.Name, segment, System.StringComparison.OrdinalIgnoreCase)
                    && p is { DeclaredAccessibility: Accessibility.Public, IsStatic: false, GetMethod: not null });

            if (prop is null)
            {
                var typeName = currentType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
                context.ReportDiagnostic(Diagnostic.Create(
                    descriptor, location, segment, memberName, typeName));
                return;
            }

            currentType = prop.Type;
        }
    }
}
