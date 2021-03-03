// <copyright file="AisMessageBase.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Domain
{
    public record AisMessageBase(int MessageType, uint Mmsi) : IVesselIdentity, IAisMessageType;
}