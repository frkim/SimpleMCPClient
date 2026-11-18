using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text.Json.Nodes;

namespace SimpleMcpClient.SampleServer;

public static class SampleMcpServerHost
{
    public static WebApplication Create(string[] args, string url = "http://127.0.0.1:5005")
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseUrls(url);
        builder.Logging.ClearProviders();
        var app = builder.Build();

        app.MapPost("/mcp", async (HttpContext context) =>
        {
            try
            {
                var request = await JsonNode.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted)
                    as JsonObject;
                if (request is null)
                {
                    return Results.BadRequest();
                }

                var result = SampleMcpProtocol.Handle(request);
                return result is null ? Results.Accepted() : Results.Json(result);
            }
            catch (InvalidOperationException exception)
            {
                return Results.Json(new JsonObject
                {
                    ["jsonrpc"] = "2.0",
                    ["id"] = null,
                    ["error"] = new JsonObject { ["code"] = -32601, ["message"] = exception.Message }
                }, statusCode: StatusCodes.Status200OK);
            }
        });

        app.MapGet("/health/ready", () => Results.Ok(new { status = "ready" }));
        app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
        return app;
    }
}
