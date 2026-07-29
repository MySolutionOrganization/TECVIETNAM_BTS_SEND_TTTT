# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this repo is

A .NET solution (`BtsGetwayService.sln`) that collects telemetry from BTS/trạm quan trắc devices (weather, hydrology, rainfall, wind stations) over UDP, stores it to SQL Server (Dapper) and MongoDB, alerts operators via Telegram, and re-exports/forwards the data to third-party systems ("TTTT", "Watec"). It is **not** a single web API — it is a set of independent Windows Services/Workers plus two small ASP.NET Core MVC APIs, sharing a `Core` class library.

⚠️ **The `.claude/rules/*.md` and `.claude/skills/mediatr-cqrs` files in this repo describe a MediatR/CQRS/Carter/`Response<T>` pattern that does NOT exist anywhere in this codebase** (verified: zero references to MediatR/`IRequest`/`IRequestHandler`). Those rules appear to be generic boilerplate from an unrelated project template. Do not apply them here — follow the actual conventions described below instead.

## Solution layout

```
src/Core            Shared class library: settings, MSSQL/Mongo/Redis/RabbitMQ access, Telegram, logging, helpers
src/Infrastructure   Udp/ — BTS device wire-protocol parsing & business logic (IUdpService and friends)
src/Worker/
  UdpService          The actual UDP listener — entry point of the pipeline (port from AppSettingUDP.UdpPort)
  GatewayService       RabbitMQ consumer alternative to UdpService's in-process path
  CheckActiveDevice    Periodic device-liveness sweep + Telegram online/offline alerts
  BtsSendTTTT          Exports periodic reports to JSON + FTP ("TTTT" format)
  BtsS10SendTTTT       Same as above but for the "S10" report format
  BtsWatecService      Pushes report data to the third-party Watec HTTP API
  GetDataByMonth       Interactive console-style one-off backfill/export utility (not a real long-running service)
src/Api/
  GatewayApi           Thin HTTP ingress: POST a device message -> pushes onto RabbitMQ for GatewayService
  Api_Watec            JWT-protected read API for the "QuanTrac" web portal (stations, sensors, reports)
src/Test/UnitTest      xUnit project — actually integration tests against real SQL Server/MongoDB, not mocks
```

All workers use `Host.CreateDefaultBuilder(args).UseWindowsService()` + `BackgroundService`. The `Topshelf` package reference in `Core.csproj`/`BtsSendTTTT.csproj` is unused dead weight. Both APIs use the old-style `Startup.cs` (`ConfigureServices`/`Configure`), not minimal hosting.

**Target frameworks are inconsistent** across projects — check the `.csproj` before assuming .NET 8:
- `net8.0`: Core, Infrastructure, BtsS10SendTTTT, BtsWatecService, CheckActiveDevice
- `net6.0`: Api_Watec, BtsSendTTTT, GatewayService, UdpService (worker), UnitTest
- `netcoreapp3.1`: GatewayApi, GetDataByMonth

## Commands

```powershell
# Build everything
dotnet build BtsGetwayService.sln

# Build/run a single project
dotnet build src\Worker\UdpService\UdpService.csproj
dotnet run --project src\Worker\UdpService\UdpService.csproj

# Run the API locally
dotnet run --project src\Api\Api_Watec\Api_Watec.csproj

# Run all tests (requires reachable SQL Server/MongoDB — see Testing below)
dotnet test src\Test\UnitTest\UnitTest.csproj

# Run a single test
dotnet test src\Test\UnitTest\UnitTest.csproj --filter "FullyQualifiedName~UnitTest1.Test1"
```

There is no lint/format tooling configured. There is no `dotnet build`/`dotnet test` step in CI (see CI/CD below) — always build the affected project(s) locally before considering a change done.

## Testing

`src/Test/UnitTest` is xUnit but **not** isolated — `BaseTest.cs` boots the real DI host (same repos/services as the workers) against the connection strings in `src/Test/UnitTest/appsettings.json`, so tests hit an actual SQL Server/MongoDB instance. There is no mocking layer. `UnitTest1.Test1` exercises `IUdpService.Insert` with a hand-crafted S10 message but asserts nothing — it's a smoke test (passes as long as parsing doesn't throw). Keep this in mind: a green test run only proves "didn't crash," not correctness.

## Architecture / data flow

Two parallel ingestion paths converge on the same processing logic:

1. **UDP path (primary):** `UdpService` worker opens a `UdpClient` on `AppSettingUDP.UdpPort`, blocking-receives datagrams, splits multi-message packets on the `END;` delimiter, and calls `Infrastructure.Udp.UdpService.Insert(msg)` synchronously in-process.
2. **HTTP/queue path (alternative front door):** `GatewayApi` accepts a message over HTTP (`POST /UdpServiceController/PostMessage`, guarded by a hardcoded API-key header check) and publishes it to a RabbitMQ queue named `"UdpGateway"`; the `GatewayService` worker subscribes and calls the same `IUdpService.Insert(msg)`.

`Infrastructure.Udp.UdpService.Insert` is the central router — it branches on substring markers in the raw device message:
- `ALM` → parse as alarm → `DataAlarm` (MongoDB) → threshold check against `RegisterSMS`/`SMSServer` → raw TCP SMS gateway call.
- `SEQ` → parse as normal telemetry → `Data` (MongoDB).
- `S10` → richer periodic report format → `ReportS10` (SQL Server).
- `RP` → daily report format → one of ten `ReportDaily*` tables (temperature/humidity/rainfall/pressure/flow/water-level/solar/wind), routed by `typeReport`.

