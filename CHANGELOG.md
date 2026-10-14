# Changelog

All notable changes to Ais.Net.Receiver are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/), and this project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

Entries were not kept between 0.2.0 and 0.3.8; those versions are listed with their dates and compare
links only.

## [Unreleased]

A modernisation and reliability release: .NET 10, a Worker Service host with .NET Aspire integration,
a storage path that no longer silently loses batches, end-to-end OpenTelemetry instrumentation, a
WebSocket transport, and a broadcaster for testing without a live feed. The configuration schema has
changed shape and several defaults behave differently; the migration details are in the **Changed**
entries below.

### Added

- A .NET Aspire AppHost (`Ais.Net.Receiver.AppHost`) and a shared `Ais.Net.Receiver.ServiceDefaults`
  project carrying telemetry, health checks and service discovery. The AppHost runs an Azurite
  container and points the worker at it, so `dotnet run` on the AppHost captures NMEA locally with no
  Azure account and no secrets (Docker required). An `Ais.Net.Receiver.AppHost (broadcaster)` launch
  profile, or `AisSource=broadcaster`, feeds the worker from the bundled broadcaster instead of the
  live feed so the whole system runs offline.
- A Worker Service host (`Ais.Net.Receiver.Host.Worker`) alongside the console host, for running the
  receiver as a long-lived service. The published container image is still built from the console
  host; `Solutions/docker-compose.yml` additionally defines worker services, including a
  dead-letter-enabled variant.
- **Ais.Net.Broadcaster**, the sink-side counterpart of the receiver, and its `ais-broadcaster` dotnet
  tool (`Ais.Net.Broadcaster.Cli`). It replays an NMEA recording (paced by its tag-block timestamps,
  at a fixed rate, or flat out, and looped by default) or relays a live TCP or WebSocket feed, and
  serves it to TCP and WebSocket clients, with optional fault injection: periodic disconnects,
  silences, malformed and over-long lines. Two sample recordings ship with it, including about a
  minute of the real Norwegian feed. Design record: `docs/broadcaster-design.md`.
- A WebSocket source: `WebSocketNmeaReceiver` and `WebSocketNmeaConnection` implement `INmeaReceiver`
  over `ws://` and `wss://`, accepting either one sentence per message or several newline-delimited
  sentences in one message. Selected with the new `Ais:Connection:Transport` (`Tcp`, the default, or
  `WebSocket`) and `Ais:Connection:Url` settings; existing TCP configuration keeps working unchanged,
  and `Host` and `Port` are not required for WebSocket.
- Shared receiver primitives (`docs/receiver-design.md`): `NmeaLineReader`, the single NMEA line
  framer; `INmeaConnection` with a `Kind` of `Live` or `Finite`, the single transport contract;
  `StreamNmeaConnection`, the "open a `Stream`" base, with `FileNmeaConnection` and
  `TcpNmeaConnection` deriving from it; and the public `NmeaReceiver` with `NmeaReceiverOptions`,
  which supplies reconnect, idle timeout, metrics, tracing and the health callback for live sources
  and a single pass for finite ones.
- `ReceiverHost.Metadata`, a stream pairing each decoded message with the station id and Unix
  timestamp parsed from its NMEA tag block ([#149]).
- `StreamNmeaReceiver`, a stream-agnostic base for `FileStreamNmeaReceiver`, so recordings can be
  replayed from any `Stream`; the visualizer demo's blob replay reads captures through it.
- Opt-in publishing of decoded AIS messages to a NATS subject (`ais.messages`) as polymorphic JSON
  via `Ais.Net.Models.Json.Nats`. It activates only when a `nats` connection string is configured, and
  runs off a bounded queue that sheds the oldest messages rather than stalling the receive path.
- An AIS Visualizer demo (`Ais.Net.Receiver.Demo.Visualizer` and its `Ais.Net.Receiver.Demo.Tracks`
  pipeline): a deck.gl and MapLibre map served by ASP.NET Core and orchestrated by the AppHost
  alongside a NATS broker. The page subscribes to the worker's NATS subject directly from the browser
  with `@nats-io/nats-core`, so the web host relays no vessel data; it can instead replay a recorded day from a
  local `.nm4` file or from an hour the receiver captured to blob storage. Neither demo project ships
  in the container images or as a package.
- Dead-lettering: with `Storage:DeadLetterPath` configured, a batch that exhausts its storage retries
  is written to local disk and returned to storage by a background replayer once the backend
  recovers.
- Health checks for AIS connection state and storage accessibility. Connection health is based on
  whether the feed is delivering, not on whether connect succeeded, because a server that accepts a
  connection and then goes silent reconnects cleanly forever.
- TCP keepalive (60 s idle, 10 s probe interval, 3 retries) to detect half-open connections without
  waiting for the idle timeout, and socket errors classified into distinct log events so a refused
  connection, a DNS failure and an unreachable network are told apart.
- OpenTelemetry traces, metrics and logs across the receive, decode and storage paths, with trace
  context on log records. Metrics cover throughput by AIS message type, errors by classification,
  connection attempts and failures, consecutive failures, retry attempts, storage operations, bytes,
  batch sizes, pending batches, dropped sentences, and failed and replayed batches; histogram buckets
  are calibrated against a measured run.
- Configurable trace sampling: `OTEL_TRACES_SAMPLER` and `OTEL_TRACES_SAMPLER_ARG` take precedence,
  with `OpenTelemetry:TraceSamplingRatio` as the in-configuration fallback, applied `ParentBased` so a
  sampled trace stays intact end to end. Development always samples everything for the Aspire
  dashboard.
- Operational documentation: `docs/telemetry/observability-playbook.md` (every metric with units and
  buckets, queries, alert definitions, troubleshooting runbooks and measured performance baselines)
  and `docs/telemetry/testing-guide.md`.
- A BenchmarkDotNet project for NMEA parsing operations.
- Azurite-backed integration tests via Testcontainers, covering the storage client and, end to end,
  NMEA sentences through decode and the production storage wiring into real blobs, including
  dead-letter-then-replay after a storage failure, load shedding under a stalled backend, hour
  rollover and the storage health check. They require Docker and report as inconclusive without a
  Docker endpoint; set `AIS_TEST_REQUIRE_INTEGRATION=true` to make a missing daemon a failure, as CI
  does.
- Extensive unit test coverage across the receiver, parser, configuration, telemetry and storage.
- `AisConfigValidator`, an `IValidateOptions<AisConfig>` registered by both hosts, validates the nested
  `Connection`, `Retry` and `Telemetry` sections at start-up with their configuration paths in the
  message, so a bad transport setting fails the host immediately rather than when the receiver is
  created; `ValidateDataAnnotations` alone only inspects the top-level object.

### Changed

- Upgraded to .NET 10 (`net10.0`) and C# 14, with a modernised project structure, and to
  `Ais.Net.Models` 1.0.2.
- Adopted Central Package Management: package versions live in `Solutions/Directory.Packages.props`.
- **The configuration file is renamed and restructured.** Settings move from `settings.json` to
  `appsettings.json`, read through the standard .NET host pipeline, so `appsettings.{Environment}.json`,
  environment variables (`Ais__Connection__Host`) and command-line arguments all override it. The
  `Ais` section is now nested:

  | Before | After |
  | --- | --- |
  | `Ais:host`, `Ais:port` | `Ais:Connection:Host`, `Ais:Connection:Port` |
  | `Ais:retryAttempts`, `Ais:retryPeriodicity` | `Ais:Connection:Retry:Attempts`, `Ais:Connection:Retry:Periodicity` (and `Ais:Receiver:Retry:*`) |
  | `Ais:loggerVerbosity` | `Ais:Telemetry:Verbosity` |
  | `Ais:statisticsPeriodicity` | `Ais:Telemetry:StatisticsPeriodicity` |

  The `Storage` section keeps its key names but is documented PascalCase from this release
  (`Storage:EnableCapture` rather than `Storage:enableCapture`). Binding is case-insensitive, so
  existing lower-cased keys and environment variables (`Storage__enableCapture`) continue to bind.
- **`Ais:Telemetry:Verbosity` uses `LogLevel` names and is cumulative.** The old values map as
  `Quiet` to `Error` (or `None` for silence), `Minimal` to `Warning`, `Normal` to `Information`,
  `Detailed` to `Debug` and `Diagnostic` to `Trace`. Each level now includes everything the less
  verbose levels emit, where previously only an exact match enabled a given output.
- **`Ais:Connection:Retry:Attempts` caps how far the reconnect backoff grows; it does not limit the
  number of attempts.** The receiver reconnects indefinitely. With `Attempts: 5` and
  `Periodicity: 1s` the delay climbs to five seconds and stays there, so a value raised to stop the
  receiver giving up can return to a small number.
- **`Storage:EnableCapture` defaults to `false`** in the tracked `appsettings.json` files. They shipped
  `true` with an empty connection string, which made `ValidateOnStart` throw before the host started.
  Capture needs both `Storage:EnableCapture=true` and a connection string (user secrets, the
  environment, or the AppHost's Azurite container); a deployment that relied on the shipped `true`
  stops capturing until the flag is set explicitly.
- **OTLP exporters are registered only when `OTEL_EXPORTER_OTLP_ENDPOINT` is set.** They previously
  defaulted to `localhost:4317` and logged periodic connection failures in deployments with no
  collector, so no telemetry is exported unless the endpoint is configured.
- **BREAKING: `INmeaStreamReader` is byte-oriented.** `ReadLineAsync` returns
  `ValueTask<ReadOnlyMemory<byte>?>` (a buffer valid until the next call) instead of
  `Task<string?>`, and `DataAvailable` is removed. External implementations and callers need
  updating; new code should implement `INmeaConnection` instead.
- The receiver sources are rebuilt on the shared primitives listed under Added.
  `NetworkStreamNmeaReceiver`, `WebSocketNmeaReceiver`, `StreamNmeaReceiver`,
  `FileStreamNmeaReceiver`, `INmeaStreamReader` and `TcpClientNmeaStreamReader` remain as facades
  over them (`FileStreamNmeaReceiver` keeps its `string path` constructors alongside the injectable
  `IFileSystem` ones). Three behaviours differ: an over-long line with no newline is discarded
  through to the next newline instead of leaking its tail; file and blob sources pass bytes through
  without the previous Latin1 round-trip; and the hosts' receiver log categories are now
  `NmeaReceiver`, `TcpNmeaConnection` and `WebSocketNmeaConnection`.
- The network receiver is a native `IAsyncEnumerable` sequence; the observable API is retained as a
  copying projection for backwards compatibility.
- The reconnect backoff resets when the feed delivers its first sentence, not when the connection
  is accepted, so a peer that accepts and then closes or stays silent is retried with the configured
  backoff rather than once per base interval.
- Idle-read detection has its own 60-second default, independent of the reconnect settings. Derived
  from them it worked out to roughly five seconds, too short to tell a quiet feed from a dropped one.
- The storage queue is bounded: sentences are shed once `Storage:BoundedCapacity` is reached instead
  of growing memory without limit, and the drops are counted rather than hidden.
- Storage writes are retried before a batch is dead-lettered or, without a dead-letter path,
  surfaced to the caller rather than swallowed.
- The constant-delay retry helper is rebuilt on `Corvus.Retry` with a custom policy that preserves
  cancellation semantics. An interim move to [Polly](https://github.com/App-vNext/Polly) during this
  cycle was reverted after Polly adopted the Open Source Maintenance Fee.
- Tests run on `MSTest.Sdk` and the Microsoft.Testing.Platform runner. `dotnet test` no longer accepts
  VSTest-era switches: passing `--nologo`, `--logger` or `--test-adapter-path` makes the run report
  `Zero tests ran` rather than an unrecognised-argument error.
- Build conventions come from a checked-in `Solutions/Endjin.Conventions.props` with the .NET
  analyzers and code-style enforcement turned on. The copyright year comes from `SOURCE_DATE_EPOCH`,
  which the ZeroFailed build pins to the HEAD commit's timestamp so a commit rebuilds byte for byte
  in any year; an ad-hoc `dotnet build` without it uses the current year.

### Removed

- The `Endjin.RecommendedPractices.GitHub` build package and its StyleCop and Roslynator analyzers,
  which Central Package Management rejects; `Endjin.Conventions.props` replaces what they provided.
- The `settings.json` configuration file, replaced by `appsettings.json` (see Changed).

### Fixed

- A batch that exhausts its storage retries is no longer lost (see dead-lettering under Added).
- An hour-boundary race that could misroute append-blob writes; writes are now serialized.
- `Detailed` and `Debug` verbosity no longer suppress the statistics line.
- Dead-letter replay no longer drops empty sentences, so a replayed batch is byte-for-byte what
  the live path would have appended.
- `Ais:Telemetry:StatisticsPeriodicity`, `Ais:Telemetry:VesselInactivityTimeout` (when set) and
  the `Retry:Periodicity` values are validated as positive durations at start-up, and
  `StatisticsPeriodicity` now defaults to one minute instead of zero.
- The tracked `appsettings.json` no longer fails `ValidateOnStart` out of the box (see
  `Storage:EnableCapture` under Changed).

## [0.3.8] - 2025-03-18

Not recorded; see the compare link.

## [0.3.7] - 2025-02-26

Not recorded; see the compare link.

## [0.3.6] - 2025-02-23

Not recorded; see the compare link.

## [0.3.5] - 2025-02-23

Not recorded; see the compare link.

## [0.3.4] - 2025-02-23

Not recorded; see the compare link.

## [0.3.3] - 2025-02-23

Not recorded; see the compare link.

## [0.3.2] - 2025-02-22

Not recorded; see the compare link.

## [0.3.1] - 2024-10-09

Not recorded; see the compare link.

## [0.3.0] - 2024-10-09

Not recorded; see the compare link.

## [0.2.2] - 2024-10-09

Not recorded; see the compare link.

## [0.2.1] - 2024-06-04

Not recorded; see the compare link.

## [0.2.0] - 2022-10-25

### Added

- An `Errors` observable on `Ais.Net.Receiver.Receiver.ReceiverHost` ([#142]).

[Unreleased]: https://github.com/ais-dotnet/Ais.Net.Receiver/compare/0.3.8...HEAD
[0.3.8]: https://github.com/ais-dotnet/Ais.Net.Receiver/compare/0.3.7...0.3.8
[0.3.7]: https://github.com/ais-dotnet/Ais.Net.Receiver/compare/0.3.6...0.3.7
[0.3.6]: https://github.com/ais-dotnet/Ais.Net.Receiver/compare/0.3.5...0.3.6
[0.3.5]: https://github.com/ais-dotnet/Ais.Net.Receiver/compare/0.3.4...0.3.5
[0.3.4]: https://github.com/ais-dotnet/Ais.Net.Receiver/compare/0.3.3...0.3.4
[0.3.3]: https://github.com/ais-dotnet/Ais.Net.Receiver/compare/0.3.2...0.3.3
[0.3.2]: https://github.com/ais-dotnet/Ais.Net.Receiver/compare/0.3.1...0.3.2
[0.3.1]: https://github.com/ais-dotnet/Ais.Net.Receiver/compare/0.3.0...0.3.1
[0.3.0]: https://github.com/ais-dotnet/Ais.Net.Receiver/compare/0.2.2...0.3.0
[0.2.2]: https://github.com/ais-dotnet/Ais.Net.Receiver/compare/0.2.1...0.2.2
[0.2.1]: https://github.com/ais-dotnet/Ais.Net.Receiver/compare/0.2.0...0.2.1
[0.2.0]: https://github.com/ais-dotnet/Ais.Net.Receiver/compare/0.1.14...0.2.0
[#142]: https://github.com/ais-dotnet/Ais.Net.Receiver/pull/142
[#149]: https://github.com/ais-dotnet/Ais.Net.Receiver/issues/149
