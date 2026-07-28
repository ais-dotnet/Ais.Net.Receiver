# Ais.Net.Receiver release notes

> These notes were not maintained through the 0.3.x line; the entries below jump from v0.2.0 to the
> current unreleased work. Released 0.3.x versions are recorded in the repository's git tags.

## Unreleased

A large modernisation and reliability release. The headline changes are .NET 10, a Worker Service host
with .NET Aspire integration, a storage path that no longer silently loses batches, and end-to-end
OpenTelemetry instrumentation.

**Read the upgrade notes below before deploying** — the configuration schema has changed shape, and
several defaults behave differently.

### Platform

* Upgraded to .NET 10 (`net10.0`), C# 14, and modernised project structure.
* Adopted Central Package Management — package versions now live in `Solutions/Directory.Packages.props`.
* Added a .NET Aspire AppHost (`Ais.Net.Receiver.AppHost`) and a shared
  `Ais.Net.Receiver.ServiceDefaults` project carrying telemetry, health checks and service discovery.
  The AppHost also runs an Azurite container and points the worker at it, so `dotnet run` on the
  AppHost captures NMEA locally with no Azure account and no secrets (Docker required).
* Added an **AIS Visualizer demo** under `Solutions/Demos/`: a deck.gl and MapLibre map served by
  ASP.NET Core, orchestrated by the AppHost alongside a NATS broker. It shows live vessels by default:
  the worker publishes decoded messages to NATS as JSON and the page subscribes to that subject
  directly from the browser with `nats.ws`, so the web host relays no vessel data. It can instead
  replay a recorded day from a local `.nm4` file or straight from an hour the receiver captured to
  blob storage. Neither demo project ships in the container images or as a package.
* The worker can publish decoded AIS messages to a NATS subject (`ais.messages`) as polymorphic JSON,
  using `Ais.Net.Models.Json.Nats`. This is opt-in: it activates only when a `nats` connection string
  is configured, so a standalone worker is unaffected. Publishing runs off a bounded queue that sheds
  the oldest messages rather than stalling the receive path.
* `Storage:EnableCapture` now defaults to `false` in the tracked `appsettings.json` files. Previously
  they shipped `true` with an empty connection string, which made `ValidateOnStart` throw before the
  host started. Supply a connection string via user secrets, the environment, or the AppHost's
  Azurite container to turn capture back on.
* Added a Worker Service host (`Ais.Net.Receiver.Host.Worker`) alongside the existing console host, for
  running the receiver as a long-lived service. The published container image is still built from the
  console host; `Solutions/docker-compose.yml` additionally defines worker services, including a
  dead-letter-enabled variant.
