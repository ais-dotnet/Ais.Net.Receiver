// <copyright file="ConfigurationTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using System.ComponentModel.DataAnnotations;

using Ais.Net.Receiver.Configuration;

using Microsoft.Extensions.Logging;

using Shouldly;

namespace Ais.Net.Receiver.Tests;

[TestClass]
public class ConfigurationTests
{
    [TestMethod]
    public void AisConfig_DefaultValues_AreCorrectlyInitialized()
    {
        // Arrange & Act
        AisConfig config = new();

        // Assert
        config.Connection.ShouldNotBeNull();
        config.Receiver.ShouldNotBeNull();
        config.Telemetry.ShouldNotBeNull();
    }

    [TestMethod]
    public void AisConfig_PropertySetters_WorkCorrectly()
    {
        // Arrange
        AisConfig config = new();
        AisConnectionConfig connection = new() { Host = "test.com", Port = 1234 };
        AisReceiverConfig receiver = new();
        AisTelemetryConfig telemetry = new() { Verbosity = LogLevel.Debug };

        // Act
        config.Connection = connection;
        config.Receiver = receiver;
        config.Telemetry = telemetry;

        // Assert
        config.Connection.Host.ShouldBe("test.com");
        config.Connection.Port.ShouldBe(1234);
        config.Telemetry.Verbosity.ShouldBe(LogLevel.Debug);
    }

    [TestMethod]
    public void AisConnectionConfig_DefaultValues_AreCorrectlyInitialized()
    {
        // Arrange & Act
        AisConnectionConfig config = new();

        // Assert
        config.Host.ShouldBe(string.Empty);
        config.Port.ShouldBe(0);
        config.Retry.ShouldNotBeNull();
    }

    [TestMethod]
    public void AisConnectionConfig_PropertySetters_WorkCorrectly()
    {
        // Arrange & Act
        AisConnectionConfig config = new()
        {
            Host = "ais.example.com",
            Port = 5631,
            Retry = new AisRetryConfig { Attempts = 5, Periodicity = TimeSpan.FromSeconds(10) }
        };

        // Assert
        config.Host.ShouldBe("ais.example.com");
        config.Port.ShouldBe(5631);
        config.Retry.Attempts.ShouldBe(5);
        config.Retry.Periodicity.ShouldBe(TimeSpan.FromSeconds(10));
    }

    [TestMethod]
    public void AisConnectionConfig_Validation_RequiresHost()
    {
        // Arrange
        AisConnectionConfig config = new() { Host = string.Empty, Port = 1234 };

        // Act
        List<ValidationResult> results = [];
        bool isValid = Validator.TryValidateObject(config, new ValidationContext(config), results, validateAllProperties: true);

        // Assert
        isValid.ShouldBeFalse();
        results.ShouldContain(r => r.MemberNames.Contains("Host"));
    }

    [TestMethod]
    public void AisConnectionConfig_Validation_PortMustBeInRange()
    {
        // Arrange - port 0 is invalid (range is 1-65535)
        AisConnectionConfig config = new() { Host = "valid.host.com", Port = 0 };

        // Act
        List<ValidationResult> results = [];
        bool isValid = Validator.TryValidateObject(config, new ValidationContext(config), results, validateAllProperties: true);

        // Assert
        isValid.ShouldBeFalse();
        results.ShouldContain(r => r.MemberNames.Contains("Port"));
    }

    [TestMethod]
    public void AisConnectionConfig_Validation_ValidConfiguration_Passes()
    {
        // Arrange
        AisConnectionConfig config = new() { Host = "valid.host.com", Port = 5631 };

        // Act
        List<ValidationResult> results = [];
        bool isValid = Validator.TryValidateObject(config, new ValidationContext(config), results, validateAllProperties: true);

        // Assert
        isValid.ShouldBeTrue();
        results.ShouldBeEmpty();
    }

