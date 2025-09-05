using System.Text;
using RabbitMQ.Client;

namespace sync_to_async;

public class SyncRabbitMq(IConnection connection, SyncToAsync<string, string> syncToAsync) : BackgroundService
{
  public async Task<string> Publish(string request, CancellationToken cancellationToken)
  {
    using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
    await channel.BasicPublishAsync("", "ping_queue", true, new BasicProperties() { Headers = new Dictionary<string, object?>() {["X-Message-Id"] = request}}, Encoding.UTF8.GetBytes(request), cancellationToken);
    return await syncToAsync.RequestAsync(request, cancellationToken);
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
    try
    {
      while (!stoppingToken.IsCancellationRequested)
      {
        var res = await channel.BasicGetAsync("pong_queue", true, stoppingToken);
        if (res is not null)
        {
          var body = Encoding.UTF8.GetString(res!.Body.Span);
          var msgId = Encoding.UTF8.GetString((byte[])res.BasicProperties.Headers!["X-Message-Id"]!);

          syncToAsync.Resolve(msgId, body);
        }

        await Task.Delay(100, stoppingToken);
      }
    }
    catch (TaskCanceledException)
    {}
    }
}
