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

    /// <summary>
    /// Builds the whole ship type to category-and-colour mapping as a table, keyed by the numeric AIS
    /// ship type that appears on the wire.
    /// </summary>
    /// <returns>An entry for every ship type value AIS defines.</returns>
    /// <remarks>
    /// This exists so the live view can colour vessels without the browser knowing anything about AIS.
    /// The page decodes raw messages itself and reads <c>ShipType</c> as a number; handing it this
    /// table means the ship-type-to-category rules and the palette stay here, in one place, shared
    /// with the replay pipeline, rather than being reimplemented in JavaScript and drifting.
    /// </remarks>
    public static IReadOnlyDictionary<int, ShipTypeStyle> GetStyleTable()
    {
        // AIS ship type is a two-digit code, so 0-99 covers every value a message can carry.
        Dictionary<int, ShipTypeStyle> table = new(100);

        for (int shipType = 0; shipType <= 99; shipType++)
        {
            (string category, int[] color) = GetCategoryAndColor((ShipType)shipType);
            table[shipType] = new ShipTypeStyle(category, color);
        }

        return table;
    }
}

/// <summary>
/// How one ship type is presented: the category shown in labels and tooltips, and the colour its
/// vessels are drawn in.
/// </summary>
/// <param name="Category">The human-readable category name.</param>
/// <param name="Color">The RGB triple.</param>
public readonly record struct ShipTypeStyle(string Category, int[] Color);
