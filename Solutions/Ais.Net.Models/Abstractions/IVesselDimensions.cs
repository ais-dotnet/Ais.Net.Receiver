// <copyright file="IVesselDimensions.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Models.Abstractions
{
    public interface IVesselDimensions
    {
        uint DimensionToBow { get; }

        uint DimensionToPort { get; }

        uint DimensionToStarboard { get; }

        uint DimensionToStern { get; }
    }
}