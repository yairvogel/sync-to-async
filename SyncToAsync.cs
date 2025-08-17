using System.Collections.Concurrent;

public class SyncToAsync : IDisposable
{
    private readonly ILogger<SyncToAsync> logger;
    public SyncToAsync(ILogger<SyncToAsync> logger, IHostApplicationLifetime hostLifetime)
    {
        this.logger = logger;
        hostLifetime.ApplicationStopping.Register(Dispose);
    }

    private record Entry(TaskCompletionSource<string> Tcs);
    private readonly ConcurrentDictionary<string, Entry> inflight = new();

    public Task<string> RequestAsync(string request, CancellationToken cancellationToken)
    {
        var entry = inflight.GetOrAdd(request, _ =>
        {
            logger.LogInformation("creating entry for {request}", request);
            return new Entry(new(cancellationToken));
        });

        return entry.Tcs.Task;
    }

    public void Resolve(string request, string response)
    {
        logger.LogInformation("resolving request {request} with {response}", request, response);
        if (!inflight.TryRemove(request, out Entry? entry))
        {
            logger.LogWarning("Couldn't find {request}", request);
            return;
        }

        entry.Tcs.SetResult(response);
    }

    public void Dispose()
    {
        foreach (var e in inflight.Values)
        {
            e.Tcs.SetCanceled();
        }
    }
}
