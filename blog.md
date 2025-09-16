
# Converting RabbitMQ request-response to synchronous call

Lately, I was tasked with implementing an integration with a large bank. Unlike other integrations, this bank uses a amqp based api, where you send requests as messasges to a queue, and receive your resposnes through a different queue.

Simply put, the architecture looks somewhat like this:

```
----------------              --------------
|              |   requests   |             |
|              | -----------> |             |
| Our Service  |              |    Bank     |
|              |   responses  |             |
|              | <----------- |             |
----------------              --------------
```

The caveat - This service has to expose a simple Http API. The fact that this service integration uses rabbitMQ shouldn't be leaked to the services consuming this service. So, somehow, we should convert a synchronous api to an asynchronous one. The request response loop is solved by using a Request-Id header to our rabbitmq messages, that is being echoed to us with the response. So as long as we can compute a serializable key to our request, we'll be able to resolve it when the response is received.

## .net async-await
.net is async heavy and async functionality is baked into the language and the runtime, and there's a big ecosystem that is optimized towards async workloads. However, normally when writing standard backend code and async code no asynchrony is actually introduced. Let's take a look at the following example, from a simplified controller action for creating an entity and saving it to database:

```csharp
public async Task<string> AddEntity(DbContext context, Entity entity, CancellationToken cancellationToken)
{
    entity.Id = Guid.NewGuid();
    context.Add(entity);
    await context.SaveChangesAsync(cancellationToken);
    return CreatedAtAction(nameof(GetEntity), new { id = entity.Id }, entity);
}
```

This function lives in the middle of an example backend project that uses ASP.net and EFCore. It has the async modifier, and awaits a call to context from efcore. Except for the async-await keywords and the Task return type, this function looks nothing an async function! It takes its parameters, gets/executes the data on the database and simply gets the responses, which it then later returns. The async-await syntax does a great job in hiding the fact that this function runs asynchronously. But where is this asynchrony happening?

The short answer is asynchrony shines only when the workloads are IO-bound - where asynchronous networking and multi-processing comes into play. In that networking model a request can be sent, and the calling thread doesn't need to wait for the response to arrive, freeing the cpu and improving the overall throughput of the dotnet backend service. Luckily, the hard part of asynchrony is normally solved for us by the asp.net team and the EFCore team (or any low-level dotnet libraries), all we have to do is used the long-established APIs that support that, using the Task type and the async-await sugar.

However, for the problem that I needed to solve asynchrony is part of the application communication. The rabbitMQ API exposed by my employer partner bank is async by nature, and there's no magic library that just do that. So - I need to create a component that manages the async API but exposes a Task API that is simple and allows for standard async-await, similar to the above example, to use it just fine. Let's solve this issue together.

## Manual async using the TaskCompletionSource

So, we need to resort to creating, managing and exposing our own tasks, and the we achieve this is by using TaskCompletionSource. I like to think about the TaskCompletionSource as the backstage worker that runs around and works in setting up the show that is the Task object. The class exposes a Task that can be returned, and exposes hooks that allow us to control its state in a way the calling async-await code will know how to handle. The simplest case is the SetResult api:

```csharp
async Task<string> ExternalMethod()
{
    string s = await RequestAsync("ping");
    return s;
}

public Task<string> RequestAsync(string message)
{
    var tcs = new TaskCompletionSource();
    tcs.SetResult("pong");
    return tcs.Task;
}
```

The following code defines RequestAsync. This function returns a Task but it is not async. Infact, this function runs synchronously and returns an already completed task, with `task.IsCompleted == true` and `task.Result == "pong"`. Calling `await` on the returned task will immediately yield "pong" - but we didn't await it as it was already precomputed before being returned to us.

#### ...But this wasn't my intention

So, we wrote a piece of code that's only disguised as an async method, but actually runs synchronously. But, as I previously said, async code is only async at the network level so if there's no io there's also no reason to be async. (small remark: async code doesn't ONLY live in networked workdloads. An example would be the use of channels for communication between threads. However normally are a part of a solution for an async-networked workload). We want to implement an async communication solution, so let's setup an asynchronous toy model:

```csharp
public class SyncToAsync
{
    // TaskCompletionSource<T> manages a Task<T>
    private TaskCompletionSource<string>? tcs = null;

    /// <summary>Our example "regular" async-await code
    public async Task<string> ExternalGet(string content)
    {
        if (tcs is not null) throw new InvalidOperationException("We can send only one request, for now");
        string s = await RequestAsync(content);
        return s;
    }

    public async string Resolve(string response)
    {
        tcs.SetResult(response);
    }

    private Task<string> RequestAsync(string content) // look ma, no async keyword!
    {
        tcs = new TaskCompletionSource()
        return tcs.Task;
    }
}
```

Now, if we call `ExternalGet` we'll hang, because `ExternalGet` calls `RequestAsync` that returns are new manual Task object. Since it wasn't completed with SetResult, this task is now in a waiting/running state. If we naively `await` this task, we'll run into a deadlock as we have no code to resolve it. Therefore, we need a separate request/thread to call Resolve - and this is where the TaskCompletionSource shines. A new request, carried out by potentially a different thread, resolved the task with a value and our calling code handled it naturally. We created are own basic asynchronous io wrapper. Of course, this code still has many issues and I can't think of many use cases that this implementation solves, but we got the main functionality and this can also be tested - put this component behind controllers and call it from two different applications (use curl, postman or swagger). The resolve call will resolve the request call, and the value provided by resolve will be returned from the request.


### Setting up the workload

