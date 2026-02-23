using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using SampleApp.Services;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddScoped<YakShavingService>();
builder.Services.AddSingleton<HashService>();
builder.Services.AddHostedService<DemoWorker>();

builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("SampleApp"))
    .WithTracing(t => t
        .AddSource("SampleApp")
        .AddOtlpExporter(o => o.Endpoint = new Uri("http://localhost:4317")));

var host = builder.Build();
await host.RunAsync();
