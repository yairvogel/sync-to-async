using System.Collections.Concurrent;

namespace sync_to_async;

public class SyncToAsync
{
    private readonly ILogger<SyncToAsync> logger;
    public SyncToAsync(ILogger<SyncToAsync> logger, IHostApplicationLifetime hostLifetime)
    {
        this.logger = logger;
        hostLifetime.ApplicationStopping.Register(CancelAll);
    }

    private record Entry(TaskCompletionSource<string> Tcs);
    private readonly ConcurrentDictionary<string, Entry> inflight = new();

    public Task<string> RequestAsync(string request, CancellationToken cancellationToken)
    {
        cancellationToken.UnsafeRegister(CancelKey, request);
        var entry = inflight.GetOrAdd(request, _ =>
        {
            logger.LogDebug("creating entry for {request}", request);
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

    private void CancelKey(object? state, CancellationToken token) {
        var key = (string)state!;
        logger.LogDebug("cancelling key {Key}", key);
        if (inflight.TryRemove(key, out var entry))
        {
            entry.Tcs.SetCanceled(token);
            logger.LogDebug("cancelled key {Key}", key);
        }
    }

    public void CancelAll()
    {
        foreach (var e in inflight.Values)
        {
            e.Tcs.SetCanceled();
        }
    }
}
