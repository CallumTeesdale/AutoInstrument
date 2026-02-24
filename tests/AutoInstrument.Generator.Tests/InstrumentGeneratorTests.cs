using Xunit;

namespace AutoInstrument.Generator.Tests;

public class InstrumentGeneratorTests
{
    [Fact]
    public void BasicMethod_GeneratesActivitySourceAndInterceptor()
    {
        var source = """
            using System.Threading.Tasks;
            using AutoInstrument;

            public class MyService
            {
                [Instrument]
                public async Task<string> DoWork(int id, string name)
                {
                    await Task.Delay(1);
                    return "done";
                }
            }

            public class Caller
            {
                public async Task Run()
                {
                    var svc = new MyService();
                    await svc.DoWork(1, "test");
                }
            }
            """;

        var results = GeneratorTestHelper.RunGenerator(source);

        // No separate ActivitySource file
        Assert.DoesNotContain(results, r => r.HintName.Contains("_ActivitySource"));

        var interceptor = results.FirstOrDefault(r => r.HintName.Contains("Interceptors"));
        Assert.NotNull(interceptor.Source);
        // ActivitySources holder
        Assert.Contains("ActivitySources", interceptor.Source);
        Assert.Contains("new(\"TestAssembly\")", interceptor.Source);
        // Interceptor references the holder
        Assert.Contains("ActivitySources.__autoInstrumentSource_TestAssembly", interceptor.Source);
        Assert.Contains("StartActivity(\"MyService.DoWork\"", interceptor.Source);
        Assert.Contains("SetTag(\"dowork.id\"", interceptor.Source);
        Assert.Contains("SetTag(\"dowork.name\"", interceptor.Source);
        Assert.Contains("try", interceptor.Source);
        Assert.Contains("catch", interceptor.Source);
    }

    [Fact]
    public void CustomActivitySourceName_UsesCustomName()
    {
        var source = """
            using AutoInstrument;

            public class MyService
            {
                [Instrument(ActivitySourceName = "Custom")]
                public void DoWork() { }
            }

            public class Caller
            {
                public void Run()
                {
                    var svc = new MyService();
                    svc.DoWork();
                }
            }
            """;

        var results = GeneratorTestHelper.RunGenerator(source);

        var interceptor = results.FirstOrDefault(r => r.HintName.Contains("Interceptors"));
        Assert.Contains("new(\"Custom\")", interceptor.Source);
        Assert.Contains("ActivitySources.__autoInstrumentSource_Custom", interceptor.Source);
    }

    [Fact]
    public void CustomSpanName_UsesCustomName()
    {
        var source = """
            using AutoInstrument;

            public class MyService
            {
                [Instrument(Name = "custom.span")]
                public void DoWork() { }
            }

            public class Caller
            {
                public void Run()
                {
                    var svc = new MyService();
                    svc.DoWork();
                }
            }
            """;

        var results = GeneratorTestHelper.RunGenerator(source);

        var interceptor = results.FirstOrDefault(r => r.HintName.Contains("Interceptors"));
        Assert.Contains("StartActivity(\"custom.span\"", interceptor.Source);
    }

    [Fact]
    public void SkipParameters_ExcludesSkippedParams()
    {
        var source = """
            using AutoInstrument;

            public class MyService
            {
                [Instrument(Skip = new[] { "password" })]
                public void Login(string username, string password) { }
            }

            public class Caller
            {
                public void Run()
                {
                    var svc = new MyService();
                    svc.Login("user", "secret");
                }
            }
            """;

        var results = GeneratorTestHelper.RunGenerator(source);

        var interceptor = results.FirstOrDefault(r => r.HintName.Contains("Interceptors"));
        Assert.Contains("SetTag(\"login.username\"", interceptor.Source);
        Assert.DoesNotContain("login.password", interceptor.Source);
    }

    [Fact]
    public void FieldsFilter_OnlyTagsSpecifiedFields()
    {
        var source = """
            using AutoInstrument;

            public class MyService
            {
                [Instrument(Fields = new[] { "id" })]
                public void Process(int id, string name, string secret) { }
            }

            public class Caller
            {
                public void Run()
                {
                    var svc = new MyService();
                    svc.Process(1, "n", "s");
                }
            }
            """;

        var results = GeneratorTestHelper.RunGenerator(source);

        var interceptor = results.FirstOrDefault(r => r.HintName.Contains("Interceptors"));
        Assert.Contains("SetTag(\"process.id\"", interceptor.Source);
        Assert.DoesNotContain("process.name", interceptor.Source);
        Assert.DoesNotContain("process.secret", interceptor.Source);
    }

