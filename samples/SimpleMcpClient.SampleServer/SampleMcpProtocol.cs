using System.Text.Json.Nodes;
using System.Globalization;

namespace SimpleMcpClient.SampleServer;

public static class SampleMcpProtocol
{
    public static JsonObject? Handle(JsonObject request)
    {
        var method = request["method"]?.GetValue<string>();
        var id = request["id"]?.DeepClone();
        if (id is null)
        {
            return null;
        }

        JsonNode result = method switch
        {
            "initialize" => new JsonObject
            {
                ["protocolVersion"] = "2025-11-25",
                ["capabilities"] = new JsonObject
                {
                    ["tools"] = new JsonObject { ["listChanged"] = false },
                    ["resources"] = new JsonObject { ["listChanged"] = false },
                    ["prompts"] = new JsonObject { ["listChanged"] = false }
                },
                ["serverInfo"] = new JsonObject { ["name"] = "SimpleMcpClient sample", ["version"] = "1.0.0" }
            },
            "tools/list" => new JsonObject
            {
                ["tools"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["name"] = "echo",
                        ["description"] = "Echo text back to the caller.",
                        ["inputSchema"] = new JsonObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JsonObject { ["text"] = new JsonObject { ["type"] = "string" } },
                            ["required"] = new JsonArray("text")
                        }
                    },
                    new JsonObject
                    {
                        ["name"] = "add",
                        ["description"] = "Add two numbers.",
                        ["inputSchema"] = new JsonObject
                        {
                            ["type"] = "object",
                            ["properties"] = new JsonObject
                            {
                                ["left"] = new JsonObject { ["type"] = "number" },
                                ["right"] = new JsonObject { ["type"] = "number" }
                            },
                            ["required"] = new JsonArray("left", "right")
                        }
                    }
                }
            },
            "tools/call" => CallTool(request["params"] as JsonObject),
            "resources/list" => new JsonObject
            {
                ["resources"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["uri"] = "memory://welcome",
                        ["name"] = "Welcome note",
                        ["description"] = "A sample resource held in memory.",
                        ["mimeType"] = "text/plain"
                    }
                }
            },
            "resources/read" => ReadResource(request["params"] as JsonObject),
            "prompts/list" => new JsonObject
            {
                ["prompts"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["name"] = "greeting",
                        ["description"] = "Create a greeting for a person.",
                        ["arguments"] = new JsonArray(
                            new JsonObject { ["name"] = "name", ["required"] = true })
                    }
                }
            },
            "prompts/get" => GetPrompt(request["params"] as JsonObject),
            _ => throw new InvalidOperationException($"Unsupported MCP method: {method}")
        };

        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result
        };
    }

    private static JsonObject CallTool(JsonObject? parameters)
    {
        var name = parameters?["name"]?.GetValue<string>();
        var arguments = parameters?["arguments"] as JsonObject ?? new JsonObject();
        var text = name switch
        {
            "echo" => arguments["text"]?.GetValue<string>() ?? string.Empty,
            "add" => ((arguments["left"]?.GetValue<double>() ?? 0)
                + (arguments["right"]?.GetValue<double>() ?? 0)).ToString(CultureInfo.InvariantCulture),
            _ => throw new InvalidOperationException($"Unknown tool: {name}")
        };

        return new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
            ["isError"] = false
        };
    }

    private static JsonObject ReadResource(JsonObject? parameters)
    {
        var uri = parameters?["uri"]?.GetValue<string>();
        if (uri != "memory://welcome")
        {
            throw new InvalidOperationException($"Unknown resource: {uri}");
        }

        return new JsonObject
        {
            ["contents"] = new JsonArray(new JsonObject
            {
                ["uri"] = uri,
                ["mimeType"] = "text/plain",
                ["text"] = "Hello from an in-memory MCP resource."
            })
        };
    }

    private static JsonObject GetPrompt(JsonObject? parameters)
    {
        var name = parameters?["name"]?.GetValue<string>();
        if (name != "greeting")
        {
            throw new InvalidOperationException($"Unknown prompt: {name}");
        }

        var person = parameters?["arguments"]?["name"]?.GetValue<string>() ?? "friend";
        return new JsonObject
        {
            ["description"] = "A friendly greeting.",
            ["messages"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["content"] = new JsonObject { ["type"] = "text", ["text"] = $"Say hello to {person}." }
            })
        };
    }
}
