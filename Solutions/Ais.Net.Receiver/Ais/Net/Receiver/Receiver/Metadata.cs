// <copyright file="Metadata.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Models.Abstractions;

namespace Ais.Net.Receiver.Receiver;

public readonly record struct Metadata(int StationId, long UnixTimestamp, IAisMessage Message);