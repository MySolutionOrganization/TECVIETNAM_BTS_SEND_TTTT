# UdpService Elasticsearch Logging Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship UdpService's application logs to Elasticsearch (`https://elasticsearch.qlgd.edu.vn`) via a new `Elastic.Serilog.Sinks`-backed Serilog pipeline, running side-by-side with the existing log4net setup (which is untouched).

**Architecture:** A new `Core.Logging.SerilogConfigure.AddSerilogElasticsearch(this IHostBuilder, string applicationName)` extension (mirroring the existing `Core.Logging.Log4Configure.AddLog4net()` pattern) wires Serilog onto the generic host in `UdpService/Program.cs`. Serilog reads a new `ElasticsearchLoggingSetting` POCO off the `"ElasticsearchLogging"` config section and writes to Console + an Elasticsearch data stream. Because `.UseSerilog()` replaces the default `Microsoft.Extensions.Logging` providers, every already-injected `ILogger<T>` in the codebase starts shipping to Elasticsearch with no other call-site changes.

**Tech Stack:** .NET (Core.csproj: net8.0, UdpService.csproj: net6.0, cross-referenced today via `Infrastructure.csproj` — this plan does not change that arrangement), Serilog 4.4.0, Serilog.Extensions.Hosting 8.0.0 (not 10.0.0 — see Task 2 correction), Serilog.Sinks.Console 6.1.1, Elastic.Serilog.Sinks 9.0.0.

## Global Constraints

- Do not modify log4net wiring (`services.AddLog4net()` in `UdpService/Program.cs`, `Core/Logging/Log4Configure.cs`, `log4net.config`) — it must keep working exactly as today.
- Do not touch any other worker's `Program.cs` (`CheckActiveDevice`, `GatewayService`, `BtsWatecService`, `GetDataByMonth`, `BtsS10SendTTTT`, `BtsSendTTTT`) — this plan is UdpService-only; the new `Core.Logging` extension is written so those can adopt it later in one line, but that adoption is out of scope here.
- ES sink registration must be wrapped so that a bad URI / unreachable cluster / bootstrap failure at startup falls back to Console-only logging — it must never crash UdpService (UDP ingestion is the primary function of this service, ES logging is best-effort observability).
- `ElasticsearchLogging` config values (from the approved spec, `docs/superpowers/specs/2026-08-14-udpservice-elasticsearch-logging-design.md`):
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
- Known risk (documented, not to be silently "fixed" by improvising a different user): `kibana_system` may lack write/create_index privileges on an arbitrary data stream. If bootstrap gets a 403 during manual verification, stop and report it — do not swap in a different account without asking.

---

### Task 1: Add `ElasticsearchLoggingSetting` POCO to Core settings

**Files:**
- Modify: `src/Core/Setting/AppSetting.cs`

**Interfaces:**
- Produces: `Core.Setting.ElasticsearchLoggingSetting` with properties `NodeUris` (string), `IndexPrefix` (string), `BatchPostingLimit` (int), `PeriodSeconds` (int), `Username` (string), `Password` (string) — consumed by Task 3's `SerilogConfigure`.

- [ ] **Step 1: Add the new class**

Append this class to the end of the `Core.Setting` namespace block in `src/Core/Setting/AppSetting.cs` (after the closing brace of `TelegramConfig`, before the namespace's final closing brace):

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

The full file should read:

```csharp
using System;
using System.Collections.Generic;
using System.Text;

namespace Core.Setting
{
    public class AppSetting
    {
        public string UrlDomainWebQuanTrac { get; set; }
        public string FolderLuuTruFile { get; set; }
        public int IsSendTTTT { get; set; }
    }
    public class AppApiWatecSetting
    {
        public string UrlPost { get; set; }
        public string ApiKey { get; set; }
        public int IsChooseGroup { get; set; }
        public int IsSendWatec { get; set; }
        public string ApiWatecKey { get; set; }
        public string ApiWatecUrl { get; set; }
    }
    public class AppSettingUDP
    {
        public int UdpPort { get; set; }        
        public bool ToDatabase { get; set; }        
        public bool IsUseRabbitMQ { get; set; }        
        public string UrlDomainWebQuanTrac { get; set; }        
    }
    public class JWT
    {
        public string Key { get; set; }
        public string Issuer { get; set; }
        public string Audience { get; set; }
        public int ExpireMinutes { get; set; }
    }
    public class JwtAccountConfig
    {
        public string Username { get; set; }
        public string Password { get; set; }        
    }
    public class CacheSettings
    {       
        public int CacheTime { get; set; }
    }
    public class TelegramConfig
    {
        public string BotToken { get; set; }
        public string ChatId { get; set; }
        public string ServerName { get; set; }
    }
    public class ElasticsearchLoggingSetting
    {
        public string NodeUris { get; set; }
        public string IndexPrefix { get; set; }
        public int BatchPostingLimit { get; set; }
        public int PeriodSeconds { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
    }
}
```

- [ ] **Step 2: Build Core to verify it compiles**

Run: `dotnet build src\Core\Core.csproj`
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add src/Core/Setting/AppSetting.cs
git commit -m "Add ElasticsearchLoggingSetting config POCO"
```

---

### Task 2: Add Serilog + Elasticsearch package references to Core.csproj

**Files:**
- Modify: `src/Core/Core.csproj`

**Interfaces:**
- Produces: `Serilog.LoggerConfiguration`, `Serilog.Events.LogEventLevel`, `Serilog.Extensions.Hosting`'s `UseSerilog` extension on `IHostBuilder`, `Serilog.Sinks.Console`'s `WriteTo.Console()`, and `Elastic.Serilog.Sinks`' `WriteTo.Elasticsearch(...)` + `DataStreamName` + `BootstrapMethod` + `BufferOptions` types — all consumed by Task 3.

- [ ] **Step 1: Add the package references**

In `src/Core/Core.csproj`, inside the existing `<ItemGroup>` that holds `PackageReference` entries, add these four lines (alphabetical position doesn't matter functionally, but keep it alphabetized like the rest of the file — insert after `Dapper.Contrib` and before `log4net` for `Elastic.Serilog.Sinks`, and after `Microsoft.Extensions.Logging.Console` and before `MongoDB.Driver` for the `Serilog.*` ones):

```xml
		<PackageReference Include="Elastic.Serilog.Sinks" Version="9.0.0" />
```
```xml
		<PackageReference Include="Serilog" Version="4.4.0" />
		<PackageReference Include="Serilog.Extensions.Hosting" Version="8.0.0" />
		<PackageReference Include="Serilog.Sinks.Console" Version="6.1.1" />
```

**Correction:** use `Serilog.Extensions.Hosting` version `8.0.0`, not `10.0.0` — `10.0.0` requires `Microsoft.Extensions.Logging >= 10.0.0`, which conflicts (NU1605) with the `Microsoft.Extensions.Logging` `8.0.0` already pinned in this file below. `8.0.0`'s dependency chain only needs `Microsoft.Extensions.Logging.Abstractions >= 8.0.0`, which the existing pins satisfy.

Resulting `<ItemGroup>` (full block, for reference — match this exactly):

```xml
	<ItemGroup>
		<PackageReference Include="Dapper" Version="2.0.35" />
		<PackageReference Include="Dapper.Contrib" Version="2.0.35" />
		<PackageReference Include="Elastic.Serilog.Sinks" Version="9.0.0" />
		<PackageReference Include="log4net" Version="3.3.1" />
		<PackageReference Include="Microsoft.Extensions.Caching.StackExchangeRedis" Version="6.0.36" />
		<PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="8.0.0" />
		<PackageReference Include="Microsoft.Extensions.Logging" Version="8.0.0" />
		<PackageReference Include="Microsoft.Extensions.Logging.Console" Version="8.0.0" />
		<PackageReference Include="Serilog" Version="4.4.0" />
		<PackageReference Include="Serilog.Extensions.Hosting" Version="8.0.0" />
		<PackageReference Include="Serilog.Sinks.Console" Version="6.1.1" />
		<PackageReference Include="MongoDB.Driver" Version="3.5.0" />
		<PackageReference Include="Newtonsoft.Json" Version="13.0.4" />
		<PackageReference Include="Serilog.Sinks.File" Version="5.0.0" />
		<PackageReference Include="StackExchange.Redis" Version="2.8.24" />
		<PackageReference Include="System.Configuration.ConfigurationManager" Version="8.0.0" />
		<PackageReference Include="System.Data.SqlClient" Version="4.9.0" />
		<PackageReference Include="Telegram.Bot" Version="22.0.0" />
		<PackageReference Include="Topshelf" Version="4.2.1" />
		<PackageReference Include="RabbitMQ.Client" Version="6.2.2" />
	</ItemGroup>
```

- [ ] **Step 2: Restore and build to verify the packages resolve**

Run: `dotnet build src\Core\Core.csproj`
Expected: `Build succeeded.` (NuGet restores the 4 new packages automatically on build)

- [ ] **Step 3: Commit**

```bash
git add src/Core/Core.csproj
git commit -m "Add Serilog and Elastic.Serilog.Sinks package references to Core"
```

---

### Task 3: Create `SerilogConfigure.AddSerilogElasticsearch` extension

**Files:**
- Create: `src/Core/Logging/SerilogConfigure.cs`

**Interfaces:**
- Consumes: `Core.Setting.ElasticsearchLoggingSetting` (Task 1); `Serilog`/`Serilog.Extensions.Hosting`/`Elastic.Serilog.Sinks` types (Task 2).
- Produces: `public static IHostBuilder AddSerilogElasticsearch(this IHostBuilder hostBuilder, string applicationName)` — consumed by Task 5's `UdpService/Program.cs`.

- [ ] **Step 1: Write the file**

```csharp
using Core.Setting;
using Elastic.Ingest.Elasticsearch;
using Elastic.Serilog.Sinks;
using Elastic.Transport;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using System;

namespace Core.Logging
{
    /// <summary>
    /// Đăng ký log cho Serilog, ghi ra Console và đẩy lên Elasticsearch.
    /// Chạy song song với AddLog4net(), không thay thế log4net.
    /// </summary>
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

                if (es == null || string.IsNullOrWhiteSpace(es.NodeUris))
                {
                    return;
                }

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
                    // Elasticsearch shipping là best-effort observability, không phải chức năng cốt lõi.
                    // Nếu URI sai hoặc cluster không reachable lúc khởi động, vẫn tiếp tục chạy với Console sink,
                    // không được làm sập UdpService (luồng UDP mới là chính).
                }
            });
        }
    }
}
```

- [ ] **Step 2: Build Core to verify it compiles**

Run: `dotnet build src\Core\Core.csproj`
Expected: `Build succeeded.` The using directives above (`Elastic.Ingest.Elasticsearch` for `DataStreamName`/`BootstrapMethod`/`BufferOptions`, `Elastic.Transport` for `BasicAuthentication`) match Elastic's own official sample for `Elastic.Serilog.Sinks`. If a future package version moves a type and the build reports "type or namespace not found", use your IDE's "Go to definition"/NuGet package explorer on the installed `Elastic.Serilog.Sinks`/`Elastic.Transport` packages to find the type's new namespace and fix the `using` here.

- [ ] **Step 3: Commit**

```bash
git add src/Core/Logging/SerilogConfigure.cs
git commit -m "Add SerilogConfigure.AddSerilogElasticsearch extension"
```

---

### Task 4: Update UdpService `appsettings.json`

**Files:**
- Modify: `src/Worker/UdpService/appsettings.json`

**Interfaces:**
- Produces: the `"ElasticsearchLogging"` config section read by Task 3's `SerilogConfigure` via `context.Configuration.GetSection("ElasticsearchLogging").Get<ElasticsearchLoggingSetting>()`.

- [ ] **Step 1: Replace the dead `Serilog` and `ElasticsearchLogging` sections**

Find this block in `src/Worker/UdpService/appsettings.json`:

```json
  "Serilog": {
    "Using": [ "Serilog.Sinks.Console", "Serilog.Sinks.Debug", "Serilog.Sinks.Elasticsearch", "Serilog.Exceptions" ],
    "MinimumLevel": {
      "Default": "Information",
      "Override": { "Microsoft": "Warning", "System": "Warning", "Microsoft.AspNetCore": "Warning" }
    },
    "WriteTo": [ { "Name": "Console" } ],
    "Enrich": [ "FromLogContext", "WithMachineName", "WithThreadId", "WithExceptionDetails" ],
    "Properties": { "Application": "QI_SIGNING_SERVICE" }
  },
  "ElasticsearchLogging": {
    "NodeUris": "http://222.255.11.74:9200",
    "IndexPrefix": "Udpservices_logs",
    "BatchPostingLimit": 1000,
    "PeriodSeconds": 30
  }
```

Replace it with:

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

(The `"Serilog"` config-driven section is removed entirely — `SerilogConfigure` builds the pipeline in code, it does not use `Serilog.Settings.Configuration`/`ReadFrom.Configuration`.)

- [ ] **Step 2: Validate the JSON is well-formed**

Run: `pwsh -Command "Get-Content src\Worker\UdpService\appsettings.json -Raw | ConvertFrom-Json | Out-Null; Write-Output OK"`
Expected: `OK` (no JSON parse error)

- [ ] **Step 3: Commit**

```bash
git add src/Worker/UdpService/appsettings.json
git commit -m "Point UdpService ElasticsearchLogging config at the real cluster"
```

---

### Task 5: Wire `AddSerilogElasticsearch` into UdpService's host builder

**Files:**
- Modify: `src/Worker/UdpService/Program.cs:30-87`

**Interfaces:**
- Consumes: `Core.Logging.SerilogConfigure.AddSerilogElasticsearch(this IHostBuilder, string applicationName)` (Task 3).

- [ ] **Step 1: Add the `.AddSerilogElasticsearch("UdpService")` call to the host builder chain**

In `src/Worker/UdpService/Program.cs`, change:

```csharp
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
```

to:

```csharp
        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .AddSerilogElasticsearch("UdpService")
                .ConfigureServices((hostContext, services) =>
```

`using Core.Logging;` is already present at the top of this file (line 7), so no new `using` is needed.

- [ ] **Step 2: Build UdpService to verify it compiles**

Run: `dotnet build src\Worker\UdpService\UdpService.csproj`
Expected: `Build succeeded.`

- [ ] **Step 3: Run UdpService locally and confirm it starts without crashing**

Run: `dotnet run --project src\Worker\UdpService\UdpService.csproj`
Expected: process starts, Console shows Serilog-formatted log lines (timestamped, e.g. `[09:15:23 INF] ...`) in addition to whatever log4net/host output already appeared before this change. It must **not** crash on startup even if Elasticsearch is briefly unreachable — if it does crash, the try/catch in `SerilogConfigure` (Task 3) isn't catching the actual failure mode; inspect the exception and widen the catch or move more of the setup inside it. Stop the process with Ctrl+C once confirmed.

- [ ] **Step 4: Verify Elasticsearch actually received documents**

Run (replace placeholders only if you have `curl` and network access to the cluster; otherwise check via Kibana Discover instead):
```bash
curl -u kibana_system:Abcd@1234 "https://elasticsearch.qlgd.edu.vn/logs-signing-service-dev-logs-default/_search?size=1&sort=@timestamp:desc"
```
Expected: a JSON response with at least one hit, `_source.Application` equal to `"UdpService"`, within ~30 seconds of the run in Step 3 (matches `PeriodSeconds: 30`).

If this returns a `403`, this confirms the "Known risk" from the spec — the `kibana_system` account lacks write privileges on this data stream. **Stop here and report this to the user** rather than substituting a different account; the fix is an Elasticsearch-side role/privilege change outside this codebase.

- [ ] **Step 5: Commit**

```bash
git add src/Worker/UdpService/Program.cs
git commit -m "Wire Serilog/Elasticsearch logging into UdpService host builder"
```

---

## Post-Plan Follow-Up (not part of this plan's scope)

- Other workers (`CheckActiveDevice`, `BtsSendTTTT`, `BtsS10SendTTTT`, `BtsWatecService`, `GatewayService`, `GetDataByMonth`) can adopt Elasticsearch logging later by adding their own `"ElasticsearchLogging"` section to their `appsettings.json` and one `.AddSerilogElasticsearch("<WorkerName>")` call — no further Core changes needed.
- If Task 5 Step 4 reveals a 403 from Elasticsearch, that's an infra/access-control fix (new ES role or different credentials), not a code change.