Message parsing is hand-rolled string splitting on a proprietary field-code format (e.g. `EQID=...;SEQ;HH:mm:ss-dd/MM/yy;BTIxx.x;BHUxx.x;...END;`), not a structured protocol library — field codes live in `Core/MongoDb/Entity/Data.cs` and `DataAlarm.cs`.

Downstream of ingestion:
- `CheckActiveDevice` periodically (`CheckDeviceS10`, every ~20 min) checks each `Site` for a recent Mongo document (last 20 min); flips online/offline status in SQL and pushes 🟢/🔴 Telegram alerts, then busts the web portal's cache via an HTTP callback.
- `BtsSendTTTT` / `BtsS10SendTTTT` / `BtsWatecService` run on a 10-minute-aligned loop (`Helper.ThoiGianDelayDeBatDauChayService` snaps start time to the next 10-min boundary) exporting report data to JSON files + FTP, or to the Watec HTTP API.
- `Api_Watec` serves read queries (stations, sensors, aggregated reports) to the web portal, with JWT auth backed by a **single hardcoded admin account** (`JwtAccountConfig`) and in-memory (non-persistent, non-distributed) refresh tokens.

Shared infra in `Core`:
- `MSSQL/Responsitory` (sic) — one Dapper repo class per entity, `DapperBaseData<Entity>` base opens a new `SqlConnection` per call.
- `MongoDb` — `MogoRepository<Entity>` (sic) generic repo, always targets database `"DataObservation"` regardless of the connection string's own database segment.
- `Caching/RedisCacheService` — cache-aside via `IDistributedCache`; prefix-based invalidation opens a fresh `ConnectionMultiplexer` per call (only wired up in `Api_Watec`).
- `MessageQueue` — RabbitMQ fanout exchange (`TOPIC_BTS_GATEWAY_SERVICE`, cross-instance cache invalidation) and a durable work queue (`"UdpGateway"`, hardcoded name, ignores the configurable `WorkerQueueName` setting).
- `PushMessage/TelegramMessageService` — sends to one fixed `ChatId`; exceptions are swallowed silently (a Telegram outage never surfaces).

## Known gotchas (read before editing)

- **Typos are load-bearing** — they're baked into folder/type/namespace names, not just comments: `Responsitory` (Repository), `Reponse`/`ReponseModel` (Response), `MogoRepository` (Mongo). Match existing spelling in a given file; don't "fix" it in isolation.
- Namespaces are inconsistent across the codebase (`bts.udpgateway`, `bts.udpgateway.integration`, `BtsGetwayService`, `BtsGetwayService.Core`, `Core.*`, `ES_CapDien.AppCode` all coexist) — don't assume a clean `Core.*` tree; check the actual namespace of the file you're editing.
- Several `StartAsync` overrides run infinite blocking loops themselves (`UdpService`, `CheckActiveDevice`) instead of returning promptly and doing work in `ExecuteAsync`; `ExecuteAsync` in those workers is a decorative no-op. `GatewayService.ExecuteAsync` is an empty busy-loop (`while(!cancelled){}`, no delay) — real work happens via an event-driven RabbitMQ subscription registered in `StartAsync`.
- `MogoRepository.CountAsync`'s predicate branch looks inverted (counts everything when a predicate IS given) — verify behavior before relying on it.
- `Infrastructure/Udp/AlarmService.cs` and `DataUdpService.cs` are empty placeholder classes; `SiteService`'s cache-settings injection is unused (no caching actually happens there) despite `CacheSettings` being wired in.
- `BtsWatecService` can block on `Console.ReadLine()` at startup if `AppApiWatecSetting.IsChooseGroup` is set — unusual for something deployed as a Windows Service.
- No RabbitMQ/Redis connection resiliency (no retry/reconnect, no Polly) — a mid-run broker restart requires the app itself to restart.

## Security notes

- `appsettings*.json` across every worker/API contain **real, working credentials checked into git** (SQL Server, MongoDB, RabbitMQ, Redis, third-party API keys). Don't add new secrets the same way — and don't assume existing checked-in values are fake.
- `GatewayApi`'s `ApiKeyHeaderCheckAttribute.cs` has API keys **hardcoded directly in source**, not config.
- `Api_Watec` auth is a single shared admin account (no user store), with in-memory-only refresh tokens (`RefreshTokenStore.Tokens` static list — lost on restart, not shared across instances).

## CI/CD

`.github/workflows/dotnet.yml` triggers only on push/PR to `production`, runs on a **self-hosted runner that is the production box itself**, and **does not build or test** — it directly invokes `deploy-tec-backup-14.177.239.150.ps1` (which wraps `shell-command.ps1`) to stop the target Windows Service, `dotnet publish -c Release`, swap in `appsettings.ProductionTecBackup.json` as `appsettings.Production.json`, and restart the service. Currently only `UdpService` and `CheckActiveDevice` are deployed this way. Because there's no build/test gate, always verify a change compiles and (where feasible) passes the integration test before pushing to `production`.
