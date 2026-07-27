# Ais.Net.Receiver Observability Playbook

This playbook provides operational guidance for monitoring, troubleshooting, and maintaining the Ais.Net.Receiver system using OpenTelemetry telemetry.

## Table of Contents

- [Overview](#overview)
- [Key Metrics](#key-metrics)
- [Common Queries](#common-queries)
- [Dashboard Layouts](#dashboard-layouts)
- [Alerting Strategies](#alerting-strategies)
- [Troubleshooting Scenarios](#troubleshooting-scenarios)
- [Performance Baselines](#performance-baselines)

## Overview

The Ais.Net.Receiver exposes comprehensive telemetry via OpenTelemetry:

- **Traces**: Connection lifecycle, message processing, storage operations
- **Metrics**: Message throughput, error rates, connection health, storage performance
- **Logs**: Structured logs with automatic trace correlation

### Telemetry Architecture

```
AIS Stream → NetworkStreamNmeaReceiver → ReceiverHost → Worker
                      ↓                       ↓           ↓
              Activity Traces          Activity Traces  Metrics
                      ↓                       ↓           ↓
              OpenTelemetry Collector / Aspire Dashboard
```

### Service Naming

- **Service Name**: `Ais.Net.Receiver`
- **Service Namespace**: `ais-net`
- **Meter Name**: `Ais.Net.Receiver`
- **Activity Source**: `Ais.Net.Receiver`

## Key Metrics

### Message Processing Metrics

#### `ais.receiver.messages.received` (Counter)
- **Type**: Counter
- **Unit**: messages
- **Dimensions**: `ais.message_type` (1-27)
- **Purpose**: Track total messages received by type

**Expected Values**:
- Type 1-3 (Position Reports): 70-80% of traffic
- Type 5 (Ship Static): 5-10%
- Type 18-19 (Class B Position): 10-15%
- Type 24 (Static Data): 5-10%

**Query Example** (Prometheus):
```promql
# Messages per second by type
rate(ais_receiver_messages_received_total{ais_message_type="1"}[5m])

# Total message rate
sum(rate(ais_receiver_messages_received_total[5m]))
```

#### `ais.receiver.sentences.received` (Counter)
- **Type**: Counter
- **Unit**: sentences
- **Purpose**: Track raw NMEA sentences (includes multi-part messages)

**Typical Ratio**: Sentences:Messages ≈ 1.1:1 (due to multi-part Type 5/24)

#### `ais.receiver.errors` (Counter)
- **Type**: Counter
- **Dimensions**: `error.type` (parse_error, unsupported_message — see `ReceiverPipeline.ClassifyError`)
- **Purpose**: Track parsing and processing errors

**Healthy Range**: 3-5% of sentences received, against a live coastal feed

This is not the sub-0.1% figure a parser error rate usually implies, and the difference is a
property of the feed rather than a fault. A measured 11-minute run against the Norwegian Coastal
Administration feed (`153.44.253.27:5631`) produced a steady 3-4% error rate, and every failure was
the same one:

```
Invalid data. Unrecognized talker id - cannot end with 50
Bad line: \s:2573105,c:1785153033*07\!B2VDM,1,1,6,B,13mqaH0PAr0hh4rR03rQ3@i40<31,0*2B
```

`50` and `49` are ASCII `2` and `1`: the parser accepts the `!BSVDM` talker id but rejects `!B1VDM`
and `!B2VDM`. Sampling 1,605 sentences straight off the socket gave 1,544 `!BSVDM`, 9 `!BSVDO`,
49 `!B2VDM` and 3 `!B1VDM` - so **3.2% of that feed is rejected before decoding**. The limitation is
in the `Ais.Net.Models` NMEA parser, not in this receiver.

Two consequences for alerting:

- A threshold below the feed's own baseline fires permanently and gets muted, taking the useful
  signal with it. Set it above the observed baseline for the feed you actually consume, and
  re-measure when the feed changes.
- Because these sentences fail before decoding, they are counted in
  `ais.receiver.sentences.received` but never reach `ais.receiver.messages.received`. Compute the
  rate against **sentences**; dividing errors by messages overstates it, since the denominator
  excludes the very sentences that failed.

**Alert Threshold**: >8% of sentences, or any sustained step-change from the established baseline -
the shape matters more than the absolute number, since a jump indicates a new class of failure
rather than more of the known one.

**Query Example**:
```promql
# Error rate as a percentage of sentences received (not messages - see above)
(sum(rate(ais_receiver_errors_total[5m])) / sum(rate(ais_receiver_sentences_received_total[5m]))) * 100

# Split by classification, to separate a new failure mode from the known talker-id baseline
sum(rate(ais_receiver_errors_total[5m])) by (error_type)
```

### Connection Health Metrics

#### `ais.receiver.connection.attempts` (Counter)
- **Type**: Counter
- **Purpose**: Track connection attempts to AIS stream

**Expected Pattern**: Should be low after initial connection

#### `ais.receiver.connection.failures` (Counter)
- **Type**: Counter
- **Purpose**: Track failed connection attempts

**Alert Threshold**: >0 failures per minute indicates network issues

#### `ais.receiver.connection.consecutive_failures` (UpDownCounter)
- **Type**: UpDownCounter (gauge-like)
- **Purpose**: Current consecutive failure count

**Alert Threshold**: >5 consecutive failures
**Recovery**: Resets to 0 on successful connection

#### `ais.receiver.retry.attempts` (Counter)
- **Type**: Counter
- **Dimensions**: `component` (currently only `connection`). Deliberately not dimensioned by attempt
  number, which would add one time series per attempt value.
- **Purpose**: Track retry behavior

**Expected Behavior**: Should converge to 0 after connection established

### Storage Performance Metrics

#### `ais.storage.write.operations` (Counter)
- **Type**: Counter
- **Dimensions**: none
- **Purpose**: Track storage batch write counts

#### `ais.storage.write.duration` (Histogram)
- **Type**: Histogram
- **Unit**: milliseconds
- **Buckets**: 1, 2.5, 5, 10, 25, 50, 100, 250, 500, 1000, 2500, 5000, 10000, 30000
- **Purpose**: Measure storage latency

**Healthy Percentiles**:
- P50: <100ms
- P95: <500ms
- P99: <1000ms

Measured P50 against the local Azurite emulator is ~9.5ms; a real blob endpoint is slower. The
boundaries start at 1ms so the fast path stays resolved rather than collapsing into the first bucket.

**Query Example**:
```promql
# P95 storage latency, in milliseconds
histogram_quantile(0.95, sum(rate(ais_storage_write_duration_bucket[5m])) by (le))
```

#### `ais.storage.bytes.written` (Counter)
- **Type**: Counter
- **Unit**: bytes (`By`)
- **Purpose**: Total bytes appended to storage

#### `ais.receiver.batch.size` (Histogram)
- **Type**: Histogram
- **Unit**: messages
- **Buckets**: 1, 10, 50, 100, 250, 500, 1000, 2500, 5000, 10000
- **Purpose**: Distribution of batch sizes handed to storage

#### `ais.storage.batches.pending` (ObservableGauge)
- **Type**: Gauge
- **Purpose**: Number of batches waiting to be written

**Healthy Range**: 0-5
**Alert Threshold**: >20 (indicates storage falling behind)

#### `ais.storage.batches.failed` (Counter)
- **Type**: Counter
- **Purpose**: Batches abandoned or dead-lettered after write retries were exhausted

**Alert Threshold**: >0 (data is being written to the dead-letter path, not storage)

#### `ais.storage.batches.replayed` (Counter)
- **Type**: Counter
- **Purpose**: Dead-lettered batches successfully replayed after the backend recovered

### Backpressure

#### `ais.receiver.sentences.dropped` (Counter)
- **Type**: Counter
- **Unit**: sentences
- **Purpose**: Sentences shed because the storage batch buffer was full. This is the real
  backpressure signal — there is no queue-depth gauge for buffered messages.

**Healthy Range**: 0
**Alert Threshold**: >0 (capture is losing data)

#### `ais.receiver.message.processing.duration` (Histogram)
- **Type**: Histogram
- **Unit**: milliseconds
- **Buckets**: 0.0005, 0.001, 0.0025, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 1, 5, 25
- **Purpose**: Per-message decode cost

Measured P50 0.0009ms, P95 0.022ms, P99 0.145ms at ~40 sentences/sec. Decoding is sub-microsecond, so
the range starts at 0.0005ms - low enough to bracket the median, but not below the point where
`Stopwatch` resolution (~100ns) would make the buckets measure timer noise. The upper boundaries exist
to catch a decode delayed behind a GC pause rather than to resolve normal work.

### Resource Utilization

#### `process.runtime.dotnet.gc.allocations.size` (Counter)
- **Type**: Counter (from Runtime Instrumentation)
- **Purpose**: Memory allocation rate

**Expected**: <50MB/sec at 100 msg/sec

#### `process.runtime.dotnet.gc.collections.count` (Counter)
- **Type**: Counter
- **Dimensions**: `generation` (0, 1, 2)
- **Purpose**: GC pressure

**Healthy Ratio**: Gen0:Gen1:Gen2 ≈ 100:10:1

## Common Queries

### Message Processing

#### Current Message Throughput
```promql
# Messages per second
sum(rate(ais_receiver_messages_received_total[1m]))

# By message type
sum(rate(ais_receiver_messages_received_total[1m])) by (ais_message_type)
```

#### Message Type Distribution
```promql
# Percentage by type
(sum(rate(ais_receiver_messages_received_total[5m])) by (ais_message_type)
 / sum(rate(ais_receiver_messages_received_total[5m]))) * 100
```

#### Error Rate Trending
```promql
# Errors as percentage of sentences received. Sentences, not messages: a sentence that fails to
# decode never becomes a message, so a messages denominator excludes the failures and overstates
# the rate.
(sum(rate(ais_receiver_errors_total[5m]))
 / sum(rate(ais_receiver_sentences_received_total[5m]))) * 100

# By error type
sum(rate(ais_receiver_errors_total[5m])) by (error_type)
```

### Connection Health

#### Connection Uptime
```promql
# Time since last connection failure (in minutes)
(time() - max(ais_receiver_connection_failures_total)) / 60
```

#### Retry Rate
```promql
# Retries per minute
sum(rate(ais_receiver_retry_attempts_total[1m]))
```

### Storage Performance

#### Storage Latency Percentiles
```promql
# P50, P95, P99
histogram_quantile(0.50, sum(rate(ais_storage_write_duration_bucket[5m])) by (le))
histogram_quantile(0.95, sum(rate(ais_storage_write_duration_bucket[5m])) by (le))
histogram_quantile(0.99, sum(rate(ais_storage_write_duration_bucket[5m])) by (le))
```

#### Storage Throughput
```promql
# Operations per second
sum(rate(ais_storage_write_operations_total[1m]))

# Bytes written per second (approximate: messages * avg_size)
sum(rate(ais_storage_bytes_written_total[1m]))
```

### Resource Utilization

#### Memory Usage Trend
```promql
# Working set memory
process_working_set_bytes

# GC heap size
process_runtime_dotnet_gc_heap_size_bytes
```

#### CPU Usage
```promql
# CPU seconds per second (0-1 range per core)
rate(process_cpu_seconds_total[1m])
```

## Dashboard Layouts

### Overview Dashboard

**Purpose**: High-level health at a glance

**Panels**:
1. **Current Status** (Single Stat)
   - Connection Status (Up/Down)
   - Message Rate (msg/sec)
   - Error Rate (%)
   - Storage Lag (batches pending)

2. **Message Throughput** (Time Series)
   - `sum(rate(ais_receiver_messages_received_total[1m]))`
   - 15-minute window

3. **Error Rate** (Time Series)
   - `(sum(rate(ais_receiver_errors_total[1m])) / sum(rate(ais_receiver_sentences_received_total[1m]))) * 100`
   - Baseline band at the feed's measured rate (~3-4% on the Norwegian feed), alert line at 8%

4. **Connection Health** (Status History)
   - `ais_receiver_connection_consecutive_failures`
   - Show disconnections as spikes

### Message Processing Dashboard

**Purpose**: Detailed message processing analysis

**Panels**:
1. **Message Type Distribution** (Pie Chart)
   - `sum(rate(ais_receiver_messages_received_total[5m])) by (ais_message_type)`

2. **Messages by Type Over Time** (Stacked Area)
   - Individual series for types 1-3, 5, 18-19, 24

3. **Parse Error Breakdown** (Bar Chart)
   - `sum(rate(ais_receiver_errors_total[5m])) by (error_type)`

4. **Top Vessels by Traffic** (Table)
   - Query traces for unique MMSIs (requires trace backend)

### Storage Performance Dashboard

**Purpose**: Storage operations and latency

**Panels**:
1. **Storage Latency Heatmap**
   - `ais_storage_write_duration_bucket`
   - Show P50, P95, P99 lines

2. **Batch Processing** (Time Series)
   - `ais_storage_batches_pending` (gauge)
   - Batch completion rate

3. **Storage Operations** (Counter)
   - `sum(rate(ais_storage_write_operations_total[1m]))`

4. **Blob Rotations** (Events)
   - Query traces for `storage.blob.rotated` events

### System Resources Dashboard

**Purpose**: Infrastructure health

**Panels**:
1. **Memory Usage** (Time Series)
   - Working set, heap size, allocated memory

2. **GC Collections** (Time Series)
   - `rate(process_runtime_dotnet_gc_collections_count[1m])` by generation

3. **CPU Usage** (Gauge)
   - `rate(process_cpu_seconds_total[1m])`

4. **Thread Pool** (Time Series)
   - `process_runtime_dotnet_threadpool_threads_count`

## Alerting Strategies

### Critical Alerts

#### Connection Down
```yaml
alert: AISConnectionDown
expr: ais_receiver_connection_consecutive_failures > 5
for: 1m
severity: critical
summary: "AIS stream connection failed"
description: "{{ $value }} consecutive connection failures detected"
```

#### High Error Rate
```yaml
# Denominator is sentences, not messages: failing sentences never become messages, so dividing by
# messages inflates the rate. The 8% threshold sits above the ~3-4% talker-id baseline measured on
# the Norwegian feed - re-measure this for your own feed before relying on it.
alert: AISHighErrorRate
expr: (sum(rate(ais_receiver_errors_total[5m])) / sum(rate(ais_receiver_sentences_received_total[5m]))) * 100 > 8
for: 5m
severity: critical
summary: "High AIS sentence error rate"
description: "Error rate is {{ $value | printf \"%.2f\" }}% of sentences (threshold: 8%)"
```

#### Storage Falling Behind
```yaml
alert: AISStorageLagging
expr: ais_storage_batches_pending > 20
for: 2m
severity: critical
summary: "Storage cannot keep up with message rate"
description: "{{ $value }} batches pending (threshold: 20)"
```

### Warning Alerts

#### Elevated Error Rate
```yaml
# A step-change relative to the previous hour, rather than an absolute number. This is the alert that
# catches a new failure mode appearing on top of the known talker-id baseline, which a fixed
# threshold set above that baseline would miss.
alert: AISElevatedErrorRate
expr: |
  (sum(rate(ais_receiver_errors_total[5m])) / sum(rate(ais_receiver_sentences_received_total[5m])))
  > 1.5 * (sum(rate(ais_receiver_errors_total[1h] offset 1h)) / sum(rate(ais_receiver_sentences_received_total[1h] offset 1h)))
for: 10m
severity: warning
summary: "AIS error rate has stepped up from its established baseline"
```

#### Slow Storage Operations
```yaml
# The instrument records milliseconds, so the threshold is 1000, not 1.0. Against a real blob
# endpoint P50 sits in the tens of milliseconds; a P95 past a second means throttling or a stall.
alert: AISSlowStorage
expr: histogram_quantile(0.95, sum(rate(ais_storage_write_duration_bucket[5m])) by (le)) > 1000
for: 5m
severity: warning
summary: "Storage operations are slow"
description: "P95 latency is {{ $value | printf \"%.0f\" }}ms (threshold: 1000ms)"
```

#### High Memory Usage
```yaml
alert: AISHighMemory
expr: process_working_set_bytes > 1073741824  # 1GB
for: 5m
severity: warning
summary: "High memory usage detected"
```

### Informational Alerts

#### Connection Restored
```yaml
alert: AISConnectionRestored
expr: ais_receiver_connection_consecutive_failures == 0 and ais_receiver_connection_consecutive_failures offset 1m > 0
for: 0m
severity: info
summary: "AIS connection restored"
```

## Troubleshooting Scenarios

### Scenario 1: No Messages Received

**Symptoms**:
- `ais_receiver_messages_received_total` not incrementing
- `ais_receiver_connection_consecutive_failures` increasing

**Diagnosis**:
1. Check connection status: `ais_receiver_connection_consecutive_failures`
2. View connection events: filter logs to event ids 4000-4005 (stream connect/retry/idle/error) and 4010-4024 (TCP-level detail, including the classified socket errors)
3. Check network connectivity to AIS host

**Queries**:
```promql
# Connection attempts in last hour
increase(ais_receiver_connection_attempts_total[1h])

# Current consecutive failures
ais_receiver_connection_consecutive_failures
```

**Common Causes**:
- Network partition to AIS provider
- Firewall blocking TCP connection
- AIS provider service down
- Incorrect host/port configuration

**Resolution**:
1. Verify network connectivity: `telnet <host> <port>`
2. Check firewall rules
3. Verify configuration in `appsettings.json`
4. Contact AIS provider

### Scenario 2: High Error Rate

**Symptoms**:
- `ais_receiver_errors_total` increasing rapidly
- Error rate >1%

**Diagnosis**:
1. Check error type distribution:
   ```promql
   sum(rate(ais_receiver_errors_total[5m])) by (error_type)
   ```

2. View error traces: Filter by `ActivityStatusCode == Error`

3. Examine logs: Filter by `LogLevel >= Error`

**Error Types**:
- **parse_error**: Malformed NMEA sentences
  - *Cause*: Data corruption, incompatible AIS version
  - *Resolution*: Contact AIS provider

- **unsupported_message**: Message type not implemented
  - *Cause*: Receiving AIS types >27 or binary messages
  - *Resolution*: Log and ignore, or implement support

- **unknown**: Unexpected errors
  - *Cause*: Application bugs
  - *Resolution*: Review stack traces, file bug report

### Scenario 3: Storage Lagging

**Symptoms**:
- `ais_storage_batches_pending` >20
- `ais_receiver_sentences_dropped_total` increasing (batch buffer shedding load)

**Diagnosis**:
1. Check storage latency:
   ```promql
   histogram_quantile(0.95, rate(ais_storage_write_duration_bucket[5m]))
   ```

2. View storage traces: Filter activities by `ProcessBatch`

3. Check Azure Storage metrics (if using Blob Storage)

**Common Causes**:
- High message volume exceeding storage throughput
- Azure Storage throttling
- Network latency to storage account
- Undersized `WriteBatchSize` configuration

**Resolution**:
1. Increase `WriteBatchSize` in configuration
2. Increase `MaxDegreeOfParallelism` for batch processing
3. Scale up Azure Storage tier
4. Reduce `BatchTimeoutSeconds` to flush more frequently

### Scenario 4: Memory Leak

**Symptoms**:
- `process_working_set_bytes` steadily increasing
- No corresponding increase in message rate
- Frequent Gen2 garbage collections

**Diagnosis**:
1. Monitor memory trend:
   ```promql
   process_working_set_bytes
   process_runtime_dotnet_gc_heap_size_bytes
   ```

2. Check GC collection frequency:
   ```promql
   rate(process_runtime_dotnet_gc_collections_count{generation="2"}[5m])
   ```

3. Take memory dump: `dotnet-gcdump`

**Common Causes**:
- Unbounded observable subscriptions
- Unclosed Activity or Metric handles
- Batch block not completing
- Large message accumulation in queues

**Resolution**:
1. Review subscription disposal in `Worker.cs`
2. Check for proper `Dispose` patterns
3. Restart service as temporary mitigation
4. Profile with dotnet-trace/dotnet-gcdump

### Scenario 5: Trace Explosion (High Cardinality)

**Symptoms**:
- APM backend (Jaeger/Tempo) memory exhaustion
- Slow trace queries
- Backend reporting high cardinality warnings

**Diagnosis**:
1. Check span *volume*, not attribute cardinality, first - `ProcessMessage` runs once per sentence, so
   at 40 sentences/sec an unsampled deployment emits ~3.5M spans/day
2. Confirm the sampling configuration is actually in effect (see below)

**Resolution**:

`ActivityExtensions` deliberately records MMSI, vessel name, call sign, position and timestamp as
**span attributes**. That is intentional and should not be "fixed" by moving them to events: a span
attribute is one indexed field on one span record, not a metric dimension, so a distinct value costs
nothing ongoing - and finding the spans that handled a given vessel is the entire purpose of trace
search. An earlier revision moved these to events for cardinality reasons and thereby removed the
ability to query by vessel at all, which is the more likely cause of a complaint here than backend
memory pressure.

Cardinality discipline belongs on **metric** dimensions, where each distinct value is a new time
series. Those are kept bounded: message type (28 values), ship type, station id, `error.type`, and
`component` on the retry counter.

If backend pressure is genuinely span volume:

1. Lower `OpenTelemetry:TraceSamplingRatio`, or set `OTEL_TRACES_SAMPLER` /
   `OTEL_TRACES_SAMPLER_ARG` - the environment variables take precedence and disable the programmatic
   sampler entirely
2. Confirm the process is not running in the Development environment, which applies `AlwaysOnSampler`

## Performance Baselines

### Low Volume (<100 msg/sec) - measured

From an 11-minute run against the Norwegian Coastal Administration feed
(`153.44.253.27:5631`), Release build, appending to a local Azurite emulator, on a 20-core Linux
container:

| Measure | Observed |
| --- | --- |
| Sentence rate | ~40 sentences/sec |
| Message rate | ~31 messages/sec |
| Error rate | 3-4% of sentences (all talker-id rejections; see Key Metrics) |
| Managed heap | 1.83 -> 1.99 MB, ~20k objects, flat |
| Working set | 99.6 MB rising to ~116 MB over ~5 min, then flat |
| Allocation rate | ~84 KB/sec (44 MB total over 9 min) |
| GC | 1 gen0, 1 gen1, 2 gen2 collections in 9 min; pause time ~0 |
| CPU | 4.28 s over 540 s = **0.79% of one core** |
| Storage write P50 | ~9.5 ms (Azurite; a real endpoint is slower) |
| Batch size | ~412 messages, flushing on the 10s timeout before reaching `writeBatchSize` 500 |
| Batches pending | 0 throughout |
| Lock contention | 3 events total |

Two notes on the memory figure. The working set is runtime-dominated - the managed heap is under
2 MB, while mapped CoreLib, JIT'd code, libcoreclr/libclrjit and ICU account for ~40 MB - so it
reflects the .NET runtime and the loaded assembly set far more than message volume. And the rise to
116 MB is warm-up (3,176 methods JIT'd, 1.26 s of JIT time) that plateaus; it is not accumulation.

### Higher volumes - not measured

The figures below are extrapolations, not observations. Given that 31 msg/sec consumed 0.79% of one
core, headroom is expected to be very large and these are almost certainly conservative - but they
have not been tested and should not be treated as baselines until they are.

- **100-1000 msg/sec**: storage latency and batch flush behaviour become the variables of interest,
  since batches start filling to `writeBatchSize` before the timeout rather than after it
- **>1000 msg/sec**: the bounded storage queue (`boundedCapacity`, default 10000) and
  `ais.receiver.sentences.dropped` are the signals to watch; CPU is unlikely to bind first

**Optimization Tips**:
- Increase `WriteBatchSize` to 1000+
- Increase `MaxDegreeOfParallelism` to 4-8
- Use premium Azure Storage tier
- Consider horizontal scaling

## Additional Resources

- [Telemetry Testing Guide](./testing-guide.md)
- [OpenTelemetry Semantic Conventions](https://opentelemetry.io/docs/specs/semconv/)
- [Aspire Dashboard Documentation](https://learn.microsoft.com/dotnet/aspire/fundamentals/dashboard)
- [Azure Monitor OpenTelemetry](https://learn.microsoft.com/azure/azure-monitor/app/opentelemetry-enable)
