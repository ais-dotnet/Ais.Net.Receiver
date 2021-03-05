// <copyright file="AisMessageBase.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Ais.Net.Models
{
    using Ais.Net.Models.Abstractions;

    public record AisMessageBase(int MessageType, uint Mmsi) : IAisMessage;
}