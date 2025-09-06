using System.Text;
using Microsoft.AspNetCore.Mvc;
using RabbitMQ.Client;
using sync_to_async;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<SyncToAsync<string, string>>();
builder.Services.AddSingleton(sp =>
    {
        var connString = builder.Configuration["Rmq"]!;
        var factory = new ConnectionFactory
        {
            Uri = new Uri(connString)
        };

        return factory.CreateConnectionAsync().Result;
    });


builder.Services.AddSingleton<SyncRabbitMq>();

builder.Services.AddHostedService(sp =>
    {
        var connection = sp.GetRequiredService<IConnection>();
        using var channel = connection.CreateChannelAsync().Result;
        channel.ExchangeDeclareAsync("ping", type: ExchangeType.Fanout, durable: true).Wait();
        channel.ExchangeDeclareAsync("pong", type: ExchangeType.Fanout, durable: true).Wait();
        return new SyncRabbitMq(connection, sp.GetRequiredService<SyncToAsync<string, string>>(), sp.GetRequiredService<ILogger<SyncRabbitMq>>());
    });

var app = builder.Build();

app.MapGet("/{req}", async ([FromServices] SyncRabbitMq rmq, [FromRoute] string req, CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Text(await rmq.Publish(req, cancellationToken), "text/plain", Encoding.UTF8);
    }
    catch (TaskCanceledException)
    {
        return Results.Problem("server is shutting down", statusCode: StatusCodes.Status504GatewayTimeout);
    }
});

app.MapPost("/{req}", async (SyncToAsync<string, string> service, [FromRoute] string req, HttpContext context) =>
    {
        using var reader = new StreamReader(context.Request.Body);
        string response = await reader.ReadToEndAsync();
        service.Resolve(req, response);
    })
.Accepts<string>("text/plain");

app.Run();
