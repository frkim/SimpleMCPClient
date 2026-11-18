# Security Policy

## Reporting a vulnerability

Please report vulnerabilities through GitHub's private vulnerability reporting for this repository. Do not open a public issue containing exploit details or credentials.

The hosted web API requires the `WEB_AUTH_TOKEN` deployment secret, and the proxy permits only HTTPS MCP destinations explicitly listed in `MCP_ALLOWED_HOSTS`. Treat both the app access token and MCP server bearer tokens as sensitive: they are not persisted by the app and must not be added to logs or source control.

## Supported versions

Only the latest version on the default branch is supported.
