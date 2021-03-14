// <copyright file="Position.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Models.Abstractions
{
    public record Position(double Latitude, double Longitude)
    {
        public static Position From10000thMins(int latitude, int longitude) =>
            new (latitude.From10000thMinsToDegrees(), longitude.From10000thMinsToDegrees());
    }
}