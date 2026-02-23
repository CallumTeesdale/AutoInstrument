using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SampleApp.Services;

public class DemoWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HashService _hashService;
    private readonly IHostApplicationLifetime _lifetime;

    public DemoWorker(
        IServiceScopeFactory scopeFactory,
        HashService hashService,
        IHostApplicationLifetime lifetime)
    {
        _scopeFactory = scopeFactory;
        _hashService = hashService;
        _lifetime = lifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<YakShavingService>();

        Console.WriteLine("=== Shaving yak #42 ===");
        var result = await svc.ShaveYak(42, "standard");
        Console.WriteLine($"Result: {result}\n");

        Console.WriteLine("=== Shaving yak #13 (will fail) ===");
        try
        {
            await svc.ShaveYak(13, "deluxe");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Caught: {ex.Message}\n");
        }

        Console.WriteLine("=== Getting yak info ===");
        var info = svc.GetYakInfo(42);
        Console.WriteLine($"Info: {info}\n");

        Console.WriteLine("=== Secure operation (password skipped from tags) ===");
        await svc.Authenticate(42, "s3cret_p@ss!", "some-token");
        Console.WriteLine("Done.\n");

        Console.WriteLine("=== Hash via separate service ===");
        var hash = _hashService.ComputeYakHash("yak-42-data");
        Console.WriteLine($"Hash: {hash}");

        _lifetime.StopApplication();
    }
}
