// <copyright file="MessageType.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Endjin.Reactor.EventData.Ais
{
    using Nuqleon.DataModel;

    /// <summary>
    /// Indicates what type of data is in an <see cref="AisMessage"/>.
    /// </summary>
    public enum MessageType
    {
        /// <summary>
        /// The message type is not one supported by this data model mapping.
        /// </summary>
        [Mapping("ais://messagetype/unknown")]
        Unknown,

        /// <summary>
        /// The message type contains vessel name data.
        /// </summary>
        [Mapping("ais://messagetype/name")]
        Name,

        /// <summary>
        /// The message type contains vessel navigation data (position, direction).
        /// </summary>
        [Mapping("ais://messagetype/navigation")]
        Navigation,
    }
}