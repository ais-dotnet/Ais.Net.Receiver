// <copyright file="AisMessageExtensions.cs" company="Endjin Limited">
// Copyright (c) Endjin Limited. All rights reserved.
// </copyright>

namespace Ais.Net.Models
{
    using System;
    using System.Text;
    using Ais.Net.Models.Abstractions;

    public static class AisMessageExtensions
    {
        public static double From10000thMinsToDegrees(this int value)
        {
            return value / 600000.0;
        }
        
        public static double From10thMinsToDegrees(this int value)
        {
            return value / 600.0;
        }

        public static float? FromTenths(this uint value)
        {
            return value == 1023 ? null : (value / 10.0f);
        }

        public static float? FromTenthsToDegrees(this uint value)
        {
            return value == 3600 ? null : (value / 10.0f);
        }

        public static string CleanVesselName(this string value)
        {
            return value.Trim('@').Trim().Replace("  ", " ");
        }

        public static string GetString(this Span<byte> value)
        {
            return Encoding.ASCII.GetString(value);
        }

        public static string TextFieldToString(this NmeaAisTextFieldParser field)
        {
            Span<byte> ascii = stackalloc byte[(int)field.CharacterCount];
            field.WriteAsAscii(ascii);

            return Encoding.ASCII.GetString(ascii);
        }

        public static ShipTypeCategory ToShipTypeCategory(this ShipType shipType)
        {
            return shipType.ToShipTypeGroup().ToShipTypeCategory();
        }

        public static ShipTypeCategory ToShipTypeCategory(this ShipTypeGroup shipTypeGroup)
        {
            return shipTypeGroup switch
            {
                ShipTypeGroup.NotAvailable => ShipTypeCategory.NotAvailable,
                ShipTypeGroup.Reserved => ShipTypeCategory.Reserved,
                ShipTypeGroup.WingInGroundAll => ShipTypeCategory.WingInGround,
                ShipTypeGroup.WingInGroundHazardous => ShipTypeCategory.WingInGround,
                ShipTypeGroup.WingInGroundReserved => ShipTypeCategory.WingInGround,
                ShipTypeGroup.Fishing => ShipTypeCategory.SpecialCategory3,
                ShipTypeGroup.Towing => ShipTypeCategory.SpecialCategory3,
                ShipTypeGroup.TowingLengthOver200OmrBreadthOver25m => ShipTypeCategory.SpecialCategory3,
                ShipTypeGroup.DredgingOrUnderwaterOps => ShipTypeCategory.SpecialCategory3,
                ShipTypeGroup.DivingOps => ShipTypeCategory.SpecialCategory3,
                ShipTypeGroup.MilitaryOps => ShipTypeCategory.SpecialCategory3,
                ShipTypeGroup.Sailing => ShipTypeCategory.SpecialCategory3,
                ShipTypeGroup.PleasureCraft => ShipTypeCategory.SpecialCategory3,
                ShipTypeGroup.HighSpeedCraftAll => ShipTypeCategory.HighSpeedCraft,
                ShipTypeGroup.HighSpeedCraftHazardous => ShipTypeCategory.HighSpeedCraft,
                ShipTypeGroup.HighSpeedCraftReserved => ShipTypeCategory.HighSpeedCraft,
                ShipTypeGroup.HighSpeedCraftNoAdditionalInformation => ShipTypeCategory.HighSpeedCraft,
                ShipTypeGroup.PilotVessel => ShipTypeCategory.SpecialCategory5,
                ShipTypeGroup.SearchAndRescueVessel => ShipTypeCategory.SpecialCategory5,
                ShipTypeGroup.Tug => ShipTypeCategory.SpecialCategory5,
                ShipTypeGroup.PortTender => ShipTypeCategory.SpecialCategory5,
                ShipTypeGroup.AntiPollutionEquipment => ShipTypeCategory.SpecialCategory5,
                ShipTypeGroup.LawEnforcement => ShipTypeCategory.SpecialCategory5,
                ShipTypeGroup.SpareLocalVessel => ShipTypeCategory.SpecialCategory5,
                ShipTypeGroup.MedicalTransport => ShipTypeCategory.SpecialCategory5,
                ShipTypeGroup.NoncombatantShip => ShipTypeCategory.SpecialCategory5,
                ShipTypeGroup.PassengerAll => ShipTypeCategory.Passenger,
                ShipTypeGroup.PassengerHazardous => ShipTypeCategory.Passenger,
                ShipTypeGroup.PassengerReserved => ShipTypeCategory.Passenger,
                ShipTypeGroup.PassengerNoAdditionalInformation => ShipTypeCategory.Passenger,
                ShipTypeGroup.CargoAll => ShipTypeCategory.Cargo,
                ShipTypeGroup.CargoHazardous => ShipTypeCategory.Cargo,
                ShipTypeGroup.CargoReserved => ShipTypeCategory.Cargo,
                ShipTypeGroup.CargoNoAdditionalInformation => ShipTypeCategory.Cargo,
                ShipTypeGroup.TankerAll => ShipTypeCategory.Tanker,
                ShipTypeGroup.TankerHazardous => ShipTypeCategory.Tanker,
                ShipTypeGroup.TankerReserved => ShipTypeCategory.Tanker,
                ShipTypeGroup.TankerNoAdditionalInformation => ShipTypeCategory.Tanker,
                ShipTypeGroup.OtherAll => ShipTypeCategory.Other,
                ShipTypeGroup.OtherHazardous => ShipTypeCategory.Other,
                ShipTypeGroup.OtherReserved => ShipTypeCategory.Other,
                ShipTypeGroup.OtherNoAdditionalInformation => ShipTypeCategory.Other,
                _ => ShipTypeCategory.NotAvailable,
            };
        }

