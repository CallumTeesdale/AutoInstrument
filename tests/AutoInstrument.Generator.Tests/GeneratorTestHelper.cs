using System.Collections.Immutable;
using System.Reflection;
using AutoInstrument.Generator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

namespace AutoInstrument.Generator.Tests;

internal static class GeneratorTestHelper
{
    /// <summary>
    /// Runs the InstrumentGenerator on the given source code and returns all generated source texts.
    /// </summary>
    internal static ImmutableArray<(string HintName, string Source)> RunGenerator(
        string source,
        string? autoInstrumentSourceName = null)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var references = GetMetadataReferences();

        var compilation = CSharpCompilation.Create(
            assemblyName: "TestAssembly",
            syntaxTrees: [syntaxTree],
            references: references,
            options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));

        var generator = new InstrumentGenerator();

        var optionsProvider = autoInstrumentSourceName is not null
            ? new TestAnalyzerConfigOptionsProvider(autoInstrumentSourceName)
            : null;

        GeneratorDriver driver = CSharpGeneratorDriver.Create(
            generators: [generator.AsSourceGenerator()],
            optionsProvider: optionsProvider);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var outputCompilation, out var diagnostics);

        var runResult = driver.GetRunResult();

        return runResult.GeneratedTrees
            .Select(t => (
                HintName: Path.GetFileName(t.FilePath),
                Source: t.GetText().ToString()))
            .ToImmutableArray();
    }

    /// <summary>
    /// Collects the metadata references needed for compilation:
    /// core runtime, System.Diagnostics.DiagnosticSource, and AutoInstrument.Attributes.
    /// </summary>
    private static MetadataReference[] GetMetadataReferences()
    {
        var refs = new List<MetadataReference>();

        // Core runtime references from the running process
        var trustedAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (trustedAssemblies != null)
        {
            foreach (var path in trustedAssemblies.Split(Path.PathSeparator))
            {
                var fileName = Path.GetFileName(path);
                if (fileName is "System.Runtime.dll"
                    or "System.Private.CoreLib.dll"
                    or "netstandard.dll"
                    or "System.Collections.dll"
                    or "System.Linq.dll"
                    or "System.Threading.Tasks.dll"
                    or "System.Threading.dll"
                    or "System.Diagnostics.DiagnosticSource.dll")
                {
                    refs.Add(MetadataReference.CreateFromFile(path));
                }
            }
        }

        // AutoInstrument.Attributes
        var attributesAssembly = typeof(InstrumentAttribute).Assembly.Location;
        if (!string.IsNullOrEmpty(attributesAssembly))
        {
            refs.Add(MetadataReference.CreateFromFile(attributesAssembly));
        }

        return refs.ToArray();
    }

    private sealed class TestAnalyzerConfigOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly TestGlobalOptions _globalOptions;

        public TestAnalyzerConfigOptionsProvider(string autoInstrumentSourceName)
        {
            _globalOptions = new TestGlobalOptions(autoInstrumentSourceName);
        }

        public override AnalyzerConfigOptions GlobalOptions => _globalOptions;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => EmptyOptions.Instance;
        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) => EmptyOptions.Instance;

        private sealed class TestGlobalOptions : AnalyzerConfigOptions
        {
            private readonly string _autoInstrumentSourceName;

            public TestGlobalOptions(string autoInstrumentSourceName)
            {
                _autoInstrumentSourceName = autoInstrumentSourceName;
            }

            public override bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value)
            {
                if (key == "build_property.AutoInstrumentSourceName")
                {
                    value = _autoInstrumentSourceName;
                    return true;
                }
                value = null;
                return false;
            }
        }

        private sealed class EmptyOptions : AnalyzerConfigOptions
        {
            public static readonly EmptyOptions Instance = new();
            public override bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value)
            {
                value = null;
                return false;
            }
        }
    }
}
