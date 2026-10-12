# Receiver design: one line reader, one connection contract, one receiver

Planned by Claude Fable 5.1 (lead) and implemented by Claude Opus subagents on 2026-10-12, in two
passes. The first pass added WebSocket support; this second pass reworked file, TCP and WebSocket so
all three build from the same primitives. This document supersedes `websocket-receiver-design.md`.

## The problem after the first pass

Three transports, three line splitters and two connection abstractions:

| Source    | Line splitting                         | Connection abstraction                     | Reconnect loop |
|-----------|----------------------------------------|--------------------------------------------|----------------|
| File/blob | `StreamReader` + Latin1 re-encoding    | none (`StreamNmeaReceiver.OpenStreamAsync`) | none           |
| TCP       | `PipeReader` loop in the stream reader | `INmeaStreamReader` (host/port at connect) | shared core    |
| WebSocket | hand-written frame state machine       | `INmeaConnection` (endpoint at construction) | shared core  |

Each splitter had its own cap, CR handling and buffer policy, and each new transport would have
added a fourth.

## The layering

```
NmeaLineReader            bytes (PipeReader) -> NMEA lines       one implementation
      ^
INmeaConnection           connect / read line / kind             the only transport contract
  |- StreamNmeaConnection  "open a Stream"                        abstract primitive
  |    |- FileNmeaConnection   Spectre.IO file                    Finite
  |    |- TcpNmeaConnection    TcpClient + NetworkStream          Live
  |- WebSocketNmeaConnection   ClientWebSocket, one line reader per message   Live
      ^
NmeaReceiver              Live: reconnect/idle/metrics/spans; Finite: single pass
      ^
facades                   NetworkStreamNmeaReceiver, WebSocketNmeaReceiver,
                          StreamNmeaReceiver, FileStreamNmeaReceiver, TcpClientNmeaStreamReader
```

### `NmeaLineReader`

A `PipeReader` in, lines out. Splits on `\n`, trims one trailing `\r`, caps a line at 8 KiB
(`DefaultMaxLineLength`), drops an over-long line and resyncs at the next newline, emits a final
unterminated line at end of stream, and hands out slices of one reusable buffer that are valid only
until the next call. `Create(Stream, ...)` wraps any stream with `leaveOpen: true` so the caller keeps
ownership. This is the only place framing rules live; every transport inherits the same behaviour
and the same `NmeaLineDiscarded` diagnostics.

### `INmeaConnection` and `NmeaSourceKind`

The contract gained `Kind`: `Live` means end of stream or a failure is a dropped feed and the
receiver reconnects; `Finite` means the data is exhausted, the receiver completes, and a failure to
open propagates. The connection knows which it is, so a caller composing `new NmeaReceiver(new
FileNmeaConnection(...))` cannot accidentally replay a file forever.

### `StreamNmeaConnection`

The "I can open a `Stream`" primitive: opens the stream on connect, wraps it in a line reader, and
optionally paces reads with a per-line delay for replay. `Kind`, `EndpointPort`, `Transport`,
`ProtocolName`, `Connected`, `ReadLineAsync` and `DisposeAsync` are virtual so a subclass can
describe itself and hook teardown.

- `FileNmeaConnection` opens a Spectre.IO file. Finite.
- `TcpNmeaConnection` opens a `TcpClient`'s `NetworkStream`, carrying over the keep-alive, no-delay,
  30 s connect bound, socket-error classification, first-read diagnostics and graceful shutdown from
  the old reader. Live.

### `WebSocketNmeaConnection`

Does not open one stream; it reads one message at a time through .NET 10's
`WebSocketStream.CreateReadableMessageStream`, wrapping each message in its own `NmeaLineReader`.
That gives the WebSocket framing rules with no bespoke code: every `\n` inside a message ends a line,
the message boundary ends the last line (the reader emits the unterminated remainder at EOF), a
trailing `\n` produces no empty line, and lines may span frames. A peer Close surfaces as an empty
end of stream leaving the socket in `CloseReceived`, which is reported as end of stream so the
receiver reconnects.

### `NmeaReceiver` and `NmeaReceiverOptions`

The former internal core, now public, with an options record (`RetryPeriodicity`,
`RetryAttemptLimit`, `IdleTimeout`). `GetAsync` dispatches on `Connection.Kind`: the live loop is
the original code, unchanged; the finite path is a single connect, read-to-EOF, complete, with the
connection-state callback honoured and no outage accounting.

### Facades

Every previously public type keeps its signature so the published package stays source-compatible:

- `NetworkStreamNmeaReceiver(host, port, ...)` now builds a `TcpNmeaConnection` directly; the
  `INmeaStreamReader` constructor keeps its private adapter for callers with their own reader.
- `TcpClientNmeaStreamReader` is a thin adapter creating a `TcpNmeaConnection` per connect.
- `WebSocketNmeaReceiver` is a thin wrapper over `NmeaReceiver` + `WebSocketNmeaConnection`.
- `StreamNmeaReceiver` keeps its `OpenStreamAsync` extension point (the demo's `BlobNmeaReceiver`
  derives from it unchanged) and still yields owned copies of each line, because its callers collect
  them; the live receivers keep the reuse-until-next-`MoveNextAsync` contract, now documented on
  `INmeaReceiver`.

### Pipeline

`ReceiverPipeline.CreateConnection` maps `AisConnectionConfig` to a `TcpNmeaConnection` or
`WebSocketNmeaConnection`; `CreateReceiver` wraps it in an `NmeaReceiver` with options from the
`Retry` section. Log categories for the hosts are now `NmeaReceiver`, `TcpNmeaConnection` and
`WebSocketNmeaConnection`.

## Adding a transport now

Implement `INmeaConnection` (or derive from `StreamNmeaConnection` if the source is a `Stream`),
state its `Kind`, and hand it to `NmeaReceiver`. Framing, reconnect, idle detection, metrics, tracing
and health all come for free.

## Work packages

| # | Scope | Owner | Depends on |
|---|-------|-------|------------|
| 1 | `NmeaLineReader`, `NmeaSourceKind` on `INmeaConnection`, `NmeaReceiver` + options with the finite path, `StreamNmeaConnection`, `FileNmeaConnection`, facades rebuilt; 20 tests. | Opus | — |
| 2 | `TcpNmeaConnection` on the stream base, `TcpClientNmeaStreamReader` as adapter, `NetworkStreamNmeaReceiver` default constructor; 10 tests. | Opus | 1 |
| 3 | `WebSocketNmeaConnection` over per-message streams, rename from `ClientWebSocketNmeaConnection`; 5 tests. | Opus | 1 |
| — | Pipeline factory, this record, release notes, final verification. | Fable | 1, 2, 3 |

Packages 2 and 3 ran in parallel in one working tree with disjoint file ownership.

## Verification

- `dotnet build Solutions/Ais.Net.Receiver.slnx -c Release --no-incremental`: 0 warnings, 0 errors.
- `dotnet test --solution Solutions/Ais.Net.Receiver.slnx -c Release --no-build`: 259 passed
  (224 before this pass, 35 added), every pre-existing test unmodified apart from test fakes gaining
  the `Kind` member.

## Deliberate behaviour changes

- Over-long data with no newline is now discarded through to the next newline rather than leaking
  its tail as a garbage line, which depended on read boundaries before.
- `TcpFirstRead` reports the first line rather than the first raw buffer.
- `NetworkStreamNmeaReceiver(host, port, ...)` validates host and port at construction.
- File and blob sources no longer round-trip bytes through Latin1 strings; bytes pass through.
