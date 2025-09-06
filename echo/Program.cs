
using System.Text;
using RabbitMQ.Client;

var connString = Environment.GetEnvironmentVariable("RmqConnectionString") ?? "amqp://user:pass@localhost:5672";

var factory = new ConnectionFactory
{
    Uri = new Uri(connString)
};

var connection = await factory.CreateConnectionAsync();
using var channel = await connection.CreateChannelAsync();
var queue = await channel.QueueDeclareAsync();
await channel.QueueBindAsync(queue.QueueName, "ping", string.Empty);

while (true)
{
  var get = await channel.BasicGetAsync(queue.QueueName, true, default);
  if (get is not null) {
    string body = Encoding.UTF8.GetString(get.Body.Span);
    Console.WriteLine($"got message: {body}");
    await channel.BasicPublishAsync("pong", string.Empty, true, new BasicProperties(get.BasicProperties), Encoding.UTF8.GetBytes(body.Replace("ping", "pong")));
  }

  await Task.Delay(100);
}
