/**
 * Reads the AIS messages the receiver publishes.
 *
 * The payloads are exactly what Ais.Net.Models.Json writes: the model's own property names, with a
 * `$type` discriminator naming the concrete message type. Nothing here re-derives AIS semantics - the
 * ship type stays the number the standard defines, and its category and colour come from the table
 * the host serves out of the same C# the replay pipeline uses.
 */

// Position reports. Type 27 is the long-range variant and carries the same fields we need.
const POSITION_TYPES = new Set(['t1_3', 't18', 't19', 't27']);

// Static and voyage data. Type 24 is split across two parts: A carries the name, B the ship type.
const NAME_TYPES = new Set(['t5', 't19', 't24p0']);
const SHIP_TYPE_TYPES = new Set(['t5', 't19', 't24p1']);

/** AIS pads names to a fixed width with '@', which would otherwise show up in labels. */
export function cleanVesselName(name) {
  return typeof name === 'string' ? name.replace(/@+$/, '').trim() : '';
}

export function isPositionReport(message) {
  return POSITION_TYPES.has(message.$type) && message.Position != null;
}

export function readPosition(message) {
  const {Longitude, Latitude} = message.Position;

  // Discard the (0,0) null-island placeholder and out-of-range values; drawing them would scatter
  // vessels off West Africa. A single zero axis is legitimate - vessels really do cross the equator
  // and the Greenwich meridian. The replay pipeline applies the same rule.
  if ((Latitude === 0 && Longitude === 0) ||
      Latitude > 90 || Latitude < -90 ||
      Longitude > 180 || Longitude < -180) {
    return null;
  }

  return {
    coordinates: [Longitude, Latitude],
    speed: message.SpeedOverGround ?? 0,
    course: message.CourseOverGround ?? 0
  };
}

export function readName(message) {
  return NAME_TYPES.has(message.$type) ? cleanVesselName(message.VesselName) : '';
}

export function readShipType(message) {
  return SHIP_TYPE_TYPES.has(message.$type) && typeof message.ShipType === 'number'
    ? message.ShipType
    : null;
}