    [Fact]
    public void RecordReturnValue_EmitsReturnValueTag()
    {
        var source = """
            using AutoInstrument;

            public class MyService
            {
                [Instrument(RecordReturnValue = true)]
                public string GetInfo() { return "info"; }
            }

            public class Caller
            {
                public void Run()
                {
                    var svc = new MyService();
                    svc.GetInfo();
                }
            }
            """;

        var results = GeneratorTestHelper.RunGenerator(source);

        var interceptor = results.FirstOrDefault(r => r.HintName.Contains("Interceptors"));
        Assert.Contains("return_value", interceptor.Source);
    }

    [Fact]
    public void RecordExceptionFalse_NoCatchBlock()
    {
        var source = """
            using AutoInstrument;

            public class MyService
            {
                [Instrument(RecordException = false)]
                public void DoWork() { }
            }

            public class Caller
            {
                public void Run()
                {
                    var svc = new MyService();
                    svc.DoWork();
                }
            }
            """;

        var results = GeneratorTestHelper.RunGenerator(source);

        var interceptor = results.FirstOrDefault(r => r.HintName.Contains("Interceptors"));
        Assert.DoesNotContain("try", interceptor.Source);
        Assert.DoesNotContain("catch", interceptor.Source);
    }

    [Fact]
    public void StaticMethod_GeneratesNonExtensionInterceptor()
    {
        var source = """
            using AutoInstrument;

            public class MyService
            {
                [Instrument]
                public static string Compute(string input) { return input; }
            }

            public class Caller
            {
                public void Run()
                {
                    MyService.Compute("test");
                }
            }
            """;

        var results = GeneratorTestHelper.RunGenerator(source);

        var interceptor = results.FirstOrDefault(r => r.HintName.Contains("Interceptors"));
        Assert.NotNull(interceptor.Source);
        // Static interceptor should NOT have "this" extension parameter
        Assert.DoesNotContain("this global::MyService", interceptor.Source);
        Assert.Contains("StartActivity(\"MyService.Compute\"", interceptor.Source);
    }

    [Fact]
    public void VoidSyncMethod_NoReturnResult()
    {
        var source = """
            using AutoInstrument;

            public class MyService
            {
                [Instrument]
                public void DoWork() { }
            }

            public class Caller
            {
                public void Run()
                {
                    var svc = new MyService();
                    svc.DoWork();
                }
            }
            """;

        var results = GeneratorTestHelper.RunGenerator(source);

        var interceptor = results.FirstOrDefault(r => r.HintName.Contains("Interceptors"));
        Assert.DoesNotContain("return __result", interceptor.Source);
    }

    [Fact]
    public void MultipleMethodsSameClass_SingleActivitySourceTwoInterceptors()
    {
        var source = """
            using AutoInstrument;

            public class MyService
            {
                [Instrument]
                public void DoA() { }

                [Instrument]
                public void DoB() { }
            }

            public class Caller
            {
                public void Run()
                {
                    var svc = new MyService();
                    svc.DoA();
                    svc.DoB();
                }
            }
            """;

        var results = GeneratorTestHelper.RunGenerator(source);

        // Single file with both ActivitySource and interceptors
        var interceptor = results.FirstOrDefault(r => r.HintName.Contains("Interceptors"));
        Assert.NotNull(interceptor.Source);

        // One ActivitySource field in the holder
        var sourceCount = interceptor.Source.Split("new(\"TestAssembly\")").Length - 1;
        Assert.Equal(1, sourceCount);

        Assert.Contains("MyService.DoA", interceptor.Source);
        Assert.Contains("MyService.DoB", interceptor.Source);
    }