we now have our basic async controls that allow us to create and resolve tasks. All we have left to have an end to end solution is to connect the async controlls to our async communication layer. In our case, we're connecting to rabbitMQ. First - the test setup: I created a new small console application called 'echo' that, literally, has the following code:
echo/Program.cs

```csharp
using System.Text;
using RabbitMQ.Client;

var connString = Environment.GetEnvironmentVariable("RmqConnectionString") ?? "amqp://user:pass@localhost:5672";

var factory = new ConnectionFactory { Uri = new Uri(connString) };

var connection = await factory.CreateConnectionAsync();
using var channel = await connection.CreateChannelAsync();
var queue = await channel.QueueDeclareAsync();
await channel.QueueBindAsync(queue.QueueName, "ping", string.Empty);

while (true)
{
  var get = await channel.BasicGetAsync(queue.QueueName, true, default);
  if (get is not null) {
    string body = Encoding.UTF8.GetString(get.Body.Span);
    await channel.BasicPublishAsync("pong", string.Empty, true, new BasicProperties(get.BasicProperties), Encoding.UTF8.GetBytes(body.Replace("ping", "pong")));
  }

  await Task.Delay(100);
}
```

This is a console application with `RabbitMQ.Client` dependency. It runs in a loop and tries to consume messages from the 'ping' RabbitMQ exchange. It'll read the content of the message and respond with the same text, replacing 'ping' with 'pong'. An important property we'll depend on is the fact that the echo program echoes back the properties sent to it, unchanged. If we'll send a request id header, we'll be able to get it back and resolve the correct request.

The dockerfile defined can be found in the github repo - it defines our service, the echo service and a regular rabbitMQ instance. It configures environment variables in both dotnet service so they can interact through the RMQ cluster.

### Integrating to rabbitMQ

We left our solution in a point where it has basic sync-to-async controls. We connected the sync part to an http controller that uses regular async-await syntax and now we're faced with the async part. Before we do that, let's improve SyncToAsync and make it a bit easier to use:

```csharp
public class SyncToAsync<TKey, TResponse> where TKey: notnull
{
    private readonly ConcurrentDictionary<TKey, TaskCompletionSource<TResponse>> inflight = new();

    public Task<TResponse> RequestAsync(string requestKey)
    {
        var entry = inflight.GetOrAdd(request, _ => return new TaskCompletionSource());
        return entry.Task;
    }

    public void Resolve(TKey requestKey, TResponse response)
    {
        if (!inflight.TryRemove(request, out var entry))
        {
            // resolving a request that is not inflight, this is an error state
            return;
        }

        entry.SetResult(response);
    }
}
```

SyncToAsync received two improvements. Firstly, it is now generic and supports all Task generic types. All the tasks have to be of the same type which for some workloads this might not be the correct solution. Request-level generic implementations exist but require some extra work. Secondly, SyncToAsync core now supports multiple requests happening at the same which is definitely an improvement from our one-request poor implementation. The resolution using a request key is important, and allows us to resolve the correct context and answer with the correct response to the correct client that is currently waiting. Notice that although this class provides a Task, no async-await is used to achieve this. As we're implementing the async resolution of the tasks we find that we don't require async-await sugar to aid us.


SyncRabbitMq.cs:
```csharp
using System.Text;
using RabbitMQ.Client;

namespace sync_to_async;

public class SyncRabbitMq(IConnection connection, SyncToAsync<string, string> syncToAsync) : BackgroundService
{
  public async Task<string> Publish(string request, int? delay)
  {
    using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
    var headers = new Dictionary<string, object?>() {["X-Message-Id"] = request};
    await channel.BasicPublishAsync("ping", routingKey: string.Empty, mandatory: true, basicProperties: new BasicProperties() { Headers = headers }, Encoding.UTF8.GetBytes(request), cancellationToken);

    // This await will not complete until we get a response message back
    return await syncToAsync.RequestAsync(request);
  }

  // consuming responses
  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
    var queue = await channel.QueueDeclareAsync(cancellationToken: stoppingToken);
    // read our responses from the 'pong' exchange
    await channel.QueueBindAsync(queue.QueueName, "pong", routingKey: string.Empty, cancellationToken: stoppingToken);
    try
    {
      // poor man's message consumer. Do not try this at prod kids!
      while (!stoppingToken.IsCancellationRequested)
      {
        var res = await channel.BasicGetAsync(queue.QueueName, true, stoppingToken);
        if (res is not null)
        {
          var body = Encoding.UTF8.GetString(res!.Body.Span);
          var msgId = Encoding.UTF8.GetString((byte[])res.BasicProperties.Headers!["X-Message-Id"]!);

          // This resolves the request from before
          syncToAsync.Resolve(msgId, body);
        }

        await Task.Delay(100, stoppingToken);
      }
    }
    catch (TaskCanceledException)
    {}
    }
}
```

This is where everything comes together - We do a request to a controller that calls SyncRabbitMq.RequestAsync. The call to SyncRabbitMq sends a request to our echo service and creates the TaskCompletionSource that is correlated with the request-id. The request is processed by the echo service and the response is sent back through the 'pong' queue, which the service reads and extracts the request id from. It then resolves the TaskCompletionSource with the contents of the inbound rabbitMq body, and we're done!
We've completely created support for a new asynchronous communication channel, in a way that is able to efficiently hook up into dotnet's async-await.

In the next post, I'll cover to important problems that arise when we opt in into managing our own tasks - cancellation and graceful shutdown.
You can check out the code for further reading.
