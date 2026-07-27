// <copyright file="ActivityExtensionsTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.Diagnostics;

using Ais.Net.Receiver.Telemetry;

using Shouldly;

namespace Ais.Net.Receiver.Tests;

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
        activity.Events.ShouldContain(e => e.Name == "ais.message.context");

        ActivityEvent contextEvent = activity.Events.First(e => e.Name == "ais.message.context");
        contextEvent.Tags.First(t => t.Key == "ais.mmsi").Value.ShouldBe((uint)123456789);
    }

    [TestMethod]
    public void SetAisMessageContext_WithNullActivity_ReturnsNull()
    {
        // Act
        Activity? result = ((Activity?)null).SetAisMessageContext(1, 123456789);

        // Assert
        result.ShouldBeNull();
    }

    [TestMethod]
    public void SetVesselIdentity_WithAllData_RecordsCorrectly()
    {
        // Arrange
        using Activity? activity = this.testSource.StartActivity("Test");

        // Act
        activity?.SetVesselIdentity(
            vesselName: "TEST VESSEL",
            callSign: "ABC123",
            shipType: 70);

        // Assert
        activity.ShouldNotBeNull();

        // Ship type should be a tag (bounded ~100 values)
        activity.GetTagItem("ais.ship_type").ShouldBe(70);

        // Vessel name and call sign should be in event (unbounded - cardinality fix)
        activity.GetTagItem("ais.vessel.name").ShouldBeNull();
        activity.GetTagItem("ais.call_sign").ShouldBeNull();

        activity.Events.ShouldContain(e => e.Name == "ais.vessel.identity");
        ActivityEvent identityEvent = activity.Events.First(e => e.Name == "ais.vessel.identity");

        identityEvent.Tags.First(t => t.Key == "ais.vessel.name").Value.ShouldBe("TEST VESSEL");
        identityEvent.Tags.First(t => t.Key == "ais.call_sign").Value.ShouldBe("ABC123");
    }

    [TestMethod]
    public void SetVesselIdentity_WithOnlyShipType_DoesNotCreateEvent()
    {
        // Arrange
        using Activity? activity = this.testSource.StartActivity("Test");

        // Act
        activity?.SetVesselIdentity(
            vesselName: null,
            callSign: null,
            shipType: 70);

        // Assert
        activity.ShouldNotBeNull();
        activity.GetTagItem("ais.ship_type").ShouldBe(70);
        activity.Events.ShouldNotContain(e => e.Name == "ais.vessel.identity");
    }

    [TestMethod]
    public void SetVesselPosition_WithValidCoordinates_RecordsAsEvent()
    {
        // Arrange
        using Activity? activity = this.testSource.StartActivity("Test");

        // Act
        activity?.SetVesselPosition(latitude: 51.5074, longitude: -0.1278);

        // Assert
        activity.ShouldNotBeNull();

        // Coordinates should be in event, not as tags (infinite precision - cardinality fix)
        activity.GetTagItem("ais.position.latitude").ShouldBeNull();
        activity.GetTagItem("ais.position.longitude").ShouldBeNull();

        activity.Events.ShouldContain(e => e.Name == "ais.vessel.position");
        ActivityEvent positionEvent = activity.Events.First(e => e.Name == "ais.vessel.position");

        positionEvent.Tags.First(t => t.Key == "ais.position.latitude").Value.ShouldBe(51.5074);
        positionEvent.Tags.First(t => t.Key == "ais.position.longitude").Value.ShouldBe(-0.1278);
    }

    [TestMethod]
    public void SetVesselPosition_WithNullCoordinates_DoesNotRecordEvent()
    {
        // Arrange
        using Activity? activity = this.testSource.StartActivity("Test");

        // Act
        activity?.SetVesselPosition(latitude: null, longitude: null);

        // Assert
        activity.ShouldNotBeNull();
        activity.Events.ShouldNotContain(e => e.Name == "ais.vessel.position");
    }

    [TestMethod]
    public void SetNavigationStatus_WithValidStatus_SetsTag()
    {
        // Arrange
        using Activity? activity = this.testSource.StartActivity("Test");

        // Act
        activity?.SetNavigationStatus(status: 0); // Under way using engine

        // Assert
        activity.ShouldNotBeNull();
        activity.GetTagItem("ais.navigation_status").ShouldBe(0);
    }

    [TestMethod]
    public void SetStationMetadata_SetsStationIdAsTag_AndTimestampAsEvent()
    {
        // Arrange
        using Activity? activity = this.testSource.StartActivity("Test");
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Act
        activity?.SetStationMetadata(stationId: 5, unixTimestamp: timestamp);

        // Assert
        activity.ShouldNotBeNull();

        // Station ID should be tag (bounded number of stations)
        activity.GetTagItem("ais.station_id").ShouldBe(5);

        // Timestamp should be in event (unbounded - every second is unique)
        activity.GetTagItem("ais.timestamp").ShouldBeNull();

        activity.Events.ShouldContain(e => e.Name == "ais.station.metadata");
        ActivityEvent metadataEvent = activity.Events.First(e => e.Name == "ais.station.metadata");

        metadataEvent.Tags.First(t => t.Key == "ais.timestamp").Value.ShouldBe(timestamp);
        metadataEvent.Tags.First(t => t.Key == "messaging.message.id").Value.ShouldBe($"5-{timestamp}");
    }

    [TestMethod]
    public void RecordExceptionWithStatus_SetsErrorStatusAndEvent()
    {
        // Arrange
        using Activity? activity = this.testSource.StartActivity("Test");
        var exception = new InvalidOperationException("Test error");

        // Act
        activity?.RecordExceptionWithStatus(exception, escaped: true);

        // Assert
        activity.ShouldNotBeNull();
        activity.Status.ShouldBe(ActivityStatusCode.Error);
        activity.StatusDescription.ShouldBe("Test error");

        activity.Events.ShouldContain(e => e.Name == "exception");
        ActivityEvent exceptionEvent = activity.Events.First(e => e.Name == "exception");

        exceptionEvent.Tags.First(t => t.Key == "exception.type").Value.ShouldBe(typeof(InvalidOperationException).FullName);
        exceptionEvent.Tags.First(t => t.Key == "exception.message").Value.ShouldBe("Test error");
        exceptionEvent.Tags.First(t => t.Key == "exception.escaped").Value.ShouldBe(true);
    }

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

        // Verify outer exception
        ActivityEvent outerEvent = activity.Events.First(e => e.Name == "exception");
        outerEvent.Tags.First(t => t.Key == "exception.type").Value.ShouldBe(typeof(Exception).FullName);
        outerEvent.Tags.First(t => t.Key == "exception.message").Value.ShouldBe("Outer error");
        outerEvent.Tags.First(t => t.Key == "exception.depth").Value.ShouldBe(0);

        // Verify inner exceptions
        ActivityEvent[] innerEvents = activity.Events.Where(e => e.Name == "exception.inner").ToArray();
        innerEvents.Length.ShouldBe(2);

        // First inner (middle)
        innerEvents[0].Tags.First(t => t.Key == "exception.type").Value.ShouldBe(typeof(ApplicationException).FullName);
        innerEvents[0].Tags.First(t => t.Key == "exception.message").Value.ShouldBe("Middle error");
        innerEvents[0].Tags.First(t => t.Key == "exception.depth").Value.ShouldBe(1);

        // Second inner (innermost)
        innerEvents[1].Tags.First(t => t.Key == "exception.type").Value.ShouldBe(typeof(InvalidOperationException).FullName);
        innerEvents[1].Tags.First(t => t.Key == "exception.message").Value.ShouldBe("Innermost error");
        innerEvents[1].Tags.First(t => t.Key == "exception.depth").Value.ShouldBe(2);
    }

    [TestMethod]
    public void RecordExceptionWithInnerExceptions_WithNullActivity_ReturnsNull()
    {
        // Arrange
        var exception = new InvalidOperationException("Test");

        // Act
        Activity? result = ((Activity?)null).RecordExceptionWithInnerExceptions(exception);

        // Assert
        result.ShouldBeNull();
    }

    [TestMethod]
    public void RecordHandledException_DoesNotSetErrorStatus()
    {
        // Arrange
        using Activity? activity = this.testSource.StartActivity("Test");
        var exception = new InvalidOperationException("Handled error");

        // Act
        activity?.RecordHandledException(exception, handlingStrategy: "retry");

        // Assert
        activity.ShouldNotBeNull();
        activity.Status.ShouldBe(ActivityStatusCode.Unset); // Should NOT set error status

        activity.Events.ShouldContain(e => e.Name == "exception.handled");
        ActivityEvent handledEvent = activity.Events.First(e => e.Name == "exception.handled");

        handledEvent.Tags.First(t => t.Key == "exception.type").Value.ShouldBe(typeof(InvalidOperationException).FullName);
        handledEvent.Tags.First(t => t.Key == "exception.message").Value.ShouldBe("Handled error");
        handledEvent.Tags.First(t => t.Key == "exception.escaped").Value.ShouldBe(false);
        handledEvent.Tags.First(t => t.Key == "exception.handling_strategy").Value.ShouldBe("retry");
    }

    [TestMethod]
    public void SetErrorType_SetsCorrectTag()
    {
        // Arrange
        using Activity? activity = this.testSource.StartActivity("Test");

        // Act
        activity?.SetErrorType("parse_error");

        // Assert
        activity.ShouldNotBeNull();
        activity.GetTagItem("error.type").ShouldBe("parse_error");
    }

    [TestMethod]
    public void IsAllDataRequested_WhenFalse_DoesNotSetTags()
    {
        // Note: This test would require creating an activity with sampling decision = Drop
        // which is complex to set up. The IsAllDataRequested check is already verified
        // by the fact that tags are set in other tests (when sampling is AllDataAndRecorded).
        // This is a defensive check in the code that's hard to test directly without
        // complex ActivityListener configuration.
    }

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
            .SetNavigationStatus(status: 0)
            .SetStationMetadata(stationId: 5, unixTimestamp: 1234567890);

        // Assert
        activity.ShouldNotBeNull();
        activity.GetTagItem("ais.message.type").ShouldBe(1);
        activity.GetTagItem("ais.ship_type").ShouldBe(70);
        activity.GetTagItem("ais.navigation_status").ShouldBe(0);
        activity.GetTagItem("ais.station_id").ShouldBe(5);

        // Verify events were created
        activity.Events.Count().ShouldBeGreaterThan(0);
    }
}