    [Fact]
    public void TwoClassesSameSourceName_SharedActivitySource()
    {
        var source = """
            using AutoInstrument;

            public class ServiceA
            {
                [Instrument]
                public void DoA() { }
            }

            public class ServiceB
            {
                [Instrument]
                public void DoB() { }
            }

            public class Caller
            {
                public void Run()
                {
                    new ServiceA().DoA();
                    new ServiceB().DoB();
                }
            }
            """;

        var results = GeneratorTestHelper.RunGenerator(source);

        var interceptor = results.FirstOrDefault(r => r.HintName.Contains("Interceptors"));
        Assert.NotNull(interceptor.Source);

        // Both classes default to assembly name hould be exactly one ActivitySource
        var sourceCount = interceptor.Source.Split("new(\"TestAssembly\")").Length - 1;
        Assert.Equal(1, sourceCount);

        // Both interceptors reference the same holder field
        Assert.Contains("ActivitySources.__autoInstrumentSource_TestAssembly", interceptor.Source);
        Assert.Contains("ServiceA.DoA", interceptor.Source);
        Assert.Contains("ServiceB.DoB", interceptor.Source);
    }

    [Fact]
    public void HasListenersGuard_EmittedBeforeActivityStart()
    {
        var source = """
            using AutoInstrument;

            public class MyService
            {
                [Instrument]
                public void DoWork() { }
            }

            public class Caller
            {
                public void Run()
                {
                    var svc = new MyService();
                    svc.DoWork();
                }
            }
            """;

        var results = GeneratorTestHelper.RunGenerator(source);

        var interceptor = results.FirstOrDefault(r => r.HintName.Contains("Interceptors"));
        Assert.Contains("HasListeners()", interceptor.Source);
        var hasListenersIndex = interceptor.Source.IndexOf("HasListeners()");
        var startActivityIndex = interceptor.Source.IndexOf("StartActivity(");
        Assert.True(hasListenersIndex < startActivityIndex,
            "HasListeners() guard should appear before StartActivity()");
    }

    [Fact]
    public void HasListenersGuard_AsyncMethod_AwaitsOnFastPath()
    {
        var source = """
            using System.Threading.Tasks;
            using AutoInstrument;

            public class MyService
            {
                [Instrument]
                public async Task<string> DoWork(int id)
                {
                    await Task.Delay(1);
                    return "done";
                }
            }

            public class Caller
            {
                public async Task Run()
                {
                    var svc = new MyService();
                    await svc.DoWork(1);
                }
            }
            """;

        var results = GeneratorTestHelper.RunGenerator(source);

        var interceptor = results.FirstOrDefault(r => r.HintName.Contains("Interceptors"));
        // Fast path should await the original call
        Assert.Contains("return await __self.DoWork(id);", interceptor.Source);
    }

    [Fact]
    public void AssemblyAttribute_OverridesDefaultSourceName()
    {
        var source = """
            using AutoInstrument;

            [assembly: AutoInstrumentSource("MyApp")]

            public class MyService
            {
                [Instrument]
                public void DoWork() { }
            }

            public class Caller
            {
                public void Run()
                {
                    var svc = new MyService();
                    svc.DoWork();
                }
            }
            """;

        var results = GeneratorTestHelper.RunGenerator(source);

        var interceptor = results.FirstOrDefault(r => r.HintName.Contains("Interceptors"));
        Assert.Contains("new(\"MyApp\")", interceptor.Source);
        Assert.Contains("ActivitySources.__autoInstrumentSource_MyApp", interceptor.Source);
        Assert.DoesNotContain("TestAssembly", interceptor.Source);
    }

    [Fact]
    public void MsBuildProperty_OverridesDefaultSourceName()
    {
        var source = """
            using AutoInstrument;

            public class MyService
            {
                [Instrument]
                public void DoWork() { }
            }

            public class Caller
            {
                public void Run()
                {
                    var svc = new MyService();
                    svc.DoWork();
                }
            }
            """;

        var results = GeneratorTestHelper.RunGenerator(source, autoInstrumentSourceName: "FromMsBuild");

        var interceptor = results.FirstOrDefault(r => r.HintName.Contains("Interceptors"));
        Assert.Contains("new(\"FromMsBuild\")", interceptor.Source);
        Assert.Contains("ActivitySources.__autoInstrumentSource_FromMsBuild", interceptor.Source);
        Assert.DoesNotContain("TestAssembly", interceptor.Source);
    }

