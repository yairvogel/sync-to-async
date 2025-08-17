using Microsoft.AspNetCore.Mvc;
using sync_to_async;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<SyncToAsync<string, string>>();

var app = builder.Build();

app.MapGet("/{req}", async (SyncToAsync<string, string> service, [FromRoute] string req, CancellationToken cancellationToken) =>
{
    try
    {
        return Results.Ok(await service.RequestAsync(req, cancellationToken));
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
