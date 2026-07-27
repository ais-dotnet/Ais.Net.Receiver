# Production Reliability Plan — Ais.Net.Receiver

Hardening plan for running the receiver as a long-lived daemon. Scope: the **correctness,
resilience, maintainability, and testing** gaps found during review, profiling, and a 58-minute
leak soak. A memory leak was ruled out (heap bounded ~160–200 KB across 22M messages), and the
hot path is already lean (~232 B/line), so this plan is about *reliability*, not performance.

## Principles

- Each item is an **independent, small PR with tests**; nothing merges without covering tests.
- **No silent behaviour changes** — where behaviour shifts, it is config-gated with a safe default.
- **Tests-first** for the correctness and resilience items.
- New operational signals are exposed as **metrics** (OpenTelemetry meter) so failures are visible.

**Progress:** C1, R1, R2, T1, T2, T3, and M1 are implemented and tested (125 tests green). The
Corvus.Retry → Polly migration is also done. Remaining: T4 (optional CI soak).

---

## Workstream 1 — Correctness

### C1. Serialize append-blob writes and fix the hour-boundary race  ·  Priority: High  ·  Effort: S  ·  Risk: Low
**Problem.** The storage `ActionBlock` uses `MaxDegreeOfParallelism = StorageConfig.MaxDegreeOfParallelism`
(default **2**), so `PersistAsync` runs concurrently against one `AzureAppendBlobStorageClient`:
1. **Wrong-blob race** — between `EnsureCurrentHourBlobInitializedAsync()` and reading
   `this.appendBlobClient` (`AzureAppendBlobStorageClient.cs:90`), a concurrent call crossing the hour
   boundary can swap the field, so a batch for hour *H* lands in hour *H+1*'s blob.
2. **Reordering** — two concurrent `AppendBlockAsync` calls commit blocks in non-deterministic order,
   so the time-ordered `.nm4` capture is reordered at batch boundaries.

Append blobs serialize service-side anyway, so parallelism buys little throughput.

**Fix.**
- Default `MaxDegreeOfParallelism` to **1**; document that `> 1` is unsafe for ordered capture.
- Make the client thread-safe regardless: `EnsureCurrentHourBlobInitializedAsync` returns the
  `(AppendBlobClient client, string path)` snapshot; `PersistAsync` appends via that **local**, never
  the field — so an hour swap cannot misroute an in-flight append.

**Acceptance.** Unit test with a `FakeTimeProvider` stepping across an hour boundary under simulated
concurrency asserts each batch targets the correct blob path; test asserts submission-order appends at
`MaxDoP = 1`.

---

## Workstream 2 — Resilience & Observability

### R1. Surface backpressure drops  ·  Priority: High  ·  Effort: S  ·  Risk: Low
**Problem.** `RawSentences.Subscribe(batchBlock.AsObserver())` posts fire-and-forget. When the block
reaches `BoundedCapacity` (default 10,000), sentences are **dropped with no metric or log** — silent
data loss when storage stalls.

**Fix.** Replace `AsObserver()` with an explicit observer that inspects `batchBlock.Post(...)`'s
result; on `false`, increment a new `ais.receiver.sentences.dropped` counter and emit a rate-limited
warning. (Consider `SendAsync` with a short timeout for softer backpressure.)

**Acceptance.** Test drives sentences faster than a blocked `ActionBlock` and asserts the dropped
counter increments with no exceptions.

### R2. Retry storage writes; never silently drop a batch  ·  Priority: Medium  ·  Effort: M  ·  Risk: Low–Med
**Problem.** The `ActionBlock` catches `AppendBlockAsync` failures, logs, and **discards the batch** —
a transient outage past the SDK's retry loses data with only a log line.

**Fix.** Wrap `PersistAsync` in a bounded retry (`Corvus.Retry`, already referenced). On terminal
failure, increment `ais.storage.batches.failed` and optionally spill the batch to a local dead-letter
file for later replay. Verify `BlobClientOptions.Retry` is configured on the client.

**Acceptance.** Mock storage client that fails N times then succeeds proves retry; terminal-failure
test asserts the failed counter increments (and the dead-letter file is written, if implemented).

**Done (with replay).** `ResilientStorageClient` retries via Polly, then dead-letters each still-failing
batch to `DeadLetterPath` as an atomic `.nm4` file (staged then renamed, via `DeadLetterStore`). A
background `DeadLetterReplayer` sweeps that directory every `DeadLetterReplayIntervalSeconds` (default
60) and re-persists each batch once the backend recovers, deleting the file on success and stopping at
the first still-failing batch so it never hammers a down backend. Replay writes through the underlying
`AzureAppendBlobStorageClient`, which now serializes appends with a write lock, so replayed batches
never race live capture on the append blob. Surfaced via `ais.storage.batches.replayed`. Wired in
`ReceiverPipeline`/`StorageBatchPipeline`, so both hosts get it with no host changes. Covered by
`DeadLetterStore` round-trip tests and `DeadLetterReplayer` tests (replay-and-delete, leave-on-failure,
recover-on-later-sweep). *Caveat — at-least-once, current-hour:* a crash after a successful replay but
before the file delete would replay that batch again (harmless duplicate lines, which carry their own
timestamp tags), and replayed sentences land in the current hour's blob rather than their original
hour. Both are acceptable for a raw capture archive; exact-hour routing would be a further change.

### R3. Managed-identity auth  ·  Status: Not planned (by decision)
Connection-string auth is retained by choice for now. Recorded for context only; revisit only if
secret management becomes a requirement — the drop-in would be `DefaultAzureCredential` + a
`BlobServiceUri` config alongside the connection string.

---

## Workstream 3 — Maintainability

### M1. Extract shared host wiring  ·  Priority: Medium  ·  Effort: M–L  ·  Risk: Med
**Problem.** `Worker.cs` and `ReceiveCommand.cs` duplicate ~150 lines: metrics/health subscriptions,
verbosity-branch wiring, the `BatchBlock → ActionBlock → timer` storage pipeline, the 30-second
flush-on-shutdown, and the error-type switch. Two copies already drift risk.

**Fix.** Extract a shared `ReceiverPipeline` (builder) — e.g., in `ServiceDefaults` or a new shared
project — that both hosts configure. Hosts differ only in output sink (`AnsiConsole` vs `ILogger`),
injected as a small strategy. Single source for the pipeline, subscriptions, and shutdown flush.

**Acceptance.** Both hosts delegate to the shared component; all existing tests pass; the error-type
switch and batch setup exist exactly once. **Sequence last** so the extracted code already contains the
C1/R1/R2 fixes rather than migrating them twice.

**Done.** Added `ReceiverPipeline` (host + storage-client + batch-pipeline factory) and
`StorageBatchPipeline` (the `BatchBlock → ActionBlock → timer → backpressure → flush` component) to
`ServiceDefaults` — chosen because it already references both the core library and the Azure blob
storage project, and its `Microsoft.AspNetCore.App` framework reference supplies TPL Dataflow without
a new package. Both hosts now shed ~220 duplicated lines and supply only their sink callbacks
(`ILogger` vs `AnsiConsole`). The pipeline is unit-tested for the first time (batching, shutdown
flush, persist-error reporting). The verbosity/metrics *subscriptions* stay in each host because they
diverge by output sink; only the batch pipeline, storage-client stack, host construction, and
error-type switch were unified.

**Follow-up (backpressure made real).** While extracting, the `ActionBlock` was found to be unbounded,
so the `BatchBlock`'s bounded capacity rarely shed load — drops were effectively unreachable and memory
would instead grow in the action block's queue during a sustained storage stall. The action block is
now bounded (`MaxPendingBatches` = `MaxDegreeOfParallelism + 1`): when the persist stage stalls, the
buffers fill, backpressure propagates back to the source, and load is shed through the drop callback
(surfaced via the `ais.receiver.sentences.dropped` metric and a throttled log) instead of growing
without limit. Total buffered memory is now bounded end-to-end; the `BatchBlock` remains the main
configurable buffer. Covered by a new test that saturates the pipeline behind a stalled backend and
asserts load is shed.

---

## Workstream 4 — Testing

### T1. Reconnection churn / resource disposal  ·  Priority: High  ·  Effort: M
Loopback server that drops the connection every N seconds. Over a run, assert the receiver reconnects,
no exception escapes `StartAsync`, and (with periodic forced-GC live-heap checks) the heap stays
bounded — proving reader/`CancellationTokenSource` disposal on the reconnect path the stable-feed soak
never exercised.

### T2. Vessel-grouping memory  ·  Priority: Medium  ·  Effort: M
Feed many distinct MMSIs through `VesselNavigationWithNameStream` with a short inactivity timeout;
assert `GroupByUntil` groups are disposed (bounded object/handle count) after inactivity — the explicit
"prevent memory accumulation" logic that is currently untested.

### T3. Storage integration with Azurite  ·  Priority: Medium  ·  Effort: M–L  ·  **Done**
Azurite-backed (Testcontainers or local) test for `AzureAppendBlobStorageClient`: asserts bytes are
written correctly (no BOM, `\n`-separated, correct `raw/yyyy/MM/dd/…​.nm4` path), hour rollover creates
a new blob, and — with C1 — correct placement/ordering under concurrency. First coverage for the blob
write path.

Delivered, and extended past the client to the whole pipeline. `AzuriteFixture` +
`AzuriteIntegrationTestBase` share one lazily started container across the test process;
`ReceiverEndToEndTests` drives NMEA sentences through `ReceiverHost` decoding and the production
`ReceiverPipeline.CreateStorage` wiring into real blobs, covering the happy path, hour rollover,
dead-letter-then-replay after a storage failure, and load shedding under a stalled backend;
`StorageHealthCheckIntegrationTests` covers `StorageHealthCheck` against a real endpoint. See the
Integration tests section of `README.md` for the Docker requirement and the environment variables.

### T4. Long-run soak in CI (optional)  ·  Priority: Low  ·  Effort: S
Parameterize the leak harness as an on-demand/nightly soak (10–15 min) that fails the build on
unbounded live-heap growth.

---

## Sequencing

| Milestone | Items | Rationale |
|-----------|-------|-----------|
| **1 — Correctness + visibility** | C1, R1 | Small, highest-value; each ships with its tests. Stops silent data loss and misrouted writes first. |
| **2 — Resilience** | R2 | Retry/dead-letter so a transient outage never silently drops a batch. |
| **3 — Test hardening** | T1, T2, T3 | Close the soak's blind spots; first storage coverage. |
| **4 — Maintainability** | M1, T4 | Extract shared wiring *after* fixes exist, so they aren't migrated twice; add the CI soak. |

## Cross-cutting

- **New metrics:** `ais.receiver.sentences.dropped`, `ais.storage.batches.failed` — registered on the
  existing meter and documented.
- **Docs:** update config docs for `MaxDegreeOfParallelism` semantics, `BlobServiceUri`/managed
  identity, and the new metrics.
- **Config-gated** behaviour changes with safe defaults; no surprises on upgrade.

## Out of scope (perf — track separately)

`Position` → `readonly record struct` (upstream `Ais.Net.Models`), pooling the storage byte-copy, and a
typed zero-alloc push API. These are allocation optimizations, not reliability; revisit only if a
high-rate scenario (archive replay, multi-feed aggregation) emerges.
