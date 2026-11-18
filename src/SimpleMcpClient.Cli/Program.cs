using SimpleMcpClient.Core;
using System.Text.Json;
using System.Text.Json.Nodes;

const string usage = """
Simple MCP Client

  simple-mcp-client tools <endpoint>
  simple-mcp-client resources <endpoint>
  simple-mcp-client prompts <endpoint>
  simple-mcp-client call <endpoint> <tool> [arguments-json]
  simple-mcp-client read <endpoint> <resource-uri>
  simple-mcp-client prompt <endpoint> <prompt> [arguments-json]
  simple-mcp-client --stdio <command> [server-args...] -- <operation> [args...]

Set MCP_AUTH_TOKEN to send an Authorization: ****** to an HTTP MCP server.
""";

try
{
    if (args.Length > 0 && args[0] == "--stdio")
    {
        var separator = Array.IndexOf(args, "--", 1);
        if (separator < 2 || separator == args.Length - 1)
        {
            Console.Error.WriteLine(usage);
            return 2;
        }

        await using var stdioClient = await McpClientSession.ConnectStdioAsync(
            args[1],
            args[2..separator]);
        await RunOperationAsync(stdioClient, args[(separator + 1)..]);
        return 0;
    }

    if (args.Length < 2)
    {
        Console.Error.WriteLine(usage);
        return 2;
    }

    await using var client = await McpClientSession.ConnectHttpAsync(
        new Uri(args[1], UriKind.Absolute),
        Environment.GetEnvironmentVariable("MCP_AUTH_TOKEN"),
        allowPrivateNetwork: true);
    await RunOperationAsync(client, args);
    return 0;
}
catch (Exception exception) when (exception is ArgumentException or McpProtocolException or HttpRequestException or JsonException)
{
    Console.Error.WriteLine($"Error: {exception.Message}");
    return 1;
}

static async Task RunOperationAsync(McpClientSession client, string[] arguments)
{
    JsonNode? result = arguments[0] switch
    {
        "tools" => await client.ListToolsAsync(),
        "resources" => await client.ListResourcesAsync(),
        "prompts" => await client.ListPromptsAsync(),
        "call" when arguments.Length >= 3 => await client.CallToolAsync(
            arguments[2],
            ParseArguments(arguments, 3)),
        "read" when arguments.Length >= 3 => await client.ReadResourceAsync(arguments[2]),
        "prompt" when arguments.Length >= 3 => await client.GetPromptAsync(
            arguments[2],
            ParseArguments(arguments, 3)),
        _ => throw new ArgumentException("Unknown operation or missing operation arguments.")
    };

    Console.WriteLine(result.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
}

static JsonObject? ParseArguments(string[] arguments, int index)
{
    if (arguments.Length <= index)
    {
        return null;
    }

    return JsonNode.Parse(arguments[index]) as JsonObject
        ?? throw new ArgumentException("Arguments must be a JSON object.");
}
