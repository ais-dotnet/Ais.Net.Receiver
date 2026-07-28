// <copyright file="ShipTypeColors.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

using Ais.Net.Models;
using Ais.Net.Models.Abstractions;

namespace Ais.Net.Receiver.Demo.Tracks.Processing;

public static class ShipTypeColors
{
    public static (string Category, int[] Color) GetCategoryAndColor(ShipType shipType)
    {
        var category = shipType.ToShipTypeCategory();
        string categoryName = category switch
        {
            ShipTypeCategory.NotAvailable => "Not Available",
            ShipTypeCategory.Reserved => "Reserved",
            ShipTypeCategory.WingInGround => "Wing In Ground",
            ShipTypeCategory.SpecialCategory3 => "Special Category",
            ShipTypeCategory.HighSpeedCraft => "High Speed Craft",
            ShipTypeCategory.SpecialCategory5 => "Special Category",
            ShipTypeCategory.Passenger => "Passenger",
            ShipTypeCategory.Cargo => "Cargo",
            ShipTypeCategory.Tanker => "Tanker",
            ShipTypeCategory.Other => "Other",
            _ => "Not Available",
        };

        int[] color = category switch
        {
            ShipTypeCategory.NotAvailable => [150, 249, 161],
            ShipTypeCategory.Reserved => [28, 121, 240],
            ShipTypeCategory.WingInGround => [248, 186, 151],
            ShipTypeCategory.SpecialCategory3 => [248, 181, 148],
            ShipTypeCategory.HighSpeedCraft => [255, 255, 85],
            ShipTypeCategory.SpecialCategory5 => [67, 255, 255],
            ShipTypeCategory.Passenger => [32, 61, 179],
            ShipTypeCategory.Cargo => [151, 249, 161],
            ShipTypeCategory.Tanker => [255, 70, 78],
            ShipTypeCategory.Other => [86, 255, 255],
            _ => [150, 249, 161],
        };

        return (categoryName, color);
    }
}