* `ReceiverHost` gained a `Metadata` stream pairing each decoded message with the station id and Unix
  timestamp parsed from its NMEA tag block (requested in #149), and `FileStreamNmeaReceiver` gained a
  stream-agnostic base class, `StreamNmeaReceiver`, so recordings can be replayed from any `Stream` —
  the visualizer demo's blob replay reads captures through it.
* Replaced `Corvus.Retry` with [Polly](https://github.com/App-vNext/Polly) for retry policies.
* Upgraded to `Ais.Net.Models` 1.0.1.
* Added a BenchmarkDotNet project for NMEA parsing operations.

### Reliability

* **Storage writes are retried, and a batch that exhausts its retries is no longer lost.** With
  `Storage:DeadLetterPath` configured, exhausted batches are written to local disk and returned to
  storage by a background replayer once the backend recovers. Without it, the failure is surfaced rather
  than swallowed.
* **The storage queue is bounded.** Sentences are shed once `Storage:BoundedCapacity` is reached instead
  of growing memory without limit, and the drops are counted rather than hidden.
* Append-blob writes are serialized, closing an hour-boundary race that could misroute writes.
* The network receiver is now a native `IAsyncEnumerable` sequence, with the observable API retained as
  a copying projection for backwards compatibility.
* Idle-read detection now has its own 60-second default, independent of the reconnect settings — derived
  from them it worked out to roughly five seconds, which was too short to distinguish a quiet feed from
  a dropped one.
* TCP keepalive (60s idle, 10s probe interval, 3 retries) detects half-open connections without waiting
  for the idle timeout, and socket errors are classified into distinct log events so a refused
  connection, a DNS failure and an unreachable network are told apart.
* Health checks for AIS connection state and storage accessibility. **Connection health is based on
  whether the feed is delivering, not on whether connect succeeded** — a server that accepts a
  connection and then goes silent reconnects cleanly forever, so connect success alone is not treated as
  healthy.

### Observability

* OpenTelemetry traces, metrics and logs across the receive, decode and storage paths.
* Log records carry trace context, so a log line can be pivoted to the trace it belongs to without call
  sites threading ids by hand.
* Trace sampling is configurable: `OTEL_TRACES_SAMPLER`/`OTEL_TRACES_SAMPLER_ARG` take precedence, with
  `OpenTelemetry:TraceSamplingRatio` as the in-configuration fallback, applied `ParentBased` so a
  sampled trace stays intact end to end. Development always samples everything for the Aspire dashboard.
* **OTLP exporters are only registered when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.** Previously they
  defaulted to `localhost:4317` and logged periodic connection failures in deployments with no collector.
* Metrics include throughput by AIS message type, errors by classification, connection attempts and
  failures, consecutive failures, retry attempts, storage operations, bytes, batch sizes, pending
  batches, dropped sentences, and failed/replayed batches. Histogram buckets are calibrated against a
  measured run rather than estimated.
* New operational documentation: `docs/telemetry/observability-playbook.md` (every metric with units and
  buckets, queries, alert definitions, troubleshooting runbooks, and measured performance baselines) and
  `docs/telemetry/testing-guide.md`.

### Testing

* Switched to `MSTest.Sdk` and the Microsoft.Testing.Platform runner.
* Added Azurite-backed integration tests via Testcontainers, covering the storage client and — end to
  end — NMEA sentences through decode and the production storage wiring into real blobs, including
  dead-letter-then-replay after a storage failure, load shedding under a stalled backend, hour rollover,
  and the storage health check. These require Docker; see the Integration tests section of `README.md`.
* Extensive unit test coverage added across the receiver, parser, configuration, telemetry and storage.

### Upgrade notes

1. **The configuration file has been renamed and restructured.** Settings move from `settings.json` to
   `appsettings.json`, now read through the standard .NET host pipeline — so
   `appsettings.{Environment}.json`, environment variables (`Ais__Connection__Host`) and command-line
   arguments all override it. The `Ais` section is now nested:

   | Before | After |
   | --- | --- |
   | `Ais:host`, `Ais:port` | `Ais:Connection:Host`, `Ais:Connection:Port` |
   | `Ais:retryAttempts`, `Ais:retryPeriodicity` | `Ais:Connection:Retry:Attempts`, `Ais:Connection:Retry:Periodicity` (and `Ais:Receiver:Retry:*`) |
   | `Ais:loggerVerbosity` | `Ais:Telemetry:Verbosity` |
   | `Ais:statisticsPeriodicity` | `Ais:Telemetry:StatisticsPeriodicity` |

2. **`Verbosity` uses `LogLevel` names and is cumulative.** The old values map as follows:

   | Before | After |
   | --- | --- |
   | `Quiet` | `Error` (or `None` for silence) |
   | `Minimal` | `Warning` |
   | `Normal` | `Information` |
   | `Detailed` | `Debug` |
   | `Diagnostic` | `Trace` |

   Each level now includes everything the less verbose levels emit, where previously only an exact match
   enabled a given output — so `Detailed`/`Debug` used to suppress the statistics line and no longer does.

3. **`Connection:Retry:Attempts` caps how far the reconnect backoff grows; it does not limit the number
   of attempts.** The receiver reconnects indefinitely. If you raised this value to stop the receiver
   giving up, you can return it to a small number — with `Attempts: 5` and `Periodicity: 1s` the delay
   climbs to five seconds and stays there.

4. **No telemetry is exported unless `OTEL_EXPORTER_OTLP_ENDPOINT` is set.**

5. **Integration tests require Docker.** They report as inconclusive when no Docker endpoint is
   available, but any other container failure fails the build. Set `AIS_TEST_REQUIRE_INTEGRATION=true`
   to make a missing daemon a failure too, as CI does.

6. **`dotnet test` now runs on Microsoft.Testing.Platform**, which does not accept VSTest-era switches.
   Passing `--nologo`, `--logger` or `--test-adapter-path` makes the run report `Zero tests ran` rather
   than an unrecognised-argument error.

## v0.2.0

* Add Errors observable to `Ais.Net.Receiver.Receiver.ReceiverHost` https://github.com/ais-dotnet/Ais.Net.Receiver/pull/142
