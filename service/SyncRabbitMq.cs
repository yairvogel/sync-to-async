using System.Text;
using RabbitMQ.Client;

namespace sync_to_async;

public class SyncRabbitMq(IConnection connection, SyncToAsync<string, string> syncToAsync, ILogger<SyncRabbitMq> logger) : BackgroundService
{
  public async Task<string> Publish(string request, int? delay, CancellationToken cancellationToken)
  {
    using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);
    var headers = new Dictionary<string, object?>() {["X-Message-Id"] = request};
    if (delay is not null)
    {
      headers.Add("X-Delay", delay);
    }
    await channel.BasicPublishAsync("ping", string.Empty, true, new BasicProperties() { Headers = headers }, Encoding.UTF8.GetBytes(request), cancellationToken);
    return await syncToAsync.RequestAsync(request, cancellationToken);
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
    var queue = await channel.QueueDeclareAsync(cancellationToken: stoppingToken);
    await channel.QueueBindAsync(queue.QueueName, "pong", string.Empty, cancellationToken: stoppingToken);
    try
    {
      while (!stoppingToken.IsCancellationRequested)
      {
        var res = await channel.BasicGetAsync(queue.QueueName, true, stoppingToken);
        if (res is not null)
        {
          var body = Encoding.UTF8.GetString(res!.Body.Span);
          var msgId = Encoding.UTF8.GetString((byte[])res.BasicProperties.Headers!["X-Message-Id"]!);
          logger.LogInformation("{MsgId}, {Body}", msgId, body);

          syncToAsync.Resolve(msgId, body);
        }

        await Task.Delay(100, stoppingToken);
      }
    }
    catch (TaskCanceledException)
    {}
    }
}
