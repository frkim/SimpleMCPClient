using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using SimpleMcpClient.Core;
using SimpleMcpClient.SampleServer;
using System.Text.Json.Nodes;

var checks = new List<(string Name, Func<Task> Run)>
{
    ("HTTP tools and tool execution", VerifyHttpToolsAsync),
    ("HTTP resources and prompts", VerifyHttpResourcesAndPromptsAsync),
    ("stdio tools and prompts", VerifyStdioAsync),
    ("endpoint validation", VerifyEndpointValidationAsync)
};

var failures = 0;
foreach (var (name, run) in checks)
{
    try
    {
        await run();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception exception)
    {
        failures++;
        Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
    }
}

Console.WriteLine($"{checks.Count - failures}/{checks.Count} smoke checks passed.");
return failures == 0 ? 0 : 1;

static async Task VerifyHttpToolsAsync()
{
    await using var server = await StartServerAsync();
    await using var client = await McpClientSession.ConnectHttpAsync(server.Endpoint, allowPrivateNetwork: true);

    var tools = await client.ListToolsAsync();
    Check(tools.Count == 2, "Expected echo and add tools.");
    Check(tools[0]?["name"]?.GetValue<string>() == "echo", "Expected echo to be the first tool.");

    var echo = await client.CallToolAsync("echo", new JsonObject { ["text"] = "hello MCP" });
    Check(echo["content"]?[0]?["text"]?.GetValue<string>() == "hello MCP", "Echo tool returned the wrong content.");
    Check(echo["isError"]?.GetValue<bool>() == false, "Echo tool should succeed.");

    var sum = await client.CallToolAsync("add", new JsonObject { ["left"] = 19, ["right"] = 23 });
    Check(sum["content"]?[0]?["text"]?.GetValue<string>() == "42", "Add tool returned the wrong sum.");
}

static async Task VerifyHttpResourcesAndPromptsAsync()
{
    await using var server = await StartServerAsync();
    await using var client = await McpClientSession.ConnectHttpAsync(server.Endpoint, allowPrivateNetwork: true);

    var resources = await client.ListResourcesAsync();
    Check(resources.Count == 1, "Expected one sample resource.");
    var resource = await client.ReadResourceAsync(resources[0]?["uri"]?.GetValue<string>() ?? "");
    Check(
        resource["contents"]?[0]?["text"]?.GetValue<string>() == "Hello from an in-memory MCP resource.",
        "Resource contents did not match.");

    var prompts = await client.ListPromptsAsync();
    Check(prompts.Count == 1, "Expected one sample prompt.");
    var prompt = await client.GetPromptAsync("greeting", new JsonObject { ["name"] = "Ada" });
    Check(
        prompt["messages"]?[0]?["content"]?["text"]?.GetValue<string>() == "Say hello to Ada.",
        "Prompt was not rendered with the supplied argument.");
}

static async Task VerifyStdioAsync()
{
    var serverPath = Path.Combine(AppContext.BaseDirectory, "SimpleMcpClient.SampleServer.dll");
    Check(File.Exists(serverPath), "The sample server assembly was not copied beside the smoke-test runner.");
    await using var client = await McpClientSession.ConnectStdioAsync("dotnet", [serverPath, "--stdio"]);
    Check((await client.ListToolsAsync()).Count == 2, "stdio transport did not list tools.");
    var prompt = await client.GetPromptAsync("greeting", new JsonObject { ["name"] = "CLI" });
    Check(
        prompt["messages"]?[0]?["content"]?["text"]?.GetValue<string>() == "Say hello to CLI.",
        "stdio transport did not retrieve the prompt.");
}

static async Task VerifyEndpointValidationAsync()
{
    await ExpectAsync<ArgumentException>(() =>
        McpEndpointValidator.ValidateAsync(new Uri("file:///etc/passwd"), allowPrivateNetwork: true, allowedHosts: null));
    await ExpectAsync<ArgumentException>(() =>
        McpEndpointValidator.ValidateAsync(new Uri("http://127.0.0.1:5005/mcp"), allowPrivateNetwork: false, allowedHosts: null));
    await ExpectAsync<ArgumentException>(() =>
        McpEndpointValidator.ValidateAsync(
            new Uri("http://mcp.example.test/mcp"),
            allowPrivateNetwork: false,
            allowedHosts: "mcp.example.test"));
    await McpEndpointValidator.ValidateAsync(
        new Uri("https://mcp.example.test/mcp"),
        allowPrivateNetwork: false,
        allowedHosts: "mcp.example.test");
    await ExpectAsync<ArgumentException>(() =>
        McpClientSession.ConnectHttpAsync(
            new Uri("http://127.0.0.1:5005/mcp"),
            bearerToken: "token\r\nX-Injected: value",
            allowPrivateNetwork: true));
}

static async Task<RunningSampleServer> StartServerAsync()
{
    var app = SampleMcpServerHost.Create([], "http://127.0.0.1:0");
    await app.StartAsync();
    var addresses = app.Services.GetRequiredService<IServer>()
        .Features.Get<IServerAddressesFeature>()?.Addresses;
    var address = addresses?.SingleOrDefault()
        ?? throw new InvalidOperationException("The sample server did not start on an ephemeral port.");
    return new RunningSampleServer(app, new Uri($"{address}/mcp"));
}

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static async Task ExpectAsync<TException>(Func<Task> action) where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

sealed class RunningSampleServer(WebApplication app, Uri endpoint) : IAsyncDisposable
{
    public Uri Endpoint { get; } = endpoint;

    public async ValueTask DisposeAsync()
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }
}
