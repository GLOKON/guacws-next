# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project overview

GuacWS is a .NET 10 ASP.NET Core server that proxies WebSocket connections from a browser-based Guacamole client to `guacd` (the Apache Guacamole proxy daemon) over raw TCP. It replaces `guacamole-client`'s Java/Tomcat server. Connection parameters (host, credentials, protocol settings) are supplied by an external caller as an encrypted token rather than being stored server-side — this server has no database and no user management of its own.

## Build & run

```bash
dotnet build ./GLOKON.GuacWS.Server.csproj
dotnet run --project ./GLOKON.GuacWS.Server.csproj
```

There is no test project in this repo (`dotnet test` has nothing to run). There is no separate lint step; `.editorconfig` defines analyzer/style rules enforced by the C# analyzers built into `dotnet build`.

Publishing a self-contained, ReadyToRun single-file binary (as CI does):

```bash
dotnet publish ./GLOKON.GuacWS.Server.csproj --configuration Release --runtime linux-musl-x64 --output ./dist
```

The server needs a running `guacd` instance to connect to (default `127.0.0.1:4822`, configured via `Guac:GuacD`). Docker image (`Dockerfile`) builds on top of `guacamole/guacd`, running both `guacd` and the .NET server side by side under `supervisord` (see `docker/supervisor.conf`, `scripts/supervisor/*.conf`). `scripts/setup.sh` and `scripts/install-*.sh` are for provisioning a bare-metal/systemd host instead (see `scripts/systemd/*.service`).

Config is entirely via `appsettings.json` / environment variables (standard ASP.NET Core config binding, e.g. `Guac__UserDriveRoot`) — see `appsettings.json` for the full shape of `Server`, `WebSocket`, `Cipher`, and `Guac` sections.

## Architecture

### Connection lifecycle

1. A client connects to `/` (any path) via WebSocket. `WebSocketConnectionsMiddleware` (`Middlewares/WebSocketConnectionsMiddleware.cs`) intercepts WebSocket upgrade requests before they reach routing.
2. Auth happens via a custom scheme, `TokenAuthenticationHandler` (`Infrastructure/Token/TokenAuthenticationHandler.cs`), which reads a `token` query-string parameter (not a header, since this is a WebSocket handshake) instead of the usual Authorization header.
3. The token is base64 → AES-decrypted (`Cipher/SymmetricCipher.cs`, key/mode from `Cipher` config) → JSON-deserialized into a `Token` containing a `ConnectionProfile` (host/protocol/settings for the target machine). This profile is the sole source of truth for what `guacd` connects to; it is never trusted from the client directly.
4. Any additional query-string parameters on the WebSocket URL are merged into `ConnectionProfile.Settings`, but only if the parameter name is present in `Guac:AllowedParameters` (global + per-connection-type allowlist in `appsettings.json`). This is the mechanism that lets a caller override things like `width`/`height`/`dpi` without being able to inject arbitrary guacd connect parameters (e.g. `password`).
5. The authenticated `ConnectionProfile` is stashed as a claim and re-parsed once the middleware accepts the WebSocket.
6. `WebSocketConnectionsMiddleware` creates a `GuacConnection` (`Guac/GuacConnection.cs`), which owns one `WebSocketConnection` (`Infrastructure/WebSocketConnection.cs`) and one `GuacDClient` (`Guac/GuacDClient.cs`), and registers it in `GuacConnectionsService` (`Services/GuacConnectionsServiceImpl.cs`) keyed by a new GUID.
7. `GuacConnection.StartAsync` connects to `guacd`, sends a `select` for the protocol (or an existing connection ID for connection sharing/joining), then pumps bytes bidirectionally between the WebSocket and the `guacd` TCP socket using `System.IO.Pipelines` (`Task.WhenAny` over four loops: guacd reader/writer, websocket reader/writer).
8. On the first round-trip it intercepts and answers the Guacamole protocol handshake itself (`TrySendGuacDHandshakeReplyAsync`) — filling in `size`/`audio`/`video`/`image`/`timezone`/`name`/`connect` args from `ConnectionProfile.Settings` — before letting raw traffic flow through untouched in both directions.
9. A background activity monitor pings the client periodically and force-closes the connection if idle beyond `Guac:Timeout`.

### Guacamole wire protocol

`Guac/GuacProtocol.cs` implements the length-prefixed protocol Guacamole uses on the wire (`"5.hello,3.foo;"` = `OPCODE,ARG,...;` where each element is prefixed by its UTF-8 length). All manual protocol construction goes through `FormatProtocolMessage`/`GetData` — don't hand-roll this format elsewhere.

### File uploads / drive sharing

`Controllers/UploadController.cs` is the only REST (non-WebSocket) endpoint, reusing the same token auth scheme. Per-connection uploads land in a per-session temp directory (`GuacConnection.UserDrive`, created/torn down alongside the connection when the profile's `enable-drive` setting is true). `DistributeAsync` fans a single upload out to every active connection sharing the same `Group`, gated by the `x-drive-distribution` setting — this is how "drag a file onto one session, it appears on every session in the group" works.

### Key abstractions

- `IGuacConnection` / `IGuacConnectionsService` — the connection registry, used by both the middleware (create/remove) and the upload controller (look up an active session's user drive and group).
- `BaseConnectionProfile<T>` is generic over the settings-value type: `JsonConnectionProfile` (`T = JsonElement`, straight off the wire) is normalized into `ConnectionProfile` (`T = string`) via `ConnectionProfile.FromJsonConnectionProfile`, since guacd only speaks string values.
- Everything under `Guac/`, `Infrastructure/`, `Middlewares/`, and `Services/` implementation classes are `internal` — only the interfaces and DTOs needed across assembly/DI boundaries are `public`.
- `GlobalStore` is a DI singleton holding pre-encoded ping protocol bytes shared across all connections (avoids re-encoding the same ping message per-connection per-tick).

### Options binding

All config sections (`ServerOptions`, `WebSocketConnectionsOptions`, `CipherOptions`, `GuacOptions`, `TokenAuthenticationOptions`, etc.) are bound in `Startup.ConfigureServices` from the matching `appsettings.json` section and injected via `IOptions<T>`/`IOptionsMonitor<T>`. When adding a new configurable value, add the property to the relevant `*Options` class and the default to `appsettings.json`, rather than reading `IConfiguration` directly.

### Kestrel listener setup

`Program.cs` (not `Startup.cs`) is where Kestrel bindings are configured — HTTP, HTTPS (via static cert, Let's Encrypt/LettuceEncrypt, or the ASP.NET dev cert), Unix sockets, and named pipes are all optionally enabled based on `ServerOptions`. This is unusual for a typical ASP.NET Core app (normally done via `appsettings.json` `Kestrel` section or `Configure`) — it's done here because listener choice depends on runtime logic (which cert source is configured, dev vs. prod).

`Startup` is not wired up via `UseStartup<Startup>()` — `Program.cs` builds a `WebApplicationBuilder` and calls `Startup.ConfigureServices`/`Startup.Configure` on it directly. This is because `ConfigureKestrel` needs `ServerOptions` resolved from DI (via `kestrelOptions.ApplicationServices`) before the app is built, which requires `Startup.ConfigureServices` to have already registered it on `builder.Services`.
