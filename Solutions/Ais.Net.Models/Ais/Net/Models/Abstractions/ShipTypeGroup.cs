// <copyright file="ShipTypeGroup.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Models.Abstractions
{
    public enum ShipTypeGroup
    {
        NotAvailable,
        Reserved,
        WingInGroundAll,
        WingInGroundHazardous,
        WingInGroundReserved,

        Fishing,
        Towing,
        TowingLengthOver200OmrBreadthOver25m,
        DredgingOrUnderwaterOps,
        DivingOps,
        MilitaryOps,
        Sailing,
        PleasureCraft,

        HighSpeedCraftAll,
        HighSpeedCraftHazardous,
        HighSpeedCraftReserved,
        HighSpeedCraftNoAdditionalInformation,

        PilotVessel,
        SearchAndRescueVessel,
        Tug,
        PortTender,
        AntiPollutionEquipment,
        LawEnforcement,
        SpareLocalVessel,
        MedicalTransport,
        NoncombatantShip,

        PassengerAll,
        PassengerHazardous,
        PassengerReserved,
        PassengerNoAdditionalInformation,

        CargoAll,
        CargoHazardous,
        CargoReserved,
        CargoNoAdditionalInformation,

        TankerAll,
        TankerHazardous,
        TankerReserved,
        TankerNoAdditionalInformation,

        OtherAll,
        OtherHazardous,
        OtherReserved,
        OtherNoAdditionalInformation,
    }
}