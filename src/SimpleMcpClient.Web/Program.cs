using SimpleMcpClient.Core;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://0.0.0.0:8080");
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole();
if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddApplicationInsightsTelemetry();
}
var app = builder.Build();
var webAuthToken = builder.Configuration["WEB_AUTH_TOKEN"];
if (string.IsNullOrWhiteSpace(webAuthToken) && !app.Environment.IsDevelopment())
{
    throw new InvalidOperationException("WEB_AUTH_TOKEN must be configured outside development.");
}

app.Use(async (context, next) =>
{
    context.Response.Headers["Content-Security-Policy"] =
        "default-src 'self'; style-src 'self'; script-src 'self'; connect-src 'self'; img-src 'self' data:; object-src 'none'; base-uri 'self'; frame-ancestors 'none'";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";

    try
    {
        if (context.Request.Path.StartsWithSegments("/api")
            && !string.IsNullOrWhiteSpace(webAuthToken)
            && !HasValidWebAuthToken(context.Request, webAuthToken))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            context.Response.Headers.WWWAuthenticate = "Bearer";
            await context.Response.WriteAsJsonAsync(new { error = "Enter a valid app access token." });
            return;
        }

        await next();
    }
    catch (Exception exception)
    {
        var correlationId = context.TraceIdentifier;
        app.Logger.LogError(
            "Request failed with error type {ErrorType} and correlation id {CorrelationId}",
            exception.GetType().Name,
            correlationId);
        if (!context.Response.HasStarted)
        {
            context.Response.Clear();
            context.Response.StatusCode = exception is ArgumentException
                ? StatusCodes.Status400BadRequest
                : StatusCodes.Status502BadGateway;
            await context.Response.WriteAsJsonAsync(new
            {
                error = exception is ArgumentException ? exception.Message : "The MCP request failed.",
                correlationId
            });
        }
    }
});

app.UseDefaultFiles();
app.UseStaticFiles();
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", () => Results.Ok(new { status = "ready" }));

app.MapPost("/api/tools", async (McpRequest request, HttpContext context) =>
{
    await using var client = await ConnectAsync(request, context.RequestAborted);
    return Results.Ok(await client.ListToolsAsync(context.RequestAborted));
});

app.MapPost("/api/resources", async (McpRequest request, HttpContext context) =>
{
    await using var client = await ConnectAsync(request, context.RequestAborted);
    return Results.Ok(await client.ListResourcesAsync(context.RequestAborted));
});

app.MapPost("/api/prompts", async (McpRequest request, HttpContext context) =>
{
    await using var client = await ConnectAsync(request, context.RequestAborted);
    return Results.Ok(await client.ListPromptsAsync(context.RequestAborted));
});

app.MapPost("/api/tools/call", async (McpRequest request, HttpContext context) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return Results.BadRequest(new { error = "A tool name is required." });
    }

    await using var client = await ConnectAsync(request, context.RequestAborted);
    return Results.Ok(await client.CallToolAsync(request.Name, request.Arguments, context.RequestAborted));
});

app.MapPost("/api/resources/read", async (McpRequest request, HttpContext context) =>
{
    if (string.IsNullOrWhiteSpace(request.Uri))
    {
        return Results.BadRequest(new { error = "A resource URI is required." });
    }

    await using var client = await ConnectAsync(request, context.RequestAborted);
    return Results.Ok(await client.ReadResourceAsync(request.Uri, context.RequestAborted));
});

app.MapPost("/api/prompts/get", async (McpRequest request, HttpContext context) =>
{
    if (string.IsNullOrWhiteSpace(request.Name))
    {
        return Results.BadRequest(new { error = "A prompt name is required." });
    }

    await using var client = await ConnectAsync(request, context.RequestAborted);
    return Results.Ok(await client.GetPromptAsync(request.Name, request.Arguments, context.RequestAborted));
});

app.Run();

static bool HasValidWebAuthToken(HttpRequest request, string expectedToken)
{
    if (!AuthenticationHeaderValue.TryParse(request.Headers.Authorization, out var authorization)
        || !string.Equals(authorization.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase)
        || string.IsNullOrEmpty(authorization.Parameter))
    {
        return false;
    }

    return CryptographicOperations.FixedTimeEquals(
        Encoding.UTF8.GetBytes(authorization.Parameter),
        Encoding.UTF8.GetBytes(expectedToken));
}

async Task<McpClientSession> ConnectAsync(McpRequest request, CancellationToken cancellationToken)
{
    if (!Uri.TryCreate(request.Endpoint, UriKind.Absolute, out var endpoint))
    {
        throw new ArgumentException("Enter a valid MCP endpoint URL.");
    }

    return await McpClientSession.ConnectHttpAsync(
        endpoint,
        request.BearerToken,
        allowPrivateNetwork: app.Environment.IsDevelopment(),
        allowedHosts: builder.Configuration["MCP_ALLOWED_HOSTS"],
        cancellationToken);
}

public sealed record McpRequest(
    string Endpoint,
    string? BearerToken,
    string? Name,
    JsonObject? Arguments,
    string? Uri);
