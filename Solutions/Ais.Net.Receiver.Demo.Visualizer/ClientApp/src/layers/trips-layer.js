import {TripsLayer} from './vendor/trips-layer.js';
import {TRAIL_LENGTH_SECONDS, TRAIL_WIDTH} from '../config.js';

const getPath = d => d.path;

// PathLayer treats a path whose ends match as closed, and a closed path of two points as invalid, so
// it gets no vertices. A vessel reporting the same position twice is exactly that, and writing its two
// timestamps would overrun the attribute buffer when it is the last vessel in the data.
const isDegeneratePath = ({path}) =>
  path.length === 2 && path[0][0] === path[1][0] && path[0][1] === path[1][1];
const getTimestamps = d => (isDegeneratePath(d) ? d.timestamps.slice(0, 1) : d.timestamps);
const getColor = d => d.color;

export function createTripsLayer(vessels, currentTime, visible) {
  return new TripsLayer({
    id: 'vessel-trips',
    data: vessels,
    getPath,
    getTimestamps,
    getColor,
    widthMinPixels: TRAIL_WIDTH,
    trailLength: TRAIL_LENGTH_SECONDS,
    currentTime,
    pickable: false,
    visible
  });
}
