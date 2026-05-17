# amFTPd Plugin API

amFTPd's plugin system lets you extend the daemon with custom SITE commands, external authentication providers, and FTP lifecycle event handlers — without modifying the core binary. Plugins are isolated in their own `AssemblyLoadContext` so they can carry their own NuGet dependencies; on REHASH the old context is unloaded and a fresh one is created.

---

## Quick start

1. Create a .NET 10 class library project.
2. Add a reference to `amFTPd.Plugin.Abstractions` (NuGet or project reference).
3. Implement at least `IAmFtpdPlugin` and whichever extension interfaces you need.
4. Run `dotnet publish -c Release -r linux-x64 --self-contained false -o out/` (or the target RID of your server).
5. Drop the publish output folder next to `amftpd.json`.
6. Register the DLL in `amftpd.json` under the `"Plugins"` array.
7. Start or REHASH the daemon.

---

## amftpd.json registration

```json
"Plugins": [
  {
    "Path":    "plugins/MyPlugin/MyPlugin.dll",
    "Enabled": true,
    "Settings": {
      "ApiKey":       "abc123",
      "WebhookUrl":   "https://hooks.example.com/ftp"
    }
  },
  {
    "Path":    "plugins/MyAuth/MyAuth.dll",
    "Enabled": false
  }
]
```

`Path` is resolved relative to the directory containing `amftpd.json`. The directory must contain a `.deps.json` file so that the loader can resolve the plugin's NuGet dependencies. This file is produced automatically by `dotnet publish`.

`Settings` is an arbitrary `Dictionary<string, string>` passed verbatim to `IPluginContext.Settings`.

---

## Interfaces

### `IAmFtpdPlugin` — required for every plugin

```csharp
public interface IAmFtpdPlugin
{
    string Name        { get; }
    string Version     { get; }
    string Author      { get; }
    string Description { get; }

    Task InitializeAsync(IPluginContext context, CancellationToken ct = default);
    Task ShutdownAsync(CancellationToken ct = default);
}
```

`InitializeAsync` is called once when the plugin is loaded (daemon start or REHASH). Open connections, read files, and parse settings here. `ShutdownAsync` is called during graceful shutdown or before REHASH unloads the old context — release all resources.

---

### `ISiteCommandPlugin` — custom SITE sub-commands

```csharp
public interface ISiteCommandPlugin : IAmFtpdPlugin
{
    IReadOnlyList<string>                CommandNames { get; }
    IReadOnlyDictionary<string, string>  HelpLines    { get; }

    Task<PluginSiteResult> ExecuteAsync(PluginSiteContext context, CancellationToken ct);
}
```

`CommandNames` declares which `SITE <verb>` names the plugin handles (case-insensitive). The verbs must not overlap with built-in amFTPd commands. `ExecuteAsync` must write the FTP response to `context.WriteResponseAsync` and return `PluginSiteResult.Done`. If for any reason the command cannot be handled, return `PluginSiteResult.NotHandled` and the daemon sends the standard `502 Unknown SITE command` response.

**PluginSiteContext fields:**

| Property | Type | Description |
|---|---|---|
| `Verb` | `string` | SITE sub-command (upper-case) |
| `Argument` | `string` | Everything after the verb on the command line |
| `Username` | `string` | Authenticated user name |
| `IsAdmin` | `bool` | User has admin flag |
| `IsSiteop` | `bool` | User has siteop flag |
| `RemoteIp` | `string` | Client IP address |
| `CurrentPath` | `string?` | Current virtual working directory |
| `WriteResponseAsync` | `Func<string,CancellationToken,Task>` | Write raw FTP response (include `\r\n`) |
| `PluginContext` | `IPluginContext` | Logging and settings |

---

### `IAuthProviderPlugin` — external authentication

```csharp
public interface IAuthProviderPlugin : IAmFtpdPlugin
{
    Task<PluginAuthResult> AuthenticateAsync(PluginAuthRequest request, CancellationToken ct);
}
```

Providers are tried in config order. The first non-`Passthrough` result wins. If all providers return `Passthrough`, the daemon falls back to its built-in hashed-password store.

When a plugin returns `Authenticated`, the daemon looks up the user in the local user store by username. If the user does not exist locally, the login is denied — auth plugins validate identity but do not create accounts.

**PluginAuthResult statics:**

```csharp
PluginAuthResult.Passthrough      // skip this provider
PluginAuthResult.Authenticated    // accept the login
PluginAuthResult.Reject("reason") // deny with a custom 530 message
```

---

### `IEventHandlerPlugin` — FTP lifecycle events

```csharp
public interface IEventHandlerPlugin : IAmFtpdPlugin
{
    IReadOnlyList<string> HandledEventTypes { get; }
    Task OnEventAsync(PluginFtpEvent ftpEvent, CancellationToken ct);
}
```

Return `["*"]` or an empty list to receive all events, or list specific type names to filter. `OnEventAsync` is invoked asynchronously from a `Task.Run` context — exceptions are logged and swallowed so a broken plugin cannot crash the daemon. Each call has a 30-second timeout.

**Well-known event type names:**

`Upload`, `Download`, `Delete`, `Mkdir`, `Rmdir`, `Login`, `Logout`, `Nuke`, `Unnuke`, `Wipe`, `Pre`, `RaceUpdate`, `RaceComplete`, `ZipscriptStatus`, `Oneliner`, `Request`, `AutoNuke`

**PluginFtpEvent fields (all nullable except `Type` and `Timestamp`):**

| Field | Type | Notes |
|---|---|---|
| `Type` | `string` | Event type name |
| `Timestamp` | `DateTimeOffset` | UTC time of the event |
| `Username` | `string?` | Session user |
| `Group` | `string?` | User's primary group |
| `Section` | `string?` | Section (e.g. `"MP3"`) |
| `VirtualPath` | `string?` | Virtual path of file/dir |
| `ReleaseName` | `string?` | Release directory name |
| `Bytes` | `long` | Transfer size (0 if N/A) |
| `Reason` | `string?` | Nuke reason or other payload |
| `RemoteHost` | `string?` | Client IP |
| `Extra` | `IReadOnlyDictionary<string, object?>?` | Event-specific extras |

---

## IPluginContext

Passed to `InitializeAsync` and available as `PluginSiteContext.PluginContext` during SITE dispatch.

```csharp
public interface IPluginContext
{
    string ConfigDirectory { get; }                           // directory of amftpd.json
    IReadOnlyDictionary<string, string> Settings { get; }    // from amftpd.json "Settings"
    void LogInfo (string message);
    void LogWarn (string message);
    void LogError(string message, Exception? ex = null);
}
```

---

## Combining extension interfaces

A single class may implement any combination of the three extension interfaces. The daemon detects which interfaces are implemented and registers the plugin for each one:

```csharp
public sealed class MyPlugin
    : IAmFtpdPlugin,
      ISiteCommandPlugin,
      IEventHandlerPlugin
{ ... }
```

---

## Building and deploying

```bash
dotnet publish MyPlugin/MyPlugin.csproj \
    -c Release \
    -r linux-x64 \
    --self-contained false \
    -o /srv/amftpd/plugins/MyPlugin/
```

The publish output must include:

- `MyPlugin.dll` — the plugin assembly
- `MyPlugin.deps.json` — dependency manifest (generated automatically)
- Any NuGet dependency DLLs that are not part of the .NET 10 shared framework

Point `amftpd.json` at the DLL:

```json
"Plugins": [
  { "Path": "plugins/MyPlugin/MyPlugin.dll", "Enabled": true }
]
```

---

## REHASH behavior

When the daemon receives a `SITE REHASH` command or a `kill -HUP` signal (Linux), it:

1. Loads a new configuration from disk via `AmFtpdConfigLoader.LoadAsync`.
2. Constructs a new `PluginHost` that loads fresh copies of all enabled plugins (new `AssemblyLoadContext` per DLL).
3. Calls `WireEventHandlers` on the new host.
4. Swaps the live runtime atomically.
5. Calls `DisposeAsync` on the old `PluginHost`, which calls `ShutdownAsync` on each old plugin LIFO and then unloads the `AssemblyLoadContext`.

Because each REHASH creates a completely new `AssemblyLoadContext`, plugins can be updated on disk and activated without restarting the daemon.

---

## Sample plugin

A fully-annotated reference plugin is provided in `Plugins/amFTPd.SamplePlugin/`. It demonstrates all three extension points — `SITE HELLO`, `SITE PING`, a magic-password auth bypass (dev only), and an upload event logger.

To build it alongside the main project, add it to the solution:

```bash
dotnet sln amFTPd.sln add Plugins/amFTPd.SamplePlugin/amFTPd.SamplePlugin.csproj
```

---

## Security considerations

- Plugins run in the same OS process as the daemon with the same privileges. Only load plugins you trust.
- The `AssemblyLoadContext` isolation prevents version conflicts between plugins but is not a security sandbox.
- Avoid storing plaintext credentials in `Settings`; use environment variables or a secrets file that the plugin reads in `InitializeAsync`.
- Auth plugins that return `Authenticated` bypass the built-in password check. Ensure they perform equivalent or stronger verification.
- A misbehaving event handler can delay event processing for up to 30 seconds (the per-call timeout); keep `OnEventAsync` fast or offload to a background queue inside the plugin.