    [Fact]
    public void MsBuildProperty_TakesPriorityOverAssemblyAttribute()
    {
        var source = """
            using AutoInstrument;

            [assembly: AutoInstrumentSource("FromAttribute")]

            public class MyService
            {
                [Instrument]
                public void DoWork() { }
            }

            public class Caller
            {
                public void Run()
                {
                    var svc = new MyService();
                    svc.DoWork();
                }
            }
            """;

        var results = GeneratorTestHelper.RunGenerator(source, autoInstrumentSourceName: "FromMsBuild");

        var interceptor = results.FirstOrDefault(r => r.HintName.Contains("Interceptors"));
        Assert.Contains("new(\"FromMsBuild\")", interceptor.Source);
        Assert.DoesNotContain("FromAttribute", interceptor.Source);
    }

    [Fact]
    public void PerMethodSourceName_TakesPriorityOverDefaults()
    {
        var source = """
            using AutoInstrument;

            [assembly: AutoInstrumentSource("FromAttribute")]

            public class MyService
            {
                [Instrument(ActivitySourceName = "PerMethod")]
                public void DoWork() { }
            }

            public class Caller
            {
                public void Run()
                {
                    var svc = new MyService();
                    svc.DoWork();
                }
            }
            """;

        var results = GeneratorTestHelper.RunGenerator(source, autoInstrumentSourceName: "FromMsBuild");

        var interceptor = results.FirstOrDefault(r => r.HintName.Contains("Interceptors"));
        Assert.Contains("new(\"PerMethod\")", interceptor.Source);
        Assert.DoesNotContain("FromMsBuild", interceptor.Source);
        Assert.DoesNotContain("FromAttribute", interceptor.Source);
    }

    [Fact]
    public void ComplexType_AutoExpandsPublicProperties()
    {
        var source = """
            using AutoInstrument;

            public class Order
            {
                public int Id { get; set; }
                public string Name { get; set; }
                private string Secret { get; set; }
            }

            public class MyService
            {
                [Instrument]
                public void Process(Order order) { }
            }

            public class Caller
            {
                public void Run()
                {
                    var svc = new MyService();
                    svc.Process(new Order());
                }
            }
            """;

        var results = GeneratorTestHelper.RunGenerator(source);

        var interceptor = results.FirstOrDefault(r => r.HintName.Contains("Interceptors"));
        // Should expand public properties
        Assert.Contains("process.order.id", interceptor.Source);
        Assert.Contains("process.order.name", interceptor.Source);
        Assert.Contains("order?.Id", interceptor.Source);
        Assert.Contains("order?.Name", interceptor.Source);
        // Private property should not be expanded
        Assert.DoesNotContain("secret", interceptor.Source);
    }

    [Fact]
    public void ComplexType_SkipDotNotation_SkipsSpecificProperty()
    {
        var source = """
            using AutoInstrument;

            public class Order
            {
                public int Id { get; set; }
                public string CreditCard { get; set; }
            }

            public class MyService
            {
                [Instrument(Skip = new[] { "order.CreditCard" })]
                public void Process(Order order) { }
            }

            public class Caller
            {
                public void Run()
                {
                    var svc = new MyService();
                    svc.Process(new Order());
                }
            }
            """;

        var results = GeneratorTestHelper.RunGenerator(source);

        var interceptor = results.FirstOrDefault(r => r.HintName.Contains("Interceptors"));
        Assert.Contains("process.order.id", interceptor.Source);
        Assert.DoesNotContain("creditcard", interceptor.Source);
    }

    [Fact]
    public void ComplexType_FieldsDotNotation_OnlyTagsSpecifiedProperties()
    {
        var source = """
            using AutoInstrument;

            public class Order
            {
                public int Id { get; set; }
                public string Name { get; set; }
                public string Secret { get; set; }
            }

            public class MyService
            {
                [Instrument(Fields = new[] { "order.Id" })]
                public void Process(Order order) { }
            }

            public class Caller
            {
                public void Run()
                {
                    var svc = new MyService();
                    svc.Process(new Order());
                }
            }
            """;

        var results = GeneratorTestHelper.RunGenerator(source);

        var interceptor = results.FirstOrDefault(r => r.HintName.Contains("Interceptors"));
        Assert.Contains("process.order.id", interceptor.Source);
        Assert.DoesNotContain("process.order.name", interceptor.Source);
        Assert.DoesNotContain("process.order.secret", interceptor.Source);
    }

