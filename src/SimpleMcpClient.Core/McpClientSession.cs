using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace SimpleMcpClient.Core;

public sealed class McpClientSession : IAsyncDisposable
{
    internal const string ProtocolVersion = "2026-07-28";
    private readonly IMcpTransport _transport;
    private int _requestId;

    private McpClientSession(IMcpTransport transport) => _transport = transport;

    public static async Task<McpClientSession> ConnectHttpAsync(
        Uri endpoint,
        string? bearerToken = null,
        bool allowPrivateNetwork = false,
        string? allowedHosts = null,
        CancellationToken cancellationToken = default)
    {
        await McpEndpointValidator.ValidateAsync(endpoint, allowPrivateNetwork, allowedHosts, cancellationToken);
        if (bearerToken is { Length: > 4096 } ||
            bearerToken?.Any(character =>
                !char.IsAsciiLetterOrDigit(character) && !"-._~+/=".Contains(character)) == true)
        {
            throw new ArgumentException("The bearer token contains invalid characters or exceeds 4096 characters.");
        }

        var client = new McpClientSession(new HttpMcpTransport(endpoint, bearerToken));
        try
        {
            await client.InitializeAsync(cancellationToken);
            return client;
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    public static async Task<McpClientSession> ConnectStdioAsync(
        string command,
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken = default)
    {
        var client = new McpClientSession(new StdioMcpTransport(command, arguments));
        try
        {
            await client.InitializeAsync(cancellationToken);
            return client;
        }
        catch
        {
            await client.DisposeAsync();
            throw;
        }
    }

    public Task<JsonArray> ListToolsAsync(CancellationToken cancellationToken = default) =>
        ListAllAsync("tools/list", "tools", cancellationToken);

    public Task<JsonArray> ListResourcesAsync(CancellationToken cancellationToken = default) =>
        ListAllAsync("resources/list", "resources", cancellationToken);

    public Task<JsonArray> ListPromptsAsync(CancellationToken cancellationToken = default) =>
        ListAllAsync("prompts/list", "prompts", cancellationToken);

    public Task<JsonObject> CallToolAsync(
        string name,
        JsonObject? arguments = null,
        CancellationToken cancellationToken = default) =>
        SendRequestAsync("tools/call", new JsonObject
        {
            ["name"] = name,
            ["arguments"] = arguments?.DeepClone() ?? new JsonObject()
        }, cancellationToken);

    public Task<JsonObject> ReadResourceAsync(string uri, CancellationToken cancellationToken = default) =>
        SendRequestAsync("resources/read", new JsonObject { ["uri"] = uri }, cancellationToken);

    public Task<JsonObject> GetPromptAsync(
        string name,
        JsonObject? arguments = null,
        CancellationToken cancellationToken = default) =>
        SendRequestAsync("prompts/get", new JsonObject
        {
            ["name"] = name,
            ["arguments"] = arguments?.DeepClone() ?? new JsonObject()
        }, cancellationToken);

    private async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await SendRequestAsync("initialize", new JsonObject
        {
            ["protocolVersion"] = ProtocolVersion,
            ["capabilities"] = new JsonObject(),
            ["clientInfo"] = new JsonObject
            {
                ["name"] = "SimpleMcpClient",
                ["version"] = "1.0.0"
            }
        }, cancellationToken);
        await _transport.SendAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "notifications/initialized"
        }, cancellationToken);
    }

    private async Task<JsonArray> ListAllAsync(string method, string resultKey, CancellationToken cancellationToken)
    {
        var all = new JsonArray();
        string? cursor = null;
        for (var page = 0; page < 100; page++)
        {
            var parameters = new JsonObject();
            if (cursor is not null)
            {
                parameters["cursor"] = cursor;
            }

            var response = await SendRequestAsync(method, parameters, cancellationToken);
            if (response[resultKey] is not JsonArray items)
            {
                throw new McpProtocolException($"The server returned an invalid {resultKey} list.");
            }

            foreach (var item in items)
            {
                all.Add(item?.DeepClone());
            }

            cursor = response["nextCursor"]?.GetValue<string>();
            if (string.IsNullOrEmpty(cursor))
            {
                return all;
            }
        }

        throw new McpProtocolException($"The server exceeded the 100-page limit for {resultKey}.");
    }

    private async Task<JsonObject> SendRequestAsync(
        string method,
        JsonObject parameters,
        CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _requestId);
        var response = await _transport.SendAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters
        }, cancellationToken);

        if (response is null)
        {
            throw new McpProtocolException($"The server did not respond to {method}.");
        }

        if (response["error"] is JsonObject error)
        {
            throw new McpProtocolException(error["message"]?.GetValue<string>() ?? "The MCP request failed.");
        }

        return response["result"] as JsonObject
            ?? throw new McpProtocolException($"The server returned an invalid result for {method}.");
    }

    public ValueTask DisposeAsync() => _transport.DisposeAsync();
}

public sealed class McpProtocolException(string message) : Exception(message);

internal interface IMcpTransport : IAsyncDisposable
{
    Task<JsonObject?> SendAsync(JsonObject message, CancellationToken cancellationToken);
}

