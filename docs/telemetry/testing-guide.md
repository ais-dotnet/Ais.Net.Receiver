# Telemetry Testing Guide

This guide explains how to test OpenTelemetry instrumentation in the Ais.Net.Receiver codebase, including activities (traces), metrics, and logs.

## Table of Contents

- [Overview](#overview)
- [Testing Activities (Traces)](#testing-activities-traces)
- [Testing Metrics](#testing-metrics)
- [Testing Log-Trace Correlation](#testing-log-trace-correlation)
- [Best Practices](#best-practices)
- [Common Patterns](#common-patterns)
- [Troubleshooting](#troubleshooting)

## Overview

Testing telemetry is critical for ensuring observability remains reliable as the codebase evolves. The Ais.Net.Receiver project uses:

- **Activities (Traces)**: Distributed tracing with OpenTelemetry semantic conventions
- **Metrics**: Counters, histograms, and gauges for operational visibility
- **Logs**: Structured logging with automatic trace correlation

All telemetry components should be tested to prevent regressions and ensure compliance with OpenTelemetry standards.

## Testing Activities (Traces)

### Setting Up Activity Testing

Activity testing requires configuring an `ActivityListener` to capture activities created by your code:

```csharp
[TestClass]
public class ActivityExtensionsTests
{
    private const string TestSourceName = "TestActivitySource";
    private ActivitySource testSource = null!;
    private ActivityListener listener = null!;
    private List<Activity> capturedActivities = null!;

    [TestInitialize]
    public void Setup()
    {
        this.testSource = new ActivitySource(TestSourceName);
        this.capturedActivities = [];

        this.listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == TestSourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => this.capturedActivities.Add(activity),
        };

        ActivitySource.AddActivityListener(this.listener);
    }

    [TestCleanup]
    public void Cleanup()
    {
        this.listener?.Dispose();
        this.testSource?.Dispose();
    }
}
```

### Testing Activity Tags

Tags are indexed dimensions used for querying and filtering. Test that:
1. Only bounded values (<1000 unique combinations) are used as tags
2. Tags follow OpenTelemetry semantic conventions
3. Tags are set correctly based on input

```csharp
[TestMethod]
public void SetAisMessageContext_WithValidData_SetsExpectedTags()
{
    // Arrange
    using Activity? activity = this.testSource.StartActivity("Test");

    // Act
    activity?.SetAisMessageContext(messageType: 1, mmsi: 123456789);

    // Assert
    activity.ShouldNotBeNull();
    activity.GetTagItem("ais.message.type").ShouldBe(1);
    activity.GetTagItem("messaging.system").ShouldBe("ais");

    // Verify MMSI is in event, not as tag (cardinality fix)
    activity.GetTagItem("ais.mmsi").ShouldBeNull();
}
```

### Testing Activity Events

Events provide non-indexed context data. Test that:
1. Unbounded values are recorded as events, not tags
2. Event names follow conventions
3. Event data is accurate

```csharp
[TestMethod]
public void SetAisMessageContext_RecordsMMSIAsEvent()
{
    // Arrange
    using Activity? activity = this.testSource.StartActivity("Test");

    // Act
    activity?.SetAisMessageContext(messageType: 1, mmsi: 123456789);

    // Assert
    activity.ShouldNotBeNull();
    activity.Events.ShouldContain(e => e.Name == "ais.message.context");

    ActivityEvent contextEvent = activity.Events.First(e => e.Name == "ais.message.context");
    contextEvent.Tags.First(t => t.Key == "ais.mmsi").Value.ShouldBe((uint)123456789);
}
```

### Testing Exception Recording

Ensure exceptions are recorded with full context:

```csharp
[TestMethod]
public void RecordExceptionWithInnerExceptions_RecordsAllLevels()
{
    // Arrange
    using Activity? activity = this.testSource.StartActivity("Test");

    var innermost = new InvalidOperationException("Innermost error");
    var middle = new ApplicationException("Middle error", innermost);
    var outer = new Exception("Outer error", middle);

    // Act
    activity?.RecordExceptionWithInnerExceptions(outer, escaped: true);

    // Assert
    activity.ShouldNotBeNull();
    activity.Status.ShouldBe(ActivityStatusCode.Error);
    activity.StatusDescription.ShouldBe("Outer error");

    // Should have 3 exception events: outer + 2 inner
    activity.Events.Count(e => e.Name == "exception" || e.Name == "exception.inner").ShouldBe(3);

    // Verify depth tracking
    ActivityEvent outerEvent = activity.Events.First(e => e.Name == "exception");
    outerEvent.Tags.First(t => t.Key == "exception.depth").Value.ShouldBe(0);
}
```

### Testing Null Safety

Always test null handling:

```csharp
[TestMethod]
public void SetAisMessageContext_WithNullActivity_ReturnsNull()
{
    // Act
    Activity? result = ((Activity?)null).SetAisMessageContext(1, 123456789);

    // Assert
    result.ShouldBeNull();
}
```

### Testing Fluent API Method Chaining

Verify extension methods support chaining:

```csharp
[TestMethod]
public void MethodChaining_AllowsFluentAPI()
{
    // Arrange
    using Activity? activity = this.testSource.StartActivity("Test");

    // Act - chain multiple enrichment calls
    activity?
        .SetAisMessageContext(messageType: 1, mmsi: 123456789)
        .SetVesselIdentity(vesselName: "TEST", callSign: "ABC", shipType: 70)
        .SetVesselPosition(latitude: 51.5, longitude: -0.1)
        .SetNavigationStatus(status: 0);

    // Assert
    activity.ShouldNotBeNull();
    activity.GetTagItem("ais.message.type").ShouldBe(1);
}
```

## Testing Metrics

### Setting Up Metric Testing

Use `MeterListener` to capture metric measurements:

```csharp
[TestClass]
public class ApplicationMetricsTests
{
    private MeterListener listener = null!;
    private ApplicationMetrics metrics = null!;
    private Dictionary<string, List<Measurement<long>>> longMeasurements = null!;
    private Dictionary<string, List<Measurement<double>>> doubleMeasurements = null!;

    [TestInitialize]
    public void Setup()
    {
        this.longMeasurements = new Dictionary<string, List<Measurement<long>>>();
        this.doubleMeasurements = new Dictionary<string, List<Measurement<double>>>();

        this.listener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name == ApplicationMetrics.MeterName)
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };

        this.listener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            if (!this.longMeasurements.ContainsKey(instrument.Name))
            {
                this.longMeasurements[instrument.Name] = new List<Measurement<long>>();
            }
            this.longMeasurements[instrument.Name].Add(
                new Measurement<long>(measurement, tags));
        });

        this.listener.Start();
        this.metrics = new ApplicationMetrics(new FakeMeterFactory());
    }

    [TestCleanup]
    public void Cleanup()
    {
        this.listener?.Dispose();
        this.metrics?.Dispose();
    }
}
```

### Testing Counter Metrics

```csharp
[TestMethod]
public void MessagesReceived_IncrementsCounter()
{
    // Arrange
    var tags = new TagList { { "ais.message_type", 1 } };

    // Act
    this.metrics.MessagesReceived.Add(1, tags);
    this.metrics.MessagesReceived.Add(1, tags);

    // Assert
    this.longMeasurements["ais.messages.received"].Count.ShouldBe(2);
    this.longMeasurements["ais.messages.received"].Sum(m => m.Value).ShouldBe(2);
}
```

### Testing Gauge Metrics

```csharp
[TestMethod]
public void ConsecutiveConnectionFailures_TracksCurrentValue()
{
    // Act
    this.metrics.ConsecutiveConnectionFailures.Add(1);
    this.metrics.ConsecutiveConnectionFailures.Add(1);
    this.metrics.ConsecutiveConnectionFailures.Add(-2); // Reset

    // Assert
    long currentValue = this.longMeasurements["ais.connection.consecutive_failures"].Last().Value;
    currentValue.ShouldBe(0);
}
```

### Testing Tag Cardinality

Ensure metrics don't create excessive tag combinations:

```csharp
[TestMethod]
public void MessagesReceived_BoundedCardinality()
{
    // Act - Record all AIS message types (1-27)
    for (int i = 1; i <= 27; i++)
    {
        var tags = new TagList { { "ais.message_type", i } };
        this.metrics.MessagesReceived.Add(1, tags);
    }

    // Assert - Should have exactly 27 unique tag combinations
    var uniqueTags = this.longMeasurements["ais.messages.received"]
        .Select(m => m.Tags.First().Value)
        .Distinct()
        .Count();

    uniqueTags.ShouldBe(27); // Bounded cardinality is maintained
}
```

## Testing Log-Trace Correlation

### Verifying Automatic Correlation

Test that logs automatically include trace context:

```csharp
[TestMethod]
public void Logging_AutomaticallyIncludesTraceContext()
{
    // Arrange
    var logEntries = new List<LogEntry>();
    using var loggerFactory = LoggerFactory.Create(builder =>
    {
        builder.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });
        builder.Configure(options =>
        {
            options.ActivityTrackingOptions =
                ActivityTrackingOptions.SpanId |
                ActivityTrackingOptions.TraceId;
        });
    });

    var logger = loggerFactory.CreateLogger<MyClass>();

    // Act
    using (var activity = new ActivitySource("Test").StartActivity("TestActivity"))
    {
        logger.LogInformation("Test message");
    }

    // Assert
    // Verify log entry includes SpanId and TraceId from activity
}
```

## Best Practices

### 1. Test All Public Telemetry Methods

Every public method that creates or enriches telemetry should have tests:

```csharp
// ✅ Good - comprehensive coverage
[TestMethod] public void SetAisMessageContext_WithValidData_SetsExpectedTags() { }
[TestMethod] public void SetAisMessageContext_WithNullActivity_ReturnsNull() { }
[TestMethod] public void SetAisMessageContext_WithInvalidData_HandlesGracefully() { }

// ❌ Bad - insufficient coverage
[TestMethod] public void SetAisMessageContext_Works() { } // Too vague
```

### 2. Verify Semantic Conventions

Ensure tag and event names follow OpenTelemetry conventions:

```csharp
// ✅ Good - validates semantic conventions
activity.GetTagItem("server.address").ShouldBe("localhost");
activity.GetTagItem("server.port").ShouldBe(5631);
activity.GetTagItem("network.transport").ShouldBe("tcp");

// ❌ Bad - non-standard naming
activity.GetTagItem("host").ShouldBe("localhost"); // Wrong convention
```

### 3. Test Cardinality Constraints

Validate that unbounded values aren't used as tags:

```csharp
[TestMethod]
public void VesselPosition_DoesNotCreateTags()
{
    using Activity? activity = this.testSource.StartActivity("Test");
    activity?.SetVesselPosition(latitude: 51.5074, longitude: -0.1278);

    // Assert - coordinates should NOT be tags (infinite cardinality)
    activity.GetTagItem("ais.position.latitude").ShouldBeNull();
    activity.GetTagItem("ais.position.longitude").ShouldBeNull();

    // Assert - coordinates should be in events instead
    activity.Events.ShouldContain(e => e.Name == "ais.vessel.position");
}
```

### 4. Use Descriptive Test Names

Follow the pattern: `MethodName_Scenario_ExpectedBehavior`

```csharp
// ✅ Good
[TestMethod] public void RecordException_WithInnerExceptions_RecordsAllLevels() { }

// ❌ Bad
[TestMethod] public void TestException() { }
```

### 5. Test IsAllDataRequested Guards

Ensure performance guards work correctly:

```csharp
[TestMethod]
public void ActivityEnrichment_WhenNotRequested_SkipsExpensiveOperations()
{
    // Create activity with sampling decision = Drop
    using var listener = new ActivityListener
    {
        ShouldListenTo = _ => true,
        Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
            ActivitySamplingResult.None // Not recording
    };
    ActivitySource.AddActivityListener(listener);

    using Activity? activity = new ActivitySource("Test").StartActivity("Test");

    // Act
    activity?.SetVesselPosition(51.5, -0.1);

    // Assert - should not have created event when not requested
    activity?.Events.Count().ShouldBe(0);
}
```

## Common Patterns

### Pattern 1: Testing Activity Lifecycle

```csharp
[TestMethod]
public void Activity_CompleteLifecycle_WorksCorrectly()
{
    // Arrange
    using Activity? activity = this.testSource.StartActivity("ProcessMessage");

    // Act - simulate complete processing lifecycle
    activity?.SetAisMessageContext(messageType: 1, mmsi: 123456789);
    activity?.SetVesselPosition(latitude: 51.5, longitude: -0.1);
    activity?.SetStatus(ActivityStatusCode.Ok, "Processing complete");
    activity?.Stop();

    // Assert
    activity.ShouldNotBeNull();
    activity.Status.ShouldBe(ActivityStatusCode.Ok);
    activity.Duration.ShouldBeGreaterThan(TimeSpan.Zero);
}
```

### Pattern 2: Testing Error Scenarios

```csharp
[TestMethod]
public void Activity_WhenExceptionOccurs_RecordsCorrectly()
{
    // Arrange
    using Activity? activity = this.testSource.StartActivity("ProcessMessage");

    try
    {
        // Act - simulate error
        throw new InvalidOperationException("Simulated error");
    }
    catch (Exception ex)
    {
        activity?.RecordExceptionWithStatus(ex, escaped: false);
    }

    // Assert
    activity.ShouldNotBeNull();
    activity.Status.ShouldBe(ActivityStatusCode.Error);
    activity.Events.ShouldContain(e => e.Name == "exception");
}
```

### Pattern 3: Testing Business Events

```csharp
[TestMethod]
public void RecordVesselDetected_CreatesBusinessEvent()
{
    // Arrange
    using Activity? activity = this.testSource.StartActivity("ProcessMessage");

    // Act
    activity?.RecordVesselDetected(mmsi: 123456789, vesselName: "TEST VESSEL");

    // Assert
    activity.ShouldNotBeNull();
    activity.Events.ShouldContain(e => e.Name == "vessel.detected");

    var vesselEvent = activity.Events.First(e => e.Name == "vessel.detected");
    vesselEvent.Tags.First(t => t.Key == "ais.mmsi").Value.ShouldBe((uint)123456789);
    vesselEvent.Tags.First(t => t.Key == "ais.vessel.name").Value.ShouldBe("TEST VESSEL");
}
```

## Troubleshooting

### Activities Not Being Captured

**Problem**: `ActivityListener` doesn't capture activities.

**Solution**: Ensure sampling is configured correctly:

```csharp
listener.Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
    ActivitySamplingResult.AllDataAndRecorded; // Not AllData or PropagationData
```

### Tags Not Appearing

**Problem**: Tags set on activity don't appear in tests.

**Solution**: Check `IsAllDataRequested`:

```csharp
// Only set tags when data is requested
if (activity is not null && activity.IsAllDataRequested)
{
    activity.SetTag("my.tag", value);
}
```

### Metrics Not Recording

**Problem**: `MeterListener` doesn't receive measurements.

**Solution**: Ensure listener is started before creating metrics:

```csharp
this.listener.Start(); // Must be called before creating metrics
this.metrics = new ApplicationMetrics(meterFactory);
```

### Test Isolation Issues

**Problem**: Tests interfere with each other.

**Solution**: Always dispose listeners and sources in `[TestCleanup]`:

```csharp
[TestCleanup]
public void Cleanup()
{
    this.listener?.Dispose();
    this.testSource?.Dispose();
    this.metrics?.Dispose();
}
```

## Additional Resources

- [OpenTelemetry .NET SDK Documentation](https://opentelemetry.io/docs/languages/net/)
- [Semantic Conventions v1.38.0](https://opentelemetry.io/docs/specs/semconv/)
- [ActivityExtensionsTests.cs](../../Solutions/Ais.Net.Receiver.Tests/ActivityExtensionsTests.cs) - Reference implementation
- [ApplicationMetricsTests.cs](../../Solutions/Ais.Net.Receiver.Tests/ApplicationMetricsTests.cs) - Metrics testing examples
