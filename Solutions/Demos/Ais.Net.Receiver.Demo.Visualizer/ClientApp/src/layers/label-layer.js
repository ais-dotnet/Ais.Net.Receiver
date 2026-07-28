import {TextLayer} from '@deck.gl/layers';
import {CollisionFilterExtension} from '@deck.gl/extensions';
import {LABEL_SIZE} from '../config.js';

const getPosition = d => d._currentPos.coordinates;
const getText = d => {
  const cat = d.shipTypeCategory || '';
  return d.name + (cat ? '\n' + cat : '');
};
const getColor = [255, 255, 255, 220];
const getPixelOffset = [0, -14];

export function createLabelLayer(activeVessels, visible) {
  return new TextLayer({
    id: 'vessel-labels',
    data: activeVessels,
    getPosition,
    getText,
    getColor,
    getSize: LABEL_SIZE,
    getTextAnchor: 'middle',
    getAlignmentBaseline: 'bottom',
    getPixelOffset: getPixelOffset,
    fontFamily: 'Segoe UI, system-ui, sans-serif',
    fontWeight: 700,
    outlineWidth: 2,
    outlineColor: [0, 0, 0, 200],
    pickable: false,
    visible,
    getCollisionPriority: d => d.positions.length,
    collisionTestProps: {sizeScale: 2},
    extensions: [new CollisionFilterExtension()]
  });
}
