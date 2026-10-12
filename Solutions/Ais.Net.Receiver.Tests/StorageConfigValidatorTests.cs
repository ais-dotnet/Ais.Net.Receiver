using Ais.Net.Receiver.Storage.Azure.Blob.Configuration;

using Shouldly;

namespace Ais.Net.Receiver.Tests;

[TestClass]
public class StorageConfigValidatorTests
{
    private readonly StorageConfigValidator validator = new();

    [TestMethod]
    public void Validate_CaptureDisabled_SucceedsWithEmptyConnectionString()
    {
        // The receiver can run for live display only, with no storage configured.
        StorageConfig config = new() { EnableCapture = false, ConnectionString = string.Empty, ContainerName = string.Empty };

        this.validator.Validate(null, config).Succeeded.ShouldBeTrue();
    }

    [TestMethod]
    public void Validate_CaptureEnabledWithEmptyConnectionString_Fails()
    {
        StorageConfig config = new() { EnableCapture = true, ConnectionString = string.Empty, ContainerName = "nmea" };

        this.validator.Validate(null, config).Failed.ShouldBeTrue();
    }

    [TestMethod]
    public void Validate_CaptureEnabledWithEmptyContainerName_Fails()
    {
        StorageConfig config = new() { EnableCapture = true, ConnectionString = "UseDevelopmentStorage=true", ContainerName = string.Empty };

        this.validator.Validate(null, config).Failed.ShouldBeTrue();
    }

    [TestMethod]
    public void Validate_CaptureEnabledFullyConfigured_Succeeds()
    {
        StorageConfig config = new() { EnableCapture = true, ConnectionString = "UseDevelopmentStorage=true", ContainerName = "nmea" };

        this.validator.Validate(null, config).Succeeded.ShouldBeTrue();
    }
}
