using SimpleMcpClient.SampleServer;
using System.Text.Json.Nodes;

if (args.Length > 0 && args[0] == "--stdio")
{
    string? line;
    while ((line = await Console.In.ReadLineAsync()) is not null)
    {
        try
        {
            var request = JsonNode.Parse(line) as JsonObject;
            var response = request is null ? null : SampleMcpProtocol.Handle(request);
            if (response is not null)
            {
                await Console.Out.WriteLineAsync(response.ToJsonString());
                await Console.Out.FlushAsync();
            }
        }
        catch (InvalidOperationException exception)
        {
            Console.Error.WriteLine(exception.Message);
        }
    }
}
else
{
    var app = SampleMcpServerHost.Create(args);
    await app.RunAsync();
}
