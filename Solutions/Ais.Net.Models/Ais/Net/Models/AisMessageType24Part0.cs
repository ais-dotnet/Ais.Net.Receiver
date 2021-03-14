// <copyright file="AisMessageType24Part0.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Models
{
    using Ais.Net.Models.Abstractions;

    public record AisMessageType24Part0(
        uint Mmsi,
        uint PartNumber,
        uint RepeatIndicator,
        uint Spare160) :
            AisMessageBase(MessageType: 24, Mmsi),
            IAisMultipartMessage,
            IRepeatIndicator,
            IAisMessageType24Part0;
}