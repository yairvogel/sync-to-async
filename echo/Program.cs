
using System.Text;
using RabbitMQ.Client;

var connString = Environment.GetEnvironmentVariable("RmqConnectionString") ?? "amqp://user:pass@localhost:5672";

var factory = new ConnectionFactory
{
    Uri = new Uri(connString)
};

var connection = await factory.CreateConnectionAsync();
using var channel = await connection.CreateChannelAsync();

while (true)
{
  var get = await channel.BasicGetAsync("ping_queue", true, default);
  if (get is not null) {
    string body = Encoding.UTF8.GetString(get.Body.Span);
    Console.WriteLine($"got message: {body}");
    await channel.BasicPublishAsync("", "pong_queue", true, new BasicProperties(get.BasicProperties), Encoding.UTF8.GetBytes(body.Replace("ping", "pong")));
  }

  await Task.Delay(100);
}
