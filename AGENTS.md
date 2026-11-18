# Simple MCP Client

This repository provides a single-process ASP.NET Core web client, a .NET CLI, and an in-memory sample MCP server. The web app is deployed as a container to Azure Container Apps; no application database or persistent user data is used.

- **Web**: ASP.NET Core 10, static HTML/CSS/JavaScript served with same-process request handlers
- **CLI**: .NET 10
- **MCP**: Streamable HTTP and stdio client transports
- **Hosting**: Azure Container Apps, provisioned with Bicep

## Setup and commands

Use the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0). The repository's `NuGet.config` uses the Microsoft-protected feed for all builds, including Docker and GitHub Actions; see the [package-feed standard](https://github.com/frkim/ai-coding-standards/blob/main/standards/development/package-feeds.md).

| Task | Command |
| --- | --- |
| Run sample server | `dotnet run --project samples/SimpleMcpClient.SampleServer` |
| Run web app | `ASPNETCORE_ENVIRONMENT=Development dotnet run --project src/SimpleMcpClient.Web` |
| Lint/format | `dotnet format SimpleMcpClient.slnx --verify-no-changes --no-restore` |
| Build | `dotnet build SimpleMcpClient.slnx --configuration Release -warnaserror` |
| Test | `dotnet run --project tests/SimpleMcpClient.SmokeTests --configuration Release` |
| Validate Bicep | `az bicep lint --file infra/main.bicep && az bicep build --file infra/main.bicep` |

## Project layout

```text
src/SimpleMcpClient.Core/       MCP protocol client and transports
src/SimpleMcpClient.Web/        same-process web UI and request handlers
src/SimpleMcpClient.Cli/        command-line client
samples/SimpleMcpClient.SampleServer/ in-memory MCP test server
tests/SimpleMcpClient.SmokeTests/     end-to-end smoke-test runner
infra/                          Azure Container Apps Bicep
```

## Non-negotiables

- Keep all application state in memory. Do not add a database or persistent credential storage.
- Require the exact `MCP_ALLOWED_HOSTS` allowlist for production web requests; never weaken endpoint validation to make a connection work.
- Do not log bearer tokens or MCP payloads. Return a correlation ID with actionable errors.
- Keep Azure secrets in GitHub Actions secrets and workload registry pulls on managed identity.
- Keep dependencies pinned in NuGet lock files; use the repository's protected feed for all builds.
- Update this README when commands, security requirements, or deployment behavior change.
- Follow the [ai-coding-standards development](https://github.com/frkim/ai-coding-standards/blob/main/standards/development/development.md), [Azure](https://github.com/frkim/ai-coding-standards/blob/main/standards/azure/azure.md), [GitHub](https://github.com/frkim/ai-coding-standards/blob/main/standards/github/github.md), and [security](https://github.com/frkim/ai-coding-standards/blob/main/standards/security/security.md) standards.