internal sealed class HttpMcpTransport : IMcpTransport
{
    private readonly HttpClient _httpClient;
    private readonly Uri _endpoint;
    private readonly string? _bearerToken;
    private string? _sessionId;
    private string? _protocolVersion;

    public HttpMcpTransport(Uri endpoint, string? bearerToken)
    {
        _endpoint = endpoint;
        _bearerToken = bearerToken;
        _httpClient = new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public async Task<JsonObject?> SendAsync(JsonObject message, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
        {
            Content = new StringContent(message.ToJsonString(), Encoding.UTF8, "application/json")
        };
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Accept.ParseAdd("text/event-stream");

        if (_bearerToken is not null)
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _bearerToken);
        }

        if (_sessionId is not null)
        {
            request.Headers.TryAddWithoutValidation("MCP-Session-Id", _sessionId);
        }

        if (_protocolVersion is not null)
        {
            request.Headers.TryAddWithoutValidation(
                "MCP-Protocol-Version",
                _protocolVersion ?? McpClientSession.ProtocolVersion);
        }

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        if (message["method"]?.GetValue<string>() == "initialize")
        {
            _sessionId = response.Headers.TryGetValues("MCP-Session-Id", out var sessionIds)
                ? sessionIds.FirstOrDefault()
                : null;
        }

        if (response.StatusCode == HttpStatusCode.Accepted)
        {
            return null;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new McpProtocolException($"MCP server returned HTTP {(int)response.StatusCode}.");
        }

        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        var contentType = response.Content.Headers.ContentType?.MediaType;
        var json = contentType == "text/event-stream" ? GetEventData(body) : body;
        var parsed = JsonNode.Parse(json) as JsonObject
            ?? throw new McpProtocolException("The MCP server returned an invalid JSON-RPC response.");

        if (message["method"]?.GetValue<string>() == "initialize")
        {
            _protocolVersion = parsed["result"]?["protocolVersion"]?.GetValue<string>()
                ?? McpClientSession.ProtocolVersion;
        }

        return parsed;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_sessionId is not null)
            {
                using var request = new HttpRequestMessage(HttpMethod.Delete, _endpoint);
                if (_bearerToken is not null)
                {
                    request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
                        "Bearer",
                        _bearerToken);
                }

                request.Headers.TryAddWithoutValidation("MCP-Session-Id", _sessionId);
                if (_protocolVersion is not null)
                {
                    request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", _protocolVersion);
                }

                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                using var response = await _httpClient.SendAsync(request, timeout.Token);
            }
        }
        catch (HttpRequestException)
        {
        }
        catch (OperationCanceledException)
        {
        }

        _httpClient.Dispose();
    }

    private static string GetEventData(string body)
    {
        var data = body.Split('\n')
            .Where(line => line.StartsWith("data:", StringComparison.Ordinal))
            .Select(line => line[5..].Trim())
            .FirstOrDefault();
        return data ?? throw new McpProtocolException("The MCP server sent an empty event stream.");
    }
}

internal sealed class StdioMcpTransport : IMcpTransport
{
    private readonly Process _process;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public StdioMcpTransport(string command, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(command)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["DOTNET_NOLOGO"] = "1";
        _process = Process.Start(startInfo) ?? throw new McpProtocolException("The MCP server process could not be started.");
    }

    public async Task<JsonObject?> SendAsync(JsonObject message, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await _process.StandardInput.WriteLineAsync(message.ToJsonString().AsMemory(), cancellationToken);
            await _process.StandardInput.FlushAsync(cancellationToken);

            if (message["id"] is null)
            {
                return null;
            }

            var line = await _process.StandardOutput.ReadLineAsync(cancellationToken);
            if (line is null)
            {
                throw new McpProtocolException("The MCP server process closed its output stream.");
            }

            return JsonNode.Parse(line) as JsonObject
                ?? throw new McpProtocolException("The MCP server process returned an invalid JSON-RPC response.");
        }
        finally
        {
            _gate.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
        }

        _process.Dispose();
        _gate.Dispose();
        return ValueTask.CompletedTask;
    }
}

public static class McpEndpointValidator
{
    public static async Task ValidateAsync(
        Uri endpoint,
        bool allowPrivateNetwork,
        string? allowedHosts,
        CancellationToken cancellationToken = default)
    {
        if (!endpoint.IsAbsoluteUri || endpoint.UserInfo.Length != 0 ||
            endpoint.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(endpoint.Fragment))
        {
            throw new ArgumentException("Enter an absolute HTTP or HTTPS MCP endpoint without user info or a fragment.");
        }

        if (allowPrivateNetwork)
        {
            return;
        }

        if (endpoint.Scheme != Uri.UriSchemeHttps)
        {
            throw new ArgumentException("HTTPS is required for MCP endpoints outside local development.");
        }

        var permittedHosts = (allowedHosts ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (permittedHosts.Contains(endpoint.Host, StringComparer.OrdinalIgnoreCase))
        {
            return;
        }

        throw new ArgumentException("This MCP host is not allowed. Add it to MCP_ALLOWED_HOSTS to permit access.");
    }
}