        public static string ToShipTypeDescription(this int shipType)
        {
            switch (shipType)
            {
                case 0: goto default;
                case >= 1 and <= 19: return "Reserved for future use";
                case 20: return "Wing in ground(WIG), all ships of this type";
                case 21: return "Wing in ground(WIG), Hazardous category A";
                case 22: return "Wing in ground(WIG), Hazardous category B";
                case 23: return "Wing in ground(WIG), Hazardous category C";
                case 24: return "Wing in ground(WIG), Hazardous category D";
                case 25: return "Wing in ground(WIG), Reserved for future use";
                case 26: return "Wing in ground(WIG), Reserved for future use";
                case 27: return "Wing in ground(WIG), Reserved for future use";
                case 28: return "Wing in ground(WIG), Reserved for future use";
                case 29: return "Wing in ground(WIG), Reserved for future use";
                case 30: return "Fishing";
                case 31: return "Towing";
                case 32: return "Towing: length exceeds 200m or breadth exceeds 25m";
                case 33: return "Dredging or underwater ops";
                case 34: return "Diving ops";
                case 35: return "Military ops";
                case 36: return "Sailing";
                case 37: return "Pleasure Craft";
                case 38: return "Reserved";
                case 39: return "Reserved";
                case 40: return "High speed craft(HSC), all ships of this type";
                case 41: return "High speed craft(HSC), Hazardous category A";
                case 42: return "High speed craft(HSC), Hazardous category B";
                case 43: return "High speed craft(HSC), Hazardous category C";
                case 44: return "High speed craft(HSC), Hazardous category D";
                case 45: return "High speed craft(HSC), Reserved for future use";
                case 46: return "High speed craft(HSC), Reserved for future use";
                case 47: return "High speed craft(HSC), Reserved for future use";
                case 48: return "High speed craft(HSC), Reserved for future use";
                case 49: return "High speed craft(HSC), No additional information";
                case 50: return "Pilot Vessel";
                case 51: return "Search and Rescue vessel";
                case 52: return "Tug";
                case 53: return "Port Tender";
                case 54: return "Anti - pollution equipment";
                case 55: return "Law Enforcement";
                case 56: return "Spare - Local Vessel";
                case 57: return "Spare - Local Vessel";
                case 58: return "Medical Transport";
                case 59: return "Noncombatant ship according to RR Resolution No. 18";
                case 60: return "Passenger, all ships of this type";
                case 61: return "Passenger, Hazardous category A";
                case 62: return "Passenger, Hazardous category B";
                case 63: return "Passenger, Hazardous category C";
                case 64: return "Passenger, Hazardous category D";
                case 65: return "Passenger, Reserved for future use";
                case 66: return "Passenger, Reserved for future use";
                case 67: return "Passenger, Reserved for future use";
                case 68: return "Passenger, Reserved for future use";
                case 69: return "Passenger, No additional information";
                case 70: return "Cargo, all ships of this type";
                case 71: return "Cargo, Hazardous category A";
                case 72: return "Cargo, Hazardous category B";
                case 73: return "Cargo, Hazardous category C";
                case 74: return "Cargo, Hazardous category D";
                case 75: return "Cargo, Reserved for future use";
                case 76: return "Cargo, Reserved for future use";
                case 77: return "Cargo, Reserved for future use";
                case 78: return "Cargo, Reserved for future use";
                case 79: return "Cargo, No additional information";
                case 80: return "Tanker, all ships of this type";
                case 81: return "Tanker, Hazardous category A";
                case 82: return "Tanker, Hazardous category B";
                case 83: return "Tanker, Hazardous category C";
                case 84: return "Tanker, Hazardous category D";
                case 85: return "Tanker, Reserved for future use";
                case 86: return "Tanker, Reserved for future use";
                case 87: return "Tanker, Reserved for future use";
                case 88: return "Tanker, Reserved for future use";
                case 89: return "Tanker, No additional information";
                case 90: return "Other Type, all ships of this type";
                case 91: return "Other Type, Hazardous category A";
                case 92: return "Other Type, Hazardous category B";
                case 93: return "Other Type, Hazardous category C";
                case 94: return "Other Type, Hazardous category D";
                case 95: return "Other Type, Reserved for future use";
                case 96: return "Other Type, Reserved for future use";
                case 97: return "Other Type, Reserved for future use";
                case 98: return "Other Type, Reserved for future use";
                case 99: return "Other Type, no additional information";
                default: return "Not available";
            }
        }

