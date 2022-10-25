// <copyright file="LoggerVerbosity.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

// Configuration binding types are typically better off as null-oblivious, because the contents
// of config files are outside the compiler's control.
#nullable disable annotations
namespace Ais.Net.Receiver.Configuration;

public enum LoggerVerbosity
{
    Quiet = 0,
    Minimal = 1,
    Normal = 2,
    Detailed = 3,
    Diagnostic = 4
}