using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<SyncToAsync>();

var app = builder.Build();

app.MapGet("/{req}", async (SyncToAsync service, [FromRoute] string req, CancellationToken cancellationToken) =>
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
app.MapPost("/{req}", async (SyncToAsync service, [FromRoute] string req, HttpContext context) =>
    {
        using var reader = new StreamReader(context.Request.Body);
        string response = await reader.ReadToEndAsync();
        service.Resolve(req, response);
    })
.Accepts<string>("text/plain");

app.Run();
