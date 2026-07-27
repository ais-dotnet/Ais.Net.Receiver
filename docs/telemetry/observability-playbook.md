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
- **Meter Name**: `Ais.Net.Receiver.Metrics`
- **Activity Source**: `Ais.Net.Receiver`

## Key Metrics

### Message Processing Metrics

#### `ais.messages.received` (Counter)
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
rate(ais_messages_received_total{ais_message_type="1"}[5m])

# Total message rate
sum(rate(ais_messages_received_total[5m]))
```

#### `ais.sentences.received` (Counter)
- **Type**: Counter
- **Unit**: sentences
- **Purpose**: Track raw NMEA sentences (includes multi-part messages)

**Typical Ratio**: Sentences:Messages ≈ 1.1:1 (due to multi-part Type 5/24)

#### `ais.errors.received` (Counter)
- **Type**: Counter
- **Dimensions**: `error.type` (parse_error, unsupported_message, unknown)
- **Purpose**: Track parsing and processing errors

**Healthy Range**: <0.1% of messages received
**Alert Threshold**: >1% error rate

**Query Example**:
```promql
# Error rate percentage
(sum(rate(ais_errors_received_total[5m])) / sum(rate(ais_messages_received_total[5m]))) * 100
```

### Connection Health Metrics

#### `ais.connection.attempts` (Counter)
- **Type**: Counter
- **Purpose**: Track connection attempts to AIS stream

**Expected Pattern**: Should be low after initial connection

#### `ais.connection.failures` (Counter)
- **Type**: Counter
- **Purpose**: Track failed connection attempts

**Alert Threshold**: >0 failures per minute indicates network issues

#### `ais.connection.consecutive_failures` (UpDownCounter)
- **Type**: UpDownCounter (gauge-like)
- **Purpose**: Current consecutive failure count

**Alert Threshold**: >5 consecutive failures
**Recovery**: Resets to 0 on successful connection

#### `ais.connection.retry_attempts` (Counter)
- **Type**: Counter
- **Dimensions**: `component`, `attempt`
- **Purpose**: Track retry behavior

**Expected Behavior**: Should converge to 0 after connection established

### Storage Performance Metrics

#### `ais.storage.operations` (Counter)
- **Type**: Counter
- **Dimensions**: `operation` (append, create_blob, rotate)
- **Purpose**: Track storage operation counts

#### `ais.storage.operation.duration` (Histogram)
- **Type**: Histogram
- **Unit**: seconds
- **Buckets**: 0.01, 0.05, 0.1, 0.5, 1.0, 5.0, 10.0
- **Purpose**: Measure storage latency

**Healthy Percentiles**:
- P50: <100ms
- P95: <500ms
- P99: <1s

**Query Example**:
```promql
# P95 storage latency
histogram_quantile(0.95, sum(rate(ais_storage_operation_duration_bucket[5m])) by (le))
```

#### `ais.storage.batches_pending` (ObservableGauge)
- **Type**: Gauge
- **Purpose**: Number of batches waiting to be written

**Healthy Range**: 0-5
**Alert Threshold**: >20 (indicates storage falling behind)

### Resource Utilization

#### `ais.messages_queued` (ObservableGauge)
- **Type**: Gauge
- **Purpose**: Messages buffered in dataflow pipeline

**Healthy Range**: 0-1000
**Alert Threshold**: >10000 (backpressure building)

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
sum(rate(ais_messages_received_total[1m]))

# By message type
sum(rate(ais_messages_received_total[1m])) by (ais_message_type)
```

#### Message Type Distribution
```promql
# Percentage by type
(sum(rate(ais_messages_received_total[5m])) by (ais_message_type)
 / sum(rate(ais_messages_received_total[5m]))) * 100
```

#### Error Rate Trending
```promql
# Errors as percentage of total messages
(sum(rate(ais_errors_received_total[5m]))
 / sum(rate(ais_messages_received_total[5m]))) * 100

# By error type
sum(rate(ais_errors_received_total[5m])) by (error_type)
```

### Connection Health

#### Connection Uptime
```promql
# Time since last connection failure (in minutes)
(time() - max(ais_connection_failures_total)) / 60
```

#### Retry Rate
```promql
# Retries per minute
sum(rate(ais_connection_retry_attempts_total[1m]))
```

### Storage Performance

#### Storage Latency Percentiles
```promql
# P50, P95, P99
histogram_quantile(0.50, sum(rate(ais_storage_operation_duration_bucket[5m])) by (le))
histogram_quantile(0.95, sum(rate(ais_storage_operation_duration_bucket[5m])) by (le))
histogram_quantile(0.99, sum(rate(ais_storage_operation_duration_bucket[5m])) by (le))
```

#### Storage Throughput
```promql
# Operations per second
sum(rate(ais_storage_operations_total[1m])) by (operation)

# Bytes written per second (approximate: messages * avg_size)
sum(rate(ais_storage_operations_total{operation="append"}[1m])) * 150
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
   - `sum(rate(ais_messages_received_total[1m]))`
   - 15-minute window

3. **Error Rate** (Time Series)
   - `(sum(rate(ais_errors_received_total[1m])) / sum(rate(ais_messages_received_total[1m]))) * 100`
   - Alert threshold line at 1%

4. **Connection Health** (Status History)
   - `ais_connection_consecutive_failures`
   - Show disconnections as spikes

### Message Processing Dashboard

**Purpose**: Detailed message processing analysis

**Panels**:
1. **Message Type Distribution** (Pie Chart)
   - `sum(rate(ais_messages_received_total[5m])) by (ais_message_type)`

2. **Messages by Type Over Time** (Stacked Area)
   - Individual series for types 1-3, 5, 18-19, 24

3. **Parse Error Breakdown** (Bar Chart)
   - `sum(rate(ais_errors_received_total[5m])) by (error_type)`

4. **Top Vessels by Traffic** (Table)
   - Query traces for unique MMSIs (requires trace backend)

### Storage Performance Dashboard

**Purpose**: Storage operations and latency

**Panels**:
1. **Storage Latency Heatmap**
   - `ais_storage_operation_duration_bucket`
   - Show P50, P95, P99 lines

2. **Batch Processing** (Time Series)
   - `ais_storage_batches_pending` (gauge)
   - Batch completion rate

3. **Storage Operations** (Counter)
   - `sum(rate(ais_storage_operations_total[1m])) by (operation)`

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
expr: ais_connection_consecutive_failures > 5
for: 1m
severity: critical
summary: "AIS stream connection failed"
description: "{{ $value }} consecutive connection failures detected"
```

#### High Error Rate
```yaml
alert: AISHighErrorRate
expr: (sum(rate(ais_errors_received_total[5m])) / sum(rate(ais_messages_received_total[5m]))) * 100 > 1
for: 5m
severity: critical
summary: "High AIS message error rate"
description: "Error rate is {{ $value | printf \"%.2f\" }}% (threshold: 1%)"
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
alert: AISElevatedErrorRate
expr: (sum(rate(ais_errors_received_total[5m])) / sum(rate(ais_messages_received_total[5m]))) * 100 > 0.5
for: 10m
severity: warning
summary: "Elevated AIS error rate detected"
```

#### Slow Storage Operations
```yaml
alert: AISSlowStorage
expr: histogram_quantile(0.95, sum(rate(ais_storage_operation_duration_bucket[5m])) by (le)) > 1.0
for: 5m
severity: warning
summary: "Storage operations are slow"
description: "P95 latency is {{ $value | printf \"%.2f\" }}s (threshold: 1s)"
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
expr: ais_connection_consecutive_failures == 0 and ais_connection_consecutive_failures offset 1m > 0
for: 0m
severity: info
summary: "AIS connection restored"
```

## Troubleshooting Scenarios

### Scenario 1: No Messages Received

**Symptoms**:
- `ais_messages_received_total` not incrementing
- `ais_connection_consecutive_failures` increasing

**Diagnosis**:
1. Check connection status: `ais_connection_consecutive_failures`
2. View connection events: filter logs to event ids 4000-4005 (stream connect/retry/idle/error) and 4010-4024 (TCP-level detail, including the classified socket errors)
3. Check network connectivity to AIS host

**Queries**:
```promql
# Connection attempts in last hour
increase(ais_connection_attempts_total[1h])

# Current consecutive failures
ais_connection_consecutive_failures
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
- `ais_errors_received_total` increasing rapidly
- Error rate >1%

**Diagnosis**:
1. Check error type distribution:
   ```promql
   sum(rate(ais_errors_received_total[5m])) by (error_type)
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
- `ais_messages_queued` increasing

**Diagnosis**:
1. Check storage latency:
   ```promql
   histogram_quantile(0.95, rate(ais_storage_operation_duration_bucket[5m]))
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
1. Verify tag cardinality in backend UI
2. Check for unbounded tags (MMSI, vessel names, coordinates)

**Resolution**:
This should not occur if using the updated `ActivityExtensions.cs` that moves unbounded values to events. If it does:

1. Verify `ActivityExtensions.cs` uses events for:
   - MMSI (1 billion values)
   - Vessel names (unbounded)
   - Coordinates (infinite precision)
   - Timestamps

2. Update to latest version with cardinality fixes

## Performance Baselines

### Low Volume (<100 msg/sec)

**Expected Performance**:
- Message Rate: 50-100 msg/sec
- Error Rate: <0.1%
- Storage Latency P95: <200ms
- Memory: ~200-300MB working set
- CPU: <10% on single core

### Medium Volume (100-1000 msg/sec)

**Expected Performance**:
- Message Rate: 100-1000 msg/sec
- Error Rate: <0.1%
- Storage Latency P95: <500ms
- Memory: ~500-800MB working set
- CPU: 10-30% on single core

### High Volume (>1000 msg/sec)

**Expected Performance**:
- Message Rate: >1000 msg/sec
- Error Rate: <0.1%
- Storage Latency P95: <1s
- Memory: ~1-2GB working set
- CPU: 30-60% on multi-core

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
