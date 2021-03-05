// <copyright file="IAisPositionFixType.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Ais.Net.Models.Abstractions
{
    public interface IAisPositionFixType
    {
        EpfdFixType PositionFixType { get; }
    }
}