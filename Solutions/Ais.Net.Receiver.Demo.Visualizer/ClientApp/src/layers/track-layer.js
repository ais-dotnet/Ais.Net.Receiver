import {PathLayer} from '@deck.gl/layers';
import {TRACK_OPACITY} from '../config.js';
import {withAlpha} from '../utils/color-utils.js';

const getPath = d => d.path;
const getColor = d => withAlpha(d.color, TRACK_OPACITY);

export function createTrackLayer(vessels, visible) {
  return new PathLayer({
    id: 'vessel-tracks',
    data: vessels,
    getPath,
    getColor,
    getWidth: 1,
    widthUnits: 'pixels',
    widthMinPixels: 1,
    pickable: false,
    visible
  });
}
