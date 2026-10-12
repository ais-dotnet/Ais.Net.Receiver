import {TRACKS_URL} from '../config.js';

export async function loadVesselData() {
  const response = await fetch(TRACKS_URL);
  if (!response.ok) throw new Error(`Failed to load vessel data: ${response.status}`);

  const raw = await response.json();

  // Pre-process vessels for deck.gl layers
  const vessels = raw.vessels.map(v => {
    // Pre-compute path array for PathLayer: [[lon, lat], ...]
    const path = v.positions.map(p => p.coordinates);
    // Pre-compute timestamps array for TripsLayer
    const timestamps = v.positions.map(p => p.timestamp);

    return {
      mmsi: v.mmsi,
      name: v.name,
      shipType: v.shipType,
      shipTypeCategory: v.shipTypeCategory,
      color: v.color,
      path,
      timestamps,
      positions: v.positions
    };
  });

  return {
    metadata: raw.metadata,
    vessels
  };
}

// Binary search to find the index of the last position with timestamp <= t
export function findPositionIndex(positions, t) {
  let lo = 0;
  let hi = positions.length - 1;

  if (hi < 0 || t < positions[0].timestamp) return -1;
  if (t >= positions[hi].timestamp) return hi;

  while (lo < hi) {
    const mid = (lo + hi + 1) >> 1;
    if (positions[mid].timestamp <= t) {
      lo = mid;
    } else {
      hi = mid - 1;
    }
  }
  return lo;
}

// Interpolate position at time t
export function interpolatePosition(positions, t) {
  const idx = findPositionIndex(positions, t);
  if (idx < 0) return null;
  if (idx >= positions.length - 1) {
    const p = positions[idx];
    // Only show if within reasonable time window (10 min)
    if (t - p.timestamp > 600) return null;
    return {coordinates: p.coordinates, speed: p.speed, course: p.course};
  }

  const p0 = positions[idx];
  const p1 = positions[idx + 1];
  const dt = p1.timestamp - p0.timestamp;

  // If gap is too large, don't interpolate
  if (dt > 600) {
    if (t - p0.timestamp > 600) return null;
    return {coordinates: p0.coordinates, speed: p0.speed, course: p0.course};
  }

  const frac = (t - p0.timestamp) / dt;
  return {
    coordinates: [
      p0.coordinates[0] + (p1.coordinates[0] - p0.coordinates[0]) * frac,
      p0.coordinates[1] + (p1.coordinates[1] - p0.coordinates[1]) * frac
    ],
    speed: p0.speed + (p1.speed - p0.speed) * frac,
    course: p0.course + (p1.course - p0.course) * frac
  };
}
