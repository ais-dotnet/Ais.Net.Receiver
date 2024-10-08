// <copyright file="AisMessageBase.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Models
{
    using Ais.Net.Models.Abstractions;

    public record AisMessageBase(int MessageType, uint Mmsi, long? UnixTimestamp) : IAisMessage;
}