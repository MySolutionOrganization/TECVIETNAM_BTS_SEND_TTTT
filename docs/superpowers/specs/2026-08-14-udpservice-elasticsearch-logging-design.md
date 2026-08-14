# UdpService Elasticsearch Logging — Design

## Goal

Ship UdpService's application logs to Elasticsearch (`https://elasticsearch.qlgd.edu.vn`) for centralized observability, without disturbing the existing log4net-based local file logging that every worker in this solution relies on.

## Context

- Every worker (`UdpService`, `CheckActiveDevice`, `GatewayService`, `BtsWatecService`, `GetDataByMonth`, `BtsS10SendTTTT`, `BtsSendTTTT`) wires logging identically: `using Core.Logging; services.AddLog4net();` in `Program.cs`, backed by a `log4net.config` alongside each project. `Core.Logging.Log4Configure.AddLog4net()` registers `ILoggingService -> Log4Service` (transient) and loads `log4net.config` via `XmlConfigurator.Configure`.
- No Serilog is bootstrapped anywhere in the solution today. `Core.csproj` references `Serilog.Sinks.File` but nothing consumes it (dead package reference), same for `BtsSendTTTT.csproj`.
- `src/Worker/UdpService/appsettings.json` already contains a `"Serilog"` section and an `"ElasticsearchLogging"` section, but both are dead configuration — nothing reads them, no `.UseSerilog(...)` call exists, and the values (`NodeUris: http://222.255.11.74:9200`, `IndexPrefix: Udpservices_logs`, `Application: QI_SIGNING_SERVICE`) look copy-pasted from an unrelated project.
- `Infrastructure/Udp/UdpService.cs` (the actual `IUdpService` hosted-service logic) already injects `Microsoft.Extensions.Logging.ILogger<UdpService>` via DI, currently backed by the generic host's default providers (Console/Debug/EventSource/EventLog), not by log4net.
- `Core/Setting/AppSetting.cs` holds one POCO class per config section (e.g. `AppSettingUDP`, `TelegramConfig`, `CacheSettings`), each a flat set of auto-properties matching its appsettings.json section 1:1. There is no existing class for `ElasticsearchLogging`.

## Decisions

| Decision | Choice | Why |
|---|---|---|
| Serilog sink package | `Elastic.Serilog.Sinks` | The older community package `Serilog.Sinks.Elasticsearch` (referenced only as a string in the dead config) was archived in 2024; Elastic's official replacement is the maintained option for Elasticsearch 8.x. |
| Relationship to log4net | Run side-by-side, unchanged | log4net keeps writing local rolling files exactly as today (zero behavior change for existing log consumers). Serilog is added purely to additionally ship logs to Elasticsearch. |
| Where the wiring code lives | `Core/Logging/SerilogConfigure.cs`, a new `IHostBuilder` extension | Mirrors the existing `AddLog4net()` convention in `Core.Logging`, so other workers can adopt Elasticsearch logging later with one line, consistent with how log4net is currently shared. |
| Credential storage | Plain values in `appsettings.json` | Matches the repo's existing (if insecure) convention — SQL/Mongo/RabbitMQ/Redis credentials are already checked into `appsettings.json` across every worker. Not introducing a new secrets-handling pattern is an explicit, informed choice here, consistent with project convention. |

## Components

### 1. `ElasticsearchLoggingSetting` (new POCO in `Core/Setting/AppSetting.cs`)

```csharp
public class ElasticsearchLoggingSetting
{
    public string NodeUris { get; set; }
    public string IndexPrefix { get; set; }
    public int BatchPostingLimit { get; set; }
    public int PeriodSeconds { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
}
```

### 2. `appsettings.json` (`src/Worker/UdpService`)

Replace the existing dead `"Serilog"` and `"ElasticsearchLogging"` sections with a single real section:

```json
"ElasticsearchLogging": {
  "NodeUris": "https://elasticsearch.qlgd.edu.vn",
  "IndexPrefix": "signing-service-dev-logs",
  "BatchPostingLimit": 1000,
  "PeriodSeconds": 30,
  "Username": "kibana_system",
  "Password": "Abcd@1234"
}
```

No separate `"Serilog"` config-driven section is kept — sink configuration is done entirely in code (`SerilogConfigure`), avoiding an extra dependency on `Serilog.Settings.Configuration`.

**Known risk:** `kibana_system` is an Elasticsearch built-in service account scoped for Kibana's own internal indices. It may lack `write`/`create_index` privileges on an arbitrary data stream like `logs-signing-service-dev-logs-default`. If Elasticsearch responds with 403 during bootstrap, this must be swapped for a user/role with index privileges on that pattern. This is flagged, not solved, by this design — verifying/fixing the ES-side role is out of scope for the code change and depends on the Elasticsearch admin.

### 3. `Core/Logging/SerilogConfigure.cs` (new)

```csharp
public static class SerilogConfigure
{
    public static IHostBuilder AddSerilogElasticsearch(this IHostBuilder hostBuilder, string applicationName)
    {
        return hostBuilder.UseSerilog((context, loggerConfig) =>
        {
            var es = context.Configuration.GetSection("ElasticsearchLogging").Get<ElasticsearchLoggingSetting>();
            loggerConfig
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithProperty("Application", applicationName)
                .WriteTo.Console();

            if (es != null && !string.IsNullOrWhiteSpace(es.NodeUris))
            {
                try
                {
                    loggerConfig.WriteTo.Elasticsearch(new[] { new Uri(es.NodeUris) }, opts =>
                    {
                        opts.DataStream = new DataStreamName("logs", es.IndexPrefix, "default");
                        opts.BootstrapMethod = BootstrapMethod.Failure;
                        opts.ConfigureChannel = channelOpts =>
                        {
                            channelOpts.BufferOptions = new BufferOptions
                            {
                                OutboundBufferMaxSize = es.BatchPostingLimit,
                                OutboundBufferMaxLifetime = TimeSpan.FromSeconds(es.PeriodSeconds)
                            };
                        };
                    }, transport => transport.Authentication(new BasicAuthentication(es.Username, es.Password)));
                }
                catch
                {
                    // Elasticsearch shipping is best-effort observability, not core functionality.
                    // A bad URI/unreachable cluster at startup must not take down UDP ingestion — fall back to Console-only.
                }
            }
        });
    }
}
```

Resulting data stream name: `logs-signing-service-dev-logs-default` (Elastic 8.x data streams use `type-dataset-namespace` naming and handle rollover/ILM themselves — there is no date-suffixed index name to configure, unlike the classic index-per-day pattern the old dead config implied).

### 4. `Program.cs` (`src/Worker/UdpService`)

Add one call to the host builder chain; `AddLog4net()` is untouched:

```csharp
Host.CreateDefaultBuilder(args)
    .UseWindowsService()
    .AddSerilogElasticsearch("UdpService")
    .ConfigureServices((hostContext, services) =>
    {
        services.AddHostedService<UdpService>();
        services.AddLog4net();
        // ...existing registrations unchanged
    });
```

Because `.UseSerilog()` replaces the default `Microsoft.Extensions.Logging` providers, every `ILogger<T>` already injected in the codebase (e.g. `Infrastructure/Udp/UdpService.cs`'s `ILogger<UdpService>`) automatically starts shipping to Console + Elasticsearch with no call-site changes. log4net continues operating independently through `ILoggingService`.

### 5. Package additions (`Core.csproj`)

- `Serilog`
- `Serilog.Extensions.Hosting`
- `Serilog.Sinks.Console`
- `Elastic.Serilog.Sinks`

`UdpService.csproj` needs no direct changes — it already references `Infrastructure.csproj`, which transitively references `Core.csproj`, matching how `Core.Logging.Log4Configure` is already consumed today.

## Error Handling

- ES sink registration is wrapped in try/catch: a bad URI, unreachable cluster, or bootstrap failure at startup falls back silently to Console-only logging rather than crashing UdpService. UDP telemetry ingestion (the primary function of this service) must never be blocked by an observability sink being unavailable.
- Runtime write failures after successful bootstrap are handled internally by the Elastic sink's own retry/buffering (`BufferOptions`); no additional handling needed.

## Testing

No automated test is feasible for host-builder logging bootstrap (no existing test infra covers `Program.cs`). Manual verification:
1. `dotnet build src\Worker\UdpService\UdpService.csproj` — confirms compilation.
2. `dotnet run --project src\Worker\UdpService\UdpService.csproj` — confirms Console sink still prints logs and the app doesn't crash if Elasticsearch is briefly unreachable.
3. Check Kibana / `GET logs-signing-service-dev-logs-default/_search` on the ES cluster for documents appearing within ~30s (`PeriodSeconds`) of app activity.
4. If step 3 returns a 403, the `kibana_system` account needs a role change (out of scope for this code change — see "Known risk" above).

## Out of Scope

- Migrating other workers (`CheckActiveDevice`, `BtsSendTTTT`, etc.) to also use `AddSerilogElasticsearch` — the extension is written to make that a one-line follow-up, but no other `Program.cs` is touched in this change.
- Replacing log4net entirely.
- Fixing Elasticsearch-side role/privilege configuration for `kibana_system`.
