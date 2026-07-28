import {ScatterplotLayer} from '@deck.gl/layers';
import {DOT_RADIUS} from '../config.js';
import {interpolatePosition} from '../utils/data-loader.js';

const getPosition = d => d._currentPos.coordinates;
const getFillColor = d => d.color;

export function createPositionLayer(vessels, currentTime, visible) {
  // Filter to vessels with valid current position
  const activeVessels = [];
  for (const v of vessels) {
    const pos = interpolatePosition(v.positions, currentTime);
    if (pos) {
      // Attach current position as transient property
      v._currentPos = pos;
      activeVessels.push(v);
    }
  }

  return {
    layer: new ScatterplotLayer({
      id: 'vessel-positions',
      data: activeVessels,
      getPosition,
      getFillColor,
      getRadius: DOT_RADIUS,
      radiusUnits: 'pixels',
      radiusMinPixels: 3,
      radiusMaxPixels: 8,
      pickable: true,
      visible
    }),
    activeVessels,
    activeCount: activeVessels.length
  };
}
