// <copyright file="IVesselDimensions.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Ais.Net.Receiver.Domain
{
    public interface IVesselDimensions
    {
        uint DimensionToBow { get; }
        uint DimensionToPort { get; }
        uint DimensionToStarboard { get; }
        uint DimensionToStern { get; }
    }
}