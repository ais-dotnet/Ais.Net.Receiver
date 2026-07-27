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
    // ActivityListeners are process-global and this assembly runs tests in parallel at method scope,
    // so a listener keyed on a shared source name would observe activities started by every other
    // concurrent test in this class. Each test instance therefore gets its own uniquely named source
    // and matches on that exact instance, keeping the listeners disjoint.
    private string testSourceName = null!;
    private ActivitySource testSource = null!;
    private ActivityListener listener = null!;

    [TestInitialize]
    public void Setup()
    {
        this.testSourceName = $"TestActivitySource.{Guid.NewGuid():N}";
        this.testSource = new ActivitySource(this.testSourceName);

        this.listener = new ActivityListener
        {
            ShouldListenTo = source => ReferenceEquals(source, this.testSource),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
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

        // MMSI is a span attribute so a trace can be searched for a specific vessel. Span attributes
        // are per-span records rather than metric dimensions, so its cardinality costs nothing here.
        activity.GetTagItem("ais.mmsi").ShouldBe((uint)123456789);
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

        // Assert - all three are span attributes, so a trace is findable by name or call sign
        activity.ShouldNotBeNull();
        activity.GetTagItem("ais.ship_type").ShouldBe(70);
        activity.GetTagItem("ais.vessel.name").ShouldBe("TEST VESSEL");
        activity.GetTagItem("ais.call_sign").ShouldBe("ABC123");
    }

    [TestMethod]
    public void SetVesselIdentity_WithOnlyShipType_SetsOnlyShipType()
    {
        // Arrange
        using Activity? activity = this.testSource.StartActivity("Test");

        // Act
        activity?.SetVesselIdentity(
            vesselName: null,
            callSign: null,
            shipType: 70);

        // Assert - absent fields must not be written as empty attributes
        activity.ShouldNotBeNull();
        activity.GetTagItem("ais.ship_type").ShouldBe(70);
        activity.GetTagItem("ais.vessel.name").ShouldBeNull();
        activity.GetTagItem("ais.call_sign").ShouldBeNull();
    }

    [TestMethod]
    public void SetVesselPosition_WithValidCoordinates_SetsTags()
    {
        // Arrange
        using Activity? activity = this.testSource.StartActivity("Test");

        // Act
        activity?.SetVesselPosition(latitude: 51.5074, longitude: -0.1278);

        // Assert - span attributes, so traces can be filtered to a geographic area
        activity.ShouldNotBeNull();
        activity.GetTagItem("ais.position.latitude").ShouldBe(51.5074);
        activity.GetTagItem("ais.position.longitude").ShouldBe(-0.1278);
    }

    [TestMethod]
    public void SetVesselPosition_WithNullCoordinates_DoesNotSetTags()
    {
        // Arrange
        using Activity? activity = this.testSource.StartActivity("Test");

        // Act
        activity?.SetVesselPosition(latitude: null, longitude: null);

        // Assert
        activity.ShouldNotBeNull();
        activity.GetTagItem("ais.position.latitude").ShouldBeNull();
        activity.GetTagItem("ais.position.longitude").ShouldBeNull();
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
    public void SetStationMetadata_SetsStationTimestampAndMessageIdTags()
    {
        // Arrange
        using Activity? activity = this.testSource.StartActivity("Test");
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Act
        activity?.SetStationMetadata(stationId: 5, unixTimestamp: timestamp);

        // Assert - the message id in particular has to be searchable to correlate a span with the
        // source sentence, which is only true of a span attribute.
        activity.ShouldNotBeNull();
        activity.GetTagItem("ais.station_id").ShouldBe(5);
        activity.GetTagItem("ais.timestamp").ShouldBe(timestamp);
        activity.GetTagItem("messaging.message.id").ShouldBe($"5-{timestamp}");
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
        // Arrange: a source whose listener returns PropagationData, so the activity is created and
        // carries trace context but IsAllDataRequested is false. This is the guard every enrichment
        // method opens with, and it is the branch the rest of the suite never reaches - all other
        // tests sample AllDataAndRecorded.
        string unsampledSourceName = $"UnsampledActivitySource.{Guid.NewGuid():N}";
        using ActivitySource unsampledSource = new(unsampledSourceName);

        using ActivityListener unsampledListener = new()
        {
            ShouldListenTo = source => ReferenceEquals(source, unsampledSource),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.PropagationData,
        };

        ActivitySource.AddActivityListener(unsampledListener);

        using Activity? activity = unsampledSource.StartActivity("Unsampled");

        // Guard the premise: if this ever became true the assertions below would pass vacuously.
        activity.ShouldNotBeNull();
        activity.IsAllDataRequested.ShouldBeFalse();

        // Act
        activity
            .SetAisMessageContext(messageType: 1, mmsi: 123456789)
            .SetVesselIdentity(vesselName: "TEST", callSign: "ABC", shipType: 70)
            .SetVesselPosition(latitude: 51.5, longitude: -0.1)
            .SetNavigationStatus(status: 0)
            .SetStationMetadata(stationId: 5, unixTimestamp: 1234567890);

        // Assert: nothing was recorded on an unsampled span.
        activity.GetTagItem("ais.message.type").ShouldBeNull();
        activity.GetTagItem("ais.mmsi").ShouldBeNull();
        activity.GetTagItem("ais.vessel.name").ShouldBeNull();
        activity.GetTagItem("ais.call_sign").ShouldBeNull();
        activity.GetTagItem("ais.ship_type").ShouldBeNull();
        activity.GetTagItem("ais.position.latitude").ShouldBeNull();
        activity.GetTagItem("ais.navigation_status").ShouldBeNull();
        activity.GetTagItem("ais.station_id").ShouldBeNull();
        activity.Events.ShouldBeEmpty();
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

        // Assert - every enrichment in the chain landed on the same span
        activity.ShouldNotBeNull();
        activity.GetTagItem("ais.message.type").ShouldBe(1);
        activity.GetTagItem("ais.mmsi").ShouldBe((uint)123456789);
        activity.GetTagItem("ais.vessel.name").ShouldBe("TEST");
        activity.GetTagItem("ais.call_sign").ShouldBe("ABC");
        activity.GetTagItem("ais.ship_type").ShouldBe(70);
        activity.GetTagItem("ais.position.latitude").ShouldBe(51.5);
        activity.GetTagItem("ais.position.longitude").ShouldBe(-0.1);
        activity.GetTagItem("ais.navigation_status").ShouldBe(0);
        activity.GetTagItem("ais.station_id").ShouldBe(5);
        activity.GetTagItem("messaging.message.id").ShouldBe("5-1234567890");
    }
}