    [Fact]
    public void PrimitiveType_NotExpanded()
    {
        var source = """
            using AutoInstrument;

            public class MyService
            {
                [Instrument]
                public void Process(int id, string name) { }
            }

            public class Caller
            {
                public void Run()
                {
                    var svc = new MyService();
                    svc.Process(1, "test");
                }
            }
            """;

        var results = GeneratorTestHelper.RunGenerator(source);

        var interceptor = results.FirstOrDefault(r => r.HintName.Contains("Interceptors"));
        // Primitives should be tagged directly, not expanded
        Assert.Contains("SetTag(\"process.id\", id)", interceptor.Source);
        Assert.Contains("SetTag(\"process.name\", name)", interceptor.Source);
    }

    [Fact]
    public void ComplexType_SkipEntireParameter_NoTags()
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
                [Instrument(Skip = new[] { "order" })]
                public void Process(Order order, int id) { }
            }

            public class Caller
            {
                public void Run()
                {
                    var svc = new MyService();
                    svc.Process(new Order(), 1);
                }
            }
            """;

        var results = GeneratorTestHelper.RunGenerator(source);

        var interceptor = results.FirstOrDefault(r => r.HintName.Contains("Interceptors"));
        // order should be entirely skipped
        Assert.DoesNotContain("process.order", interceptor.Source);
        // id should still be tagged
        Assert.Contains("process.id", interceptor.Source);
    }

    [Fact]
    public void NoInstrument_SkipsEntireParameter()
    {
        var source = """
            using AutoInstrument;

            public class MyService
            {
                [Instrument]
                public void Login(string user, [NoInstrument] string password) { }
            }

            public class Caller
            {
                public void Run()
                {
                    var svc = new MyService();
                    svc.Login("admin", "secret");
                }
            }
            """;

        var results = GeneratorTestHelper.RunGenerator(source);

        var interceptor = results.FirstOrDefault(r => r.HintName.Contains("Interceptors"));
        Assert.Contains("SetTag(\"login.user\"", interceptor.Source);
        Assert.DoesNotContain("login.password", interceptor.Source);
    }

    [Fact]
    public void NoInstrument_WithProperties_SkipsSpecificProperties()
    {
        var source = """
            using AutoInstrument;

            public class Order
            {
                public int Id { get; set; }
                public string CreditCard { get; set; }
                public string Name { get; set; }
            }

            public class MyService
            {
                [Instrument]
                public void Process([NoInstrument("CreditCard")] Order order) { }
            }

            public class Caller
            {
                public void Run()
                {
                    var svc = new MyService();
                    svc.Process(new Order());
                }
            }
            """;

        var results = GeneratorTestHelper.RunGenerator(source);

        var interceptor = results.FirstOrDefault(r => r.HintName.Contains("Interceptors"));
        Assert.Contains("process.order.id", interceptor.Source);
        Assert.Contains("process.order.name", interceptor.Source);
        Assert.DoesNotContain("creditcard", interceptor.Source);
    }

    [Fact]
    public void RecordSuccess_EmitsSetStatusOk()
    {
        var source = """
            using AutoInstrument;

            public class MyService
            {
                [Instrument(RecordSuccess = true)]
                public void DoWork() { }
            }

            public class Caller
            {
                public void Run()
                {
                    var svc = new MyService();
                    svc.DoWork();
                }
            }
            """;

        var results = GeneratorTestHelper.RunGenerator(source);

        var interceptor = results.FirstOrDefault(r => r.HintName.Contains("Interceptors"));
        Assert.Contains("SetStatus(global::System.Diagnostics.ActivityStatusCode.Ok)", interceptor.Source);
    }

    [Fact]
    public void IgnoreCancellation_True_HasOCEGuard()
    {
        var source = """
            using AutoInstrument;

            public class MyService
            {
                [Instrument(IgnoreCancellation = true)]
                public void DoWork() { }
            }

            public class Caller
            {
                public void Run()
                {
                    var svc = new MyService();
                    svc.DoWork();
                }
            }
            """;

        var results = GeneratorTestHelper.RunGenerator(source);

        var interceptor = results.FirstOrDefault(r => r.HintName.Contains("Interceptors"));
        Assert.Contains("is not global::System.OperationCanceledException", interceptor.Source);
    }

    [Fact]
    public void IgnoreCancellation_False_NoOCEGuard()
    {
        var source = """
            using AutoInstrument;

            public class MyService
            {
                [Instrument(IgnoreCancellation = false)]
                public void DoWork() { }
            }

            public class Caller
            {
                public void Run()
                {
                    var svc = new MyService();
                    svc.DoWork();
                }
            }
            """;

        var results = GeneratorTestHelper.RunGenerator(source);

        var interceptor = results.FirstOrDefault(r => r.HintName.Contains("Interceptors"));
        Assert.DoesNotContain("OperationCanceledException", interceptor.Source);
    }

    [Fact]
    public void TagAttribute_EmitsTagFromMember()
    {
        var source = """
            using AutoInstrument;

            public class MyService
            {
                [Tag] public string Region { get; set; }
                [Tag(Name = "env")] public string Environment { get; set; }

                [Instrument]
                public void Process(int id) { }
            }

            public class Caller
            {
                public void Run()
                {
                    var svc = new MyService();
                    svc.Process(1);
                }
            }
            """;

        var results = GeneratorTestHelper.RunGenerator(source);

        var interceptor = results.FirstOrDefault(r => r.HintName.Contains("Interceptors"));
        Assert.Contains("SetTag(\"region\", __self.Region)", interceptor.Source);
        Assert.Contains("SetTag(\"env\", __self.Environment)", interceptor.Source);
    }

    [Fact]
    public void TagNamingConvention_Flat_NoMethodPrefix()
    {
        var source = """
            using AutoInstrument;

            [assembly: AutoInstrumentConfig(TagNaming = TagNamingConvention.Flat)]

            public class MyService
            {
                [Instrument]
                public void Process(int id, string name) { }
            }

            public class Caller
            {
                public void Run()
                {
                    var svc = new MyService();
                    svc.Process(1, "test");
                }
            }
            """;

        var results = GeneratorTestHelper.RunGenerator(source);

        var interceptor = results.FirstOrDefault(r => r.HintName.Contains("Interceptors"));
        Assert.Contains("SetTag(\"id\", id)", interceptor.Source);
        Assert.Contains("SetTag(\"name\", name)", interceptor.Source);
        Assert.DoesNotContain("process.id", interceptor.Source);
    }

    [Fact]
    public void Condition_EmitsConditionGuard()
    {
        var source = """
            using AutoInstrument;

            public class MyService
            {
                public bool IsEnabled { get; set; }

                [Instrument(Condition = "IsEnabled")]
                public void Process(int id) { }
            }

            public class Caller
            {
                public void Run()
                {
                    var svc = new MyService();
                    svc.Process(1);
                }
            }
            """;

        var results = GeneratorTestHelper.RunGenerator(source);

        var interceptor = results.FirstOrDefault(r => r.HintName.Contains("Interceptors"));
        Assert.Contains("if (!__self.IsEnabled)", interceptor.Source);
    }

    [Fact]
    public void LinkTo_EmitsActivityLink()
    {
        var source = """
            using System.Diagnostics;
            using AutoInstrument;

            public class MyService
            {
                [Instrument(LinkTo = "ctx")]
                public void Process(int id, ActivityContext ctx) { }
            }

            public class Caller
            {
                public void Run()
                {
                    var svc = new MyService();
                    svc.Process(1, default);
                }
            }
            """;

        var results = GeneratorTestHelper.RunGenerator(source);

        var interceptor = results.FirstOrDefault(r => r.HintName.Contains("Interceptors"));
        Assert.Contains("new global::System.Diagnostics.ActivityLink(ctx)", interceptor.Source);
    }

    [Fact]
    public void MsBuildTagNaming_Flat_OverridesDefault()
    {
        var source = """
            using AutoInstrument;

            public class MyService
            {
                [Instrument]
                public void Process(int id) { }
            }

            public class Caller
            {
                public void Run()
                {
                    var svc = new MyService();
                    svc.Process(1);
                }
            }
            """;

        var results = GeneratorTestHelper.RunGenerator(source, tagNaming: "Flat");

        var interceptor = results.FirstOrDefault(r => r.HintName.Contains("Interceptors"));
        Assert.Contains("SetTag(\"id\", id)", interceptor.Source);
        Assert.DoesNotContain("process.id", interceptor.Source);
    }
}