        public static ShipTypeGroup ToShipTypeGroup(this ShipType shipType)
        {
            return ToShipTypeGroup((int)shipType);
        }

        public static ShipTypeGroup ToShipTypeGroup(this int shipType)
        {
            switch (shipType)
            {
                case 0: goto default;
                case >= 1 and <= 19: return ShipTypeGroup.Reserved;
                case 20: return ShipTypeGroup.WingInGroundAll;
                case >= 21 and <= 24: return ShipTypeGroup.WingInGroundHazardous;
                case >= 25 and <= 29: return ShipTypeGroup.WingInGroundReserved;
                case 30: return ShipTypeGroup.Fishing;
                case 31: return ShipTypeGroup.Towing;
                case 32: return ShipTypeGroup.TowingLengthOver200OmrBreadthOver25m;
                case 33: return ShipTypeGroup.DredgingOrUnderwaterOps;
                case 34: return ShipTypeGroup.DivingOps;
                case 35: return ShipTypeGroup.MilitaryOps;
                case 36: return ShipTypeGroup.Sailing;
                case 37: return ShipTypeGroup.PleasureCraft;
                case >= 38 and <= 39: return ShipTypeGroup.Reserved;
                case 40: return ShipTypeGroup.HighSpeedCraftAll;
                case >= 41 and <= 44: return ShipTypeGroup.HighSpeedCraftHazardous;
                case >= 45 and <= 49: return ShipTypeGroup.HighSpeedCraftReserved;
                case 50: return ShipTypeGroup.PilotVessel;
                case 51: return ShipTypeGroup.SearchAndRescueVessel;
                case 52: return ShipTypeGroup.Tug;
                case 53: return ShipTypeGroup.PortTender;
                case 54: return ShipTypeGroup.AntiPollutionEquipment;
                case 55: return ShipTypeGroup.LawEnforcement;
                case >= 56 and <= 57: return ShipTypeGroup.SpareLocalVessel;
                case 58: return ShipTypeGroup.MedicalTransport;
                case 59: return ShipTypeGroup.NoncombatantShip;
                case 60: return ShipTypeGroup.PassengerAll;
                case >= 61 and <= 64: return ShipTypeGroup.PassengerHazardous;
                case >= 65 and <= 68: return ShipTypeGroup.PassengerReserved;
                case 69: return ShipTypeGroup.PassengerNoAdditionalInformation;
                case 70: return ShipTypeGroup.CargoAll;
                case >= 71 and <= 74: return ShipTypeGroup.CargoHazardous;
                case >= 75 and <= 78: return ShipTypeGroup.CargoReserved;
                case 79: return ShipTypeGroup.CargoNoAdditionalInformation;
                case 80: return ShipTypeGroup.TankerAll;
                case >= 81 and <= 84: return ShipTypeGroup.TankerHazardous;
                case >= 85 and <= 88: return ShipTypeGroup.TankerReserved;
                case 89: return ShipTypeGroup.TankerNoAdditionalInformation;
                case 90: return ShipTypeGroup.OtherAll;
                case >= 91 and <= 94: return ShipTypeGroup.OtherHazardous;
                case >= 95 and <= 98: return ShipTypeGroup.OtherReserved;
                case 99: return ShipTypeGroup.OtherNoAdditionalInformation;
                default: return ShipTypeGroup.NotAvailable;
            }
        }
    }
}