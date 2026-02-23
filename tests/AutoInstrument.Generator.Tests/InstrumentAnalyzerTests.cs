using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace AutoInstrument.Generator.Tests;

public class InstrumentAnalyzerTests
{
    [Fact]
    public async Task PartialClass_NoDiagnostic()
    {
        var source = """
            using AutoInstrument;

            public partial class MyService
            {
                [Instrument]
                public void DoWork() { }
            }
            """;

        await RunAnalyzerTest(source);
    }

    [Fact]
    public async Task NonPartialClass_NoDiagnostic()
    {
        var source = """
            using AutoInstrument;

            public class MyService
            {
                [Instrument]
                public void DoWork() { }
            }
            """;

        await RunAnalyzerTest(source);
    }

    private static async Task RunAnalyzerTest(string source, params DiagnosticResult[] expected)
    {
        var test = new CSharpAnalyzerTest<InstrumentAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net90,
        };

        // Add reference to the attributes assembly
        test.TestState.AdditionalReferences.Add(typeof(InstrumentAttribute).Assembly.Location);

        test.ExpectedDiagnostics.AddRange(expected);
        await test.RunAsync();
    }
}
