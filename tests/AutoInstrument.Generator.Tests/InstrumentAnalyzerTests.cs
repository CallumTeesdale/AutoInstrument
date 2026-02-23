using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Testing;
using Microsoft.CodeAnalysis.Testing;
using Xunit;

namespace AutoInstrument.Generator.Tests;

public class InstrumentAnalyzerTests
{
    [Fact]
    public async Task Skip_InvalidParameterName_ReportsDiagnostic()
    {
        var source = """
            using AutoInstrument;

            public class MyService
            {
                [{|#0:Instrument(Skip = new[] { "pwd" })|}]
                public void Login(string username, string password) { }
            }
            """;

        var test = CreateTest(source);
        test.ExpectedDiagnostics.Add(new DiagnosticResult(InstrumentAnalyzer.InvalidSkipParameter)
            .WithLocation(0)
            .WithArguments("pwd", "MyService.Login"));
        await test.RunAsync();
    }

    [Fact]
    public async Task Skip_ValidParameterName_NoDiagnostic()
    {
        var source = """
            using AutoInstrument;

            public class MyService
            {
                [Instrument(Skip = new[] { "password" })]
                public void Login(string username, string password) { }
            }
            """;

        var test = CreateTest(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task Fields_InvalidParameterName_ReportsDiagnostic()
    {
        var source = """
            using AutoInstrument;

            public class MyService
            {
                [{|#0:Instrument(Fields = new[] { "foo" })|}]
                public void Process(int id, string name) { }
            }
            """;

        var test = CreateTest(source);
        test.ExpectedDiagnostics.Add(new DiagnosticResult(InstrumentAnalyzer.InvalidFieldsParameter)
            .WithLocation(0)
            .WithArguments("foo", "MyService.Process"));
        await test.RunAsync();
    }

    [Fact]
    public async Task Fields_ValidParameterName_NoDiagnostic()
    {
        var source = """
            using AutoInstrument;

            public class MyService
            {
                [Instrument(Fields = new[] { "id" })]
                public void Process(int id, string name) { }
            }
            """;

        var test = CreateTest(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task MixedValidAndInvalid_ReportsOnlyInvalid()
    {
        var source = """
            using AutoInstrument;

            public class MyService
            {
                [{|#0:Instrument(Skip = new[] { "name", "typo" })|}]
                public void Process(int id, string name) { }
            }
            """;

        var test = CreateTest(source);
        test.ExpectedDiagnostics.Add(new DiagnosticResult(InstrumentAnalyzer.InvalidSkipParameter)
            .WithLocation(0)
            .WithArguments("typo", "MyService.Process"));
        await test.RunAsync();
    }

    private static CSharpAnalyzerTest<InstrumentAnalyzer, DefaultVerifier> CreateTest(string source)
    {
        var test = new CSharpAnalyzerTest<InstrumentAnalyzer, DefaultVerifier>
        {
            TestCode = source,
            ReferenceAssemblies = ReferenceAssemblies.Net.Net80,
        };
        test.TestState.AdditionalReferences.Add(typeof(InstrumentAttribute).Assembly.Location);
        return test;
    }
}
