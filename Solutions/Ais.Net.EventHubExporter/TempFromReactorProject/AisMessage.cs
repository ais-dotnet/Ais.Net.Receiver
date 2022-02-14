// <copyright file="AisMessage.cs" company="Endjin">
// Copyright (c) Endjin. All rights reserved.
// </copyright>

namespace Endjin.Reactor.EventData.Ais
{
    using Nuqleon.DataModel;

    /// <summary>
    /// Discriminated union type representing any AIS message.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This enables the definition of a stream that can contain any AIS message type in a way that
    /// can be represented in the Nuqleon data model (meaning that clients don't need to be tied to
    /// any particular definition of this type to consume the stream).
    /// </para>
    /// </remarks>
    public class AisMessage
    {
        /// <summary>
        /// Gets or sets a <see cref="MessageType"/> indicating which kind of message this is.
        /// This determines which of the other properties are populated.
        /// </summary>
        /// <remarks>
        /// TBD: this might not be the right way to do this, because the existence of both class A
        /// and class B equipment, along with different message types for different scenarios (e.g.
        /// the special short forms for use with satellite communications) means that a facet model
        /// might be better: rather than asking "what type of message is this?" we should perhaps
        /// be asking "does this include location information?". However, using this for now
        /// because this is how Bart structured his demo.
        /// </remarks>
        [Mapping("ais://msg/type")]
        public MessageType Type { get; set; }

        /// <summary>
        /// Gets or sets the vessel identifier (MMSI). Always present.
        /// </summary>
        [Mapping("ais://msg/mmsi")]
        public uint Mmsi { get; set; }

        /// <summary>
        /// Gets or sets vessel name information. Present when <see cref="Type"/> is
        /// <see cref="MessageType.Name"/>.
        /// </summary>
        [Mapping("ais://msg/name")]
        public VesselName Name { get; set; }

        /// <summary>
        /// Gets or sets vessel navigation information. Present when <see cref="Type"/> is
        /// <see cref="MessageType.Name"/>.
        /// </summary>
        [Mapping("ais://msg/nav")]
        public VesselNavigation Navigation { get; set; }
    }
}