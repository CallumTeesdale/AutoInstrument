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

    [Fact]
    public async Task DotNotation_ValidPropertyName_NoDiagnostic()
    {
        var source = """
            using AutoInstrument;

            public class Order
            {
                public int Id { get; set; }
                public string Name { get; set; }
            }

            public class MyService
            {
                [Instrument(Skip = new[] { "order.Name" })]
                public void Process(Order order) { }
            }
            """;

        var test = CreateTest(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task DotNotation_InvalidPropertyName_ReportsDiagnostic()
    {
        var source = """
            using AutoInstrument;

            public class Order
            {
                public int Id { get; set; }
            }

            public class MyService
            {
                [{|#0:Instrument(Skip = new[] { "order.Nonexistent" })|}]
                public void Process(Order order) { }
            }
            """;

        var test = CreateTest(source);
        test.ExpectedDiagnostics.Add(new DiagnosticResult(InstrumentAnalyzer.InvalidPropertyPath)
            .WithLocation(0)
            .WithArguments("Nonexistent", "order", "Order", "MyService.Process"));
        await test.RunAsync();
    }

    [Fact]
    public async Task DotNotation_InvalidParameterName_ReportsDiagnostic()
    {
        var source = """
            using AutoInstrument;

            public class MyService
            {
                [{|#0:Instrument(Fields = new[] { "foo.Bar" })|}]
                public void Process(int id) { }
            }
            """;

        var test = CreateTest(source);
        test.ExpectedDiagnostics.Add(new DiagnosticResult(InstrumentAnalyzer.InvalidFieldsParameter)
            .WithLocation(0)
            .WithArguments("foo.Bar", "MyService.Process"));
        await test.RunAsync();
    }

    [Fact]
    public async Task Condition_InvalidMember_ReportsDiagnostic()
    {
        var source = """
            using AutoInstrument;

            public class MyService
            {
                [{|#0:Instrument(Condition = "NotExist")|}]
                public void Process(int id) { }
            }
            """;

        var test = CreateTest(source);
        test.ExpectedDiagnostics.Add(new DiagnosticResult(InstrumentAnalyzer.InvalidCondition)
            .WithLocation(0)
            .WithArguments("NotExist", "MyService"));
        await test.RunAsync();
    }

    [Fact]
    public async Task Condition_ValidBoolProperty_NoDiagnostic()
    {
        var source = """
            using AutoInstrument;

            public class MyService
            {
                public bool IsEnabled { get; set; }

                [Instrument(Condition = "IsEnabled")]
                public void Process(int id) { }
            }
            """;

        var test = CreateTest(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task LinkTo_InvalidParameterName_ReportsDiagnostic()
    {
        var source = """
            using AutoInstrument;

            public class MyService
            {
                [{|#0:Instrument(LinkTo = "notAParam")|}]
                public void Process(int id) { }
            }
            """;

        var test = CreateTest(source);
        test.ExpectedDiagnostics.Add(new DiagnosticResult(InstrumentAnalyzer.InvalidLinkTo)
            .WithLocation(0)
            .WithArguments("notAParam", "MyService.Process"));
        await test.RunAsync();
    }

    [Fact]
    public async Task LinkTo_WrongType_ReportsDiagnostic()
    {
        var source = """
            using AutoInstrument;

            public class MyService
            {
                [{|#0:Instrument(LinkTo = "id")|}]
                public void Process(int id) { }
            }
            """;

        var test = CreateTest(source);
        test.ExpectedDiagnostics.Add(new DiagnosticResult(InstrumentAnalyzer.InvalidLinkTo)
            .WithLocation(0)
            .WithArguments("id", "MyService.Process"));
        await test.RunAsync();
    }

    [Fact]
    public async Task LinkTo_ValidActivityContext_NoDiagnostic()
    {
        var source = """
            using System.Diagnostics;
            using AutoInstrument;

            public class MyService
            {
                [Instrument(LinkTo = "ctx")]
                public void Process(int id, ActivityContext ctx) { }
            }
            """;

        var test = CreateTest(source);
        await test.RunAsync();
    }

    [Fact]
    public async Task NoInstrument_InvalidPropertyName_ReportsDiagnostic()
    {
        var source = """
            using AutoInstrument;

            public class Order
            {
                public int Id { get; set; }
            }

            public class MyService
            {
                [Instrument]
                public void Process([{|#0:NoInstrument("Nonexistent")|}] Order order) { }
            }
            """;

        var test = CreateTest(source);
        test.ExpectedDiagnostics.Add(new DiagnosticResult(InstrumentAnalyzer.InvalidNoInstrumentProperty)
            .WithLocation(0)
            .WithArguments("Nonexistent", "order", "Order", "MyService.Process"));
        await test.RunAsync();
    }

    [Fact]
    public async Task NoInstrument_ValidPropertyName_NoDiagnostic()
    {
        var source = """
            using AutoInstrument;

            public class Order
            {
                public int Id { get; set; }
                public string Name { get; set; }
            }

            public class MyService
            {
                [Instrument]
                public void Process([NoInstrument("Name")] Order order) { }
            }
            """;

        var test = CreateTest(source);
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
