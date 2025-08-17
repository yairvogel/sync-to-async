using System.Collections.Concurrent;

namespace sync_to_async;

public class SyncToAsync<TRequest, TResponse>
    where TRequest : notnull
{
    private readonly ILogger<SyncToAsync<TRequest, TResponse>> logger;
    private readonly ConcurrentDictionary<TRequest, TaskCompletionSource<TResponse>> inflight = new();

    public SyncToAsync(ILogger<SyncToAsync<TRequest, TResponse>> logger, IHostApplicationLifetime hostLifetime)
    {
        this.logger = logger;
        hostLifetime.ApplicationStopping.Register(CancelAll);
    }

    public Task<TResponse> RequestAsync(TRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.UnsafeRegister(CancelKey, request);
        var entry = inflight.GetOrAdd(request, _ =>
        {
            logger.LogDebug("creating entry for {request}", request);
            return new(cancellationToken);
        });

        return entry.Task;
    }

    public void Resolve(TRequest request, TResponse response)
    {
        logger.LogInformation("resolving request {request} with {response}", request, response);
        if (!inflight.TryRemove(request, out var entry))
        {
            logger.LogWarning("Couldn't find {request}", request);
            return;
        }

        entry.SetResult(response);
    }

    private void CancelKey(object? state, CancellationToken token) {
        var key = (TRequest)state!;
        logger.LogDebug("cancelling key {Key}", key);
        if (inflight.TryRemove(key, out var entry))
        {
            entry.SetCanceled(token);
            logger.LogDebug("cancelled key {Key}", key);
        }
    }

    public void CancelAll()
    {
        foreach (var e in inflight.Values)
        {
            e.SetCanceled();
        }
    }
}
