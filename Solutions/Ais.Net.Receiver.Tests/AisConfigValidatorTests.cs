// <copyright file="AisConfigValidatorTests.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Receiver.Configuration;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Shouldly;

namespace Ais.Net.Receiver.Tests;

[TestClass]
public class AisConfigValidatorTests
{
    private readonly AisConfigValidator validator = new();

    [TestMethod]
    public void Validate_TcpWithHostAndPort_Succeeds()
    {
        AisConfig config = ValidTcp();

        this.validator.Validate(null, config).Succeeded.ShouldBeTrue();
    }

    [TestMethod]
    public void Validate_TcpWithoutHost_FailsNamingTheMember()
    {
        AisConfig config = ValidTcp();
        config.Connection.Host = string.Empty;

        ValidateOptionsResult result = this.validator.Validate(null, config);

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(failure => failure.Contains("Ais:Connection:Host", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Validate_TcpWithPortOutOfRange_FailsNamingTheMember()
    {
        AisConfig config = ValidTcp();
        config.Connection.Port = 0;

        ValidateOptionsResult result = this.validator.Validate(null, config);

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(failure => failure.Contains("Ais:Connection:Port", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Validate_WebSocketWithValidUrl_Succeeds()
    {
        AisConfig config = ValidTcp();
        config.Connection.Transport = AisTransport.WebSocket;
        config.Connection.Url = "wss://example.net/ais";
        config.Connection.Host = string.Empty;
        config.Connection.Port = 0;

        this.validator.Validate(null, config).Succeeded.ShouldBeTrue();
    }

    [TestMethod]
    public void Validate_WebSocketWithHttpUrl_FailsNamingTheMember()
    {
        AisConfig config = ValidTcp();
        config.Connection.Transport = AisTransport.WebSocket;
        config.Connection.Url = "http://example.net/ais";

        ValidateOptionsResult result = this.validator.Validate(null, config);

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(failure => failure.Contains("Ais:Connection:Url", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Validate_RetryAttemptsBelowOne_FailsNamingTheSection()
    {
        AisConfig config = ValidTcp();
        config.Receiver.Retry.Attempts = 0;

        ValidateOptionsResult result = this.validator.Validate(null, config);

        result.Failed.ShouldBeTrue();
        result.Failures.ShouldContain(failure => failure.Contains("Ais:Receiver:Retry:Attempts", StringComparison.Ordinal));
    }

    [TestMethod]
    public void Validate_ReportsEveryFailureAtOnce()
    {
        AisConfig config = ValidTcp();
        config.Connection.Host = string.Empty;
        config.Connection.Retry.Attempts = 0;

        ValidateOptionsResult result = this.validator.Validate(null, config);

        result.Failed.ShouldBeTrue();
        result.Failures.Count().ShouldBe(2);
    }

    [TestMethod]
    public void Validate_ThroughOptionsPipeline_FailsAtResolution()
    {
        // The way the hosts wire it: bound from configuration, data annotations plus this validator.
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ais:Connection:Transport"] = "WebSocket",
                ["Ais:Connection:Url"] = "not a url",
                ["Ais:Connection:Retry:Attempts"] = "5",
                ["Ais:Receiver:Retry:Attempts"] = "5",
            })
            .Build();

        ServiceCollection services = new();
        services.AddOptions<AisConfig>()
            .Bind(configuration.GetSection("Ais"))
            .ValidateDataAnnotations();
        services.AddSingleton<IValidateOptions<AisConfig>, AisConfigValidator>();

        using ServiceProvider provider = services.BuildServiceProvider();
        IOptions<AisConfig> options = provider.GetRequiredService<IOptions<AisConfig>>();

        OptionsValidationException exception = Should.Throw<OptionsValidationException>(() => options.Value);
        exception.Failures.ShouldContain(failure => failure.Contains("Ais:Connection:Url", StringComparison.Ordinal));
    }

    private static AisConfig ValidTcp() => new()
    {
        Connection = new AisConnectionConfig
        {
            Transport = AisTransport.Tcp,
            Host = "153.44.253.27",
            Port = 5631,
            Retry = new AisRetryConfig { Attempts = 5, Periodicity = TimeSpan.FromSeconds(1) },
        },
        Receiver = new AisReceiverConfig
        {
            Retry = new AisRetryConfig { Attempts = 5, Periodicity = TimeSpan.FromSeconds(1) },
        },
    };
}
