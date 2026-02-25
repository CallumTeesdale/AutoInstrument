using System.Diagnostics;

namespace AutoInstrument.Benchmarks;

public static class ManualInstrumentation
{
    private static readonly ActivitySource Source = new("AutoInstrument.Benchmarks");

    public static void SyncVoid(TargetService service)
    {
        if (!Source.HasListeners())
        {
            service.SyncVoid_Baseline();
            return;
        }

        using var activity = Source.StartActivity("TargetService.SyncVoid", ActivityKind.Internal);

        try
        {
            service.SyncVoid_Baseline();
        }
        catch (Exception ex)
        {
            if (activity is not null)
            {
                activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity.AddEvent(new ActivityEvent("exception",
                    tags: new ActivityTagsCollection
                    {
                        { "exception.type", ex.GetType().FullName },
                        { "exception.message", ex.Message },
                        { "exception.stacktrace", ex.ToString() }
                    }));
            }

            throw;
        }
    }

    public static int SyncReturn(TargetService service, int x)
    {
        if (!Source.HasListeners())
        {
            return service.SyncReturn_Baseline(x);
        }

        using var activity = Source.StartActivity("TargetService.SyncReturn", ActivityKind.Internal);

        if (activity is not null)
        {
            activity.SetTag("x", x);
        }

        try
        {
            return service.SyncReturn_Baseline(x);
        }
        catch (Exception ex)
        {
            if (activity is not null)
            {
                activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity.AddEvent(new ActivityEvent("exception",
                    tags: new ActivityTagsCollection
                    {
                        { "exception.type", ex.GetType().FullName },
                        { "exception.message", ex.Message },
                        { "exception.stacktrace", ex.ToString() }
                    }));
            }

            throw;
        }
    }

    public static async Task AsyncTask(TargetService service)
    {
        if (!Source.HasListeners())
        {
            await service.AsyncTask_Baseline();
            return;
        }

        using var activity = Source.StartActivity("TargetService.AsyncTask", ActivityKind.Internal);

        try
        {
            await service.AsyncTask_Baseline();
        }
        catch (Exception ex)
        {
            if (activity is not null)
            {
                activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity.AddEvent(new ActivityEvent("exception",
                    tags: new ActivityTagsCollection
                    {
                        { "exception.type", ex.GetType().FullName },
                        { "exception.message", ex.Message },
                        { "exception.stacktrace", ex.ToString() }
                    }));
            }

            throw;
        }
    }

    public static async Task<int> AsyncTaskOfT(TargetService service, int x)
    {
        if (!Source.HasListeners())
        {
            return await service.AsyncTaskOfT_Baseline(x);
        }

        using var activity = Source.StartActivity("TargetService.AsyncTaskOfT", ActivityKind.Internal);

        if (activity is not null)
        {
            activity.SetTag("x", x);
        }

        try
        {
            return await service.AsyncTaskOfT_Baseline(x);
        }
        catch (Exception ex)
        {
            if (activity is not null)
            {
                activity.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity.AddEvent(new ActivityEvent("exception",
                    tags: new ActivityTagsCollection
                    {
                        { "exception.type", ex.GetType().FullName },
                        { "exception.message", ex.Message },
                        { "exception.stacktrace", ex.ToString() }
                    }));
            }

            throw;
        }
    }
}