    [TestMethod]
    public void AisReceiverConfig_DefaultValues_AreCorrectlyInitialized()
    {
        // Arrange & Act
        AisReceiverConfig config = new();

        // Assert
        config.Retry.ShouldNotBeNull();
    }

    [TestMethod]
    public void AisReceiverConfig_PropertySetters_WorkCorrectly()
    {
        // Arrange & Act
        AisReceiverConfig config = new()
        {
            Retry = new AisRetryConfig { Attempts = 10, Periodicity = TimeSpan.FromSeconds(30) }
        };

        // Assert
        config.Retry.Attempts.ShouldBe(10);
        config.Retry.Periodicity.ShouldBe(TimeSpan.FromSeconds(30));
    }

    [TestMethod]
    public void AisRetryConfig_DefaultValues_AreCorrectlyInitialized()
    {
        // Arrange & Act
        AisRetryConfig config = new();

        // Assert
        config.Attempts.ShouldBe(0);
        config.Periodicity.ShouldBe(TimeSpan.Zero);
    }

    [TestMethod]
    public void AisRetryConfig_PropertySetters_WorkCorrectly()
    {
        // Arrange & Act
        AisRetryConfig config = new()
        {
            Attempts = 50,
            Periodicity = TimeSpan.FromMinutes(1)
        };

        // Assert
        config.Attempts.ShouldBe(50);
        config.Periodicity.ShouldBe(TimeSpan.FromMinutes(1));
    }

    [TestMethod]
    public void AisRetryConfig_Validation_AttemptsMustBePositive()
    {
        // Arrange - attempts 0 is invalid (range starts at 1)
        AisRetryConfig config = new() { Attempts = 0, Periodicity = TimeSpan.FromSeconds(5) };

        // Act
        List<ValidationResult> results = [];
        bool isValid = Validator.TryValidateObject(config, new ValidationContext(config), results, validateAllProperties: true);

        // Assert
        isValid.ShouldBeFalse();
        results.ShouldContain(r => r.MemberNames.Contains("Attempts"));
    }

    [TestMethod]
    public void AisRetryConfig_Validation_ValidConfiguration_Passes()
    {
        // Arrange
        AisRetryConfig config = new() { Attempts = 10, Periodicity = TimeSpan.FromSeconds(5) };

        // Act
        List<ValidationResult> results = [];
        bool isValid = Validator.TryValidateObject(config, new ValidationContext(config), results, validateAllProperties: true);

        // Assert
        isValid.ShouldBeTrue();
        results.ShouldBeEmpty();
    }

    [TestMethod]
    public void AisTelemetryConfig_DefaultValues_AreCorrectlyInitialized()
    {
        // Arrange & Act
        AisTelemetryConfig config = new();

        // Assert
        config.Verbosity.ShouldBe(LogLevel.None);
        config.StatisticsPeriodicity.ShouldBe(TimeSpan.Zero);
        config.VesselInactivityTimeout.ShouldBeNull();
    }

    [TestMethod]
    public void AisTelemetryConfig_PropertySetters_WorkCorrectly()
    {
        // Arrange & Act
        AisTelemetryConfig config = new()
        {
            Verbosity = LogLevel.Information,
            StatisticsPeriodicity = TimeSpan.FromMinutes(5),
            VesselInactivityTimeout = TimeSpan.FromHours(1)
        };

        // Assert
        config.Verbosity.ShouldBe(LogLevel.Information);
        config.StatisticsPeriodicity.ShouldBe(TimeSpan.FromMinutes(5));
        config.VesselInactivityTimeout.ShouldBe(TimeSpan.FromHours(1));
    }

    [TestMethod]
    public void AisTelemetryConfig_AllLogLevels_CanBeSet()
    {
        // Arrange & Act & Assert - verify all log levels can be set
        foreach (LogLevel level in Enum.GetValues<LogLevel>())
        {
            AisTelemetryConfig config = new() { Verbosity = level };
            config.Verbosity.ShouldBe(level);
        }
    }
}
