import {TripsLayer} from '@deck.gl/geo-layers';
import {TRAIL_LENGTH_SECONDS, TRAIL_WIDTH} from '../config.js';

const getPath = d => d.path;
const getTimestamps = d => d.timestamps;
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
