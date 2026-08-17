# GuacWS

A .NET 10 ASP.NET Core server that proxies WebSocket connections from a browser-based [Apache Guacamole](https://guacamole.apache.org/) client to `guacd` over raw TCP. It's a drop-in replacement for `guacamole-client`'s Java/Tomcat server, with no database and no server-side user management: connection details (host, credentials, protocol settings) are supplied per-request by the caller as an encrypted, opaque token.

## How it works

1. A browser opens a WebSocket to the server (any path) with an encrypted `token` query-string parameter.
2. The server decrypts the token, deserializes it into a connection profile (protocol, host, and Guacamole connect parameters), and opens a TCP connection to `guacd`.
3. Bytes are pumped bidirectionally between the WebSocket and `guacd` for the life of the session, with the server transparently completing the initial Guacamole protocol handshake on the client's behalf.
4. A small allowlisted set of extra query-string parameters (e.g. `width`, `height`, `dpi`) can override values in the token's connection profile per-connection, without letting the caller inject arbitrary `guacd` parameters.

Because the connection profile — including credentials — never lives in a database or is trusted from client input, whatever issues the token (your application backend) is the sole authority over what a session is allowed to connect to.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download) to build
- A running [`guacd`](https://guacamole.apache.org/doc/gug/guacamole-architecture.html) instance to connect to (default `127.0.0.1:4822`)

## Build & run

```bash
dotnet build ./GLOKON.GuacWS.Server.csproj
dotnet run --project ./GLOKON.GuacWS.Server.csproj
```

There's no test project (`dotnet test` has nothing to run) and no separate lint step — `.editorconfig` defines the analyzer/style rules enforced by `dotnet build`.

To publish a self-contained, ReadyToRun single-file binary (as CI does):

```bash
dotnet publish ./GLOKON.GuacWS.Server.csproj --configuration Release --runtime linux-musl-x64 --output ./dist
```

Swap `linux-musl-x64` for another [RID](https://learn.microsoft.com/dotnet/core/rid-catalog) (`win-x64`, `linux-x64`, `linux-arm64`, ...) to target a different platform.

## Configuration

All configuration is standard ASP.NET Core config binding — `appsettings.json`, environment variables (e.g. `Guac__UserDriveRoot`), or any other configured provider. See `appsettings.json` for the full default shape. Key sections:

| Section | Purpose |
|---|---|
| `Server` | Listener setup: bind address, HTTP/HTTPS ports, Unix socket / named pipe listeners, TLS (static cert, Let's Encrypt via LettuceEncrypt, or the ASP.NET dev cert), max upload size, trusted reverse-proxy CIDRs for `X-Forwarded-*` |
| `WebSocket` | Allowed CORS origins, permessage-deflate compression, receive buffer size, close timeout |
| `Cipher` | Symmetric algorithm/mode/key size used to decrypt incoming tokens |
| `Guac` | `guacd` host/port/socket tuning, the on-disk root for per-session upload drives, the `AllowedParameters` allowlist (see below), ping frequency, and idle timeout |

### Tokens

A connection token is:

1. A JSON object `{ "connection": { ... } }` matching `ConnectionProfile` (protocol `type`, target `id`/`group`, and a `settings` dictionary of Guacamole connect parameters such as `hostname`, `username`, `password`, etc.)
2. Encrypted with the symmetric cipher/key configured under `Cipher`, producing ciphertext + IV
3. Wrapped as `{ "iv": "<base64>", "value": "<base64>" }` and base64-encoded again

The result is passed as `?token=<...>` on the WebSocket URL (e.g. `wss://host/?token=...`). Supported connection types (`ConnectionProfile.Type`) are `rdp`, `ssh`, `vnc`, `telnet`, and `k8s`.

### Allowed parameters

`Guac:AllowedParameters` is an allowlist of query-string parameter names a caller may pass alongside the token to override/extend the token's `settings`, split into a `Global` list and a per-connection-`Connection` list (e.g. SSH/Telnet font settings). Anything not on this list is ignored — this is what stops a caller from injecting connect parameters (like `password`) that weren't in the encrypted token.

## File uploads

`POST /api/upload/connection/{id}` uploads one or more files into the requesting session's per-connection drive (only if the token's `enable-drive` setting enabled it for that session). `POST /api/upload/distribute` fans a single upload out to every other active session sharing the same `group`, gated by the token's `x-drive-distribution` setting. Both endpoints use the same token-based auth as the WebSocket endpoint.

## Deployment

### Docker

The provided `Dockerfile` builds on top of `guacamole/guacd`, running both `guacd` and this server side by side under `supervisord` (see `docker/supervisor.conf`). It expects a published `./dist` (see the `dotnet publish` command above) and exposes `8080`/`8081`. Volumes: `/user-drives` (upload staging) and `/certs` (static TLS cert).

### Bare metal / systemd

`scripts/setup.sh` provisions a host (creates the service user, installs dependencies via `scripts/install-deps-{debian,rhel}.sh`, optionally builds `guacd`/Ghostscript from source) for use with the unit files in `scripts/systemd/`.

## CI/CD

- `.github/workflows/build.yml` / `build-server.yml`: builds and publishes the server for each supported RID on every push to a feature branch, and (via `release.yml`) on `main`/`beta`/`alpha` — attaching zipped binaries to the GitHub release and pushing multi-arch Docker images.
- `.github/workflows/release.yml`: runs [`semantic-release`](https://semantic-release.gitbook.io/) to compute the next version from commit messages and cut a GitHub release.
