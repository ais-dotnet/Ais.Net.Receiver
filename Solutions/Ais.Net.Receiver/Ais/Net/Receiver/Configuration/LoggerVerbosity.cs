// <copyright file="LoggerVerbosity.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

// Configuration binding types are typically better off as null-oblivious, because the contents
// of config files are outside the compiler's control.
#nullable disable annotations
namespace Ais.Net.Receiver.Configuration;

/// <summary>
/// Defines the verbosity of the console output.
/// </summary>
public enum LoggerVerbosity
{
    /// <summary>
    /// Essential only.
    /// </summary>
    Quiet = 0,

    /// <summary>
    /// Statistics only.
    /// </summary>
    Minimal = 1,

    /// <summary>
    /// Vessel Names and Positions.
    /// </summary>
    Normal = 2,

    /// <summary>
    /// NMEA Sentences.
    /// </summary>
    Detailed = 3,

    /// <summary>
    /// Messages and Errors.
    /// </summary>
    Diagnostic = 4
}