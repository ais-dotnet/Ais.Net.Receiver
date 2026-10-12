# Ais.Net.Broadcaster: design and implementation plan

Planned by Claude Fable 5.1 (lead) and implemented by Claude Opus subagents on 2026-10-12. The
sink-side counterpart of the receiver: it takes NMEA sentences from any `INmeaReceiver` and fans
them out to TCP and WebSocket clients, so a receiver can be tested, demonstrated and soaked without
depending on a public feed being up.

## Why it lives in this repository for now

The broadcaster's input side is the receiver's own primitives (`NmeaReceiver`, `FileNmeaConnection`,
`TcpNmeaConnection`, `WebSocketNmeaConnection`, `NmeaLineReader`; see `receiver-design.md`), which
are unreleased. A separate `ais-dotnet/Ais.Net.Broadcaster` repository could only consume them from
NuGet once the receiver ships, so the two projects are kept self-contained here, with their own
package metadata and README material, and can be moved into their own repository afterwards as a
cut-and-paste rather than a rewrite.

## Layering

```
INmeaReceiver (file / TCP / WebSocket source, from the receiver)
      |
BroadcastPipeline          pacing (none | fixed rate | tag-block timestamps), loop-on-end,
      |                    fault injection (disconnect, silence, malformed, over-long)
      v
INmeaBroadcaster           Name, ClientCount, StartAsync, BroadcastAsync, DisconnectAllAsync, StopAsync
  |- TcpNmeaBroadcaster    TcpListener; newline-terminated lines (the Kystverket shape)
  |- WebSocketNmeaBroadcaster  minimal Kestrel endpoint; one sentence per message, or newline-joined batches
      ^
ais-broadcaster CLI        replay <file> | relay <tcp://|ws://|wss://> ; shipped as a dotnet tool
```

### Sinks

Each client gets a bounded queue drained by its own writer task, so one slow consumer never blocks
`BroadcastAsync` or the other clients. `SlowClientPolicy` decides whether a full queue drops the
oldest sentences (the client stays connected and skips ahead) or disconnects the client. TCP
coalesces queued lines into writes of up to 16 KiB. WebSocket framing is either one text message
per sentence or batches flushed by size or interval, which are the two shapes the receiver's
WebSocket transport accepts. `DisconnectAllAsync` exists purely for fault injection: it drops every
client while continuing to listen, so a receiver under test must reconnect.

### Pipeline

All timing runs through a `TimeProvider`, which is what makes the pacing and fault tests
deterministic. Tag-block pacing reproduces a recording's real rhythm from each sentence's `c:`
timestamp, divided by a speed factor and clamped to a maximum gap so a hole in a recording does not
stall the replay; a backward step between stations' clocks is treated as no delay. Faults are
checked per sentence: time-based ones (disconnect, silence) against the provider, count-based ones
(malformed, over-long) against a running sentence number. A finite source with `Loop` is
re-enumerated when it ends; an empty source is not looped.

### CLI

`replay` defaults to tag-block pacing and looping; `relay` defaults to no pacing. Both share the
listener, framing, queue, pacing and fault options. Logs go to stderr so the live statistics table
on stdout stays intact; when stdout is redirected a plain statistics line is printed every ten
seconds instead. Ctrl+C or SIGTERM stops cleanly with exit code 0; a second Ctrl+C terminates.

### Samples

`samples/kystverket-20260511T00.nm4` is the first 2,000 lines of a real capture (about 53 seconds,
NLOD 2.0, credit line in `samples/README.md`); `samples/sample.nm4` is a readable sixteen-line file
with synthetic tag blocks. The Aspire AppHost replays the former and, when `AisSource=broadcaster`
is set, points the worker at its WebSocket endpoint, so the whole local system runs offline.

## Work packages and ownership

| # | Scope | Owner | Depends on |
|---|-------|-------|------------|
| 0 | Projects, solution entries, `INmeaBroadcaster`, the option records, compiling stubs. | Fable | — |
| 1 | `TcpNmeaBroadcaster`, `WebSocketNmeaBroadcaster`, transport log events (5000-5099), 28 tests using the receiver's own connections as clients. | Opus | 0 |
| 2 | `BroadcastPipeline`, pacing, faults, pipeline log events (5100-5149), 15 deterministic tests on a fake time provider. | Opus | 0 |
| 3 | The CLI, the synthetic sample, the end-to-end test, AppHost wiring and the `AisSource` switch, README and release notes. | Opus | 1, 2 |
| — | The real-recording sample and its test, this record, final verification. | Fable | 3 |

Packages 1 and 2 ran in parallel in one working tree with disjoint file ownership, which is why the
contracts and stubs were written first.

## Verification

- `dotnet build Solutions/Ais.Net.Receiver.slnx -c Release --no-incremental`: 0 warnings, 0 errors.
- `dotnet test --solution Solutions/Ais.Net.Receiver.slnx -c Release --no-build`: 306 passed
  (259 receiver, 47 broadcaster).
- `ais-broadcaster replay samples/kystverket-20260511T00.nm4 --ws 18081 --tcp 15632` consumed by the
  receiver console host with `Ais__Connection__Transport=WebSocket`: live decode over the WebSocket
  transport against real sentences.

## Follow-ups

- `simulate`: synthetic vessels need an AIS encoder for message types 1, 5, 18 and 24, which
  Ais.Net does not have; a recording-free demo would start there.
- Blob replay in the CLI, reusing the demo's `BlobNmeaReceiver`.
- Extraction into `ais-dotnet/Ais.Net.Broadcaster` once the receiver's new primitives are released.
