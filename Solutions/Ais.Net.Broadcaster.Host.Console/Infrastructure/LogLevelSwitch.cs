// <copyright file="LogLevelSwitch.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Microsoft.Extensions.Logging;

namespace Ais.Net.Broadcaster.Host.Console.Infrastructure;

/// <summary>
/// The minimum log level, settable after the container is built. Logging is configured before
/// Spectre.Console.Cli parses <c>--verbosity</c>, so the console provider filters through this
/// rather than a fixed minimum.
/// </summary>
public sealed class LogLevelSwitch
{
    private volatile int level = (int)LogLevel.Warning;

    /// <summary>Gets or sets the lowest level that is written. Defaults to <see cref="LogLevel.Warning"/>.</summary>
    public LogLevel MinimumLevel
    {
        get => (LogLevel)this.level;
        set => this.level = (int)value;
    }

    /// <summary>Returns whether a message at <paramref name="logLevel"/> should be written.</summary>
    /// <param name="logLevel">The level of the message.</param>
    /// <returns><see langword="true"/> if the message passes the filter.</returns>
    public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None && logLevel >= this.MinimumLevel;
}
