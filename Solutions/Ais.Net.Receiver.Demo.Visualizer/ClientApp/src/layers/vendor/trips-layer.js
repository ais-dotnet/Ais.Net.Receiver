// Vendored from @deck.gl/geo-layers 9.3.7 (dist/trips-layer/trips-layer.js and
// trips-layer-uniforms.js), unmodified apart from merging the two files.
//
// Why vendored: TripsLayer is the only thing this app used from @deck.gl/geo-layers, and that package
// pulls in about 70 transitive packages (3D tiles, glTF, Draco, compression, zip and XML loaders, H3,
// turf) that the page never loads. They were the source of every Dependabot alert on this app.
//
// Keep in step with @deck.gl/layers: this subclasses PathLayer and injects into its shaders, and
// deck.gl 9.4 changed both PathLayer's fragment shader and TripsLayer to match. package.json pins
// @deck.gl/* to 9.3.x for that reason. When upgrading deck.gl, re-copy this file from the matching
// @deck.gl/geo-layers release.
//
// deck.gl
// SPDX-License-Identifier: MIT
// Copyright (c) vis.gl contributors
import {PathLayer} from '@deck.gl/layers';

const uniformBlock = `\
layout(std140) uniform tripsUniforms {
  bool fadeTrail;
  float trailLength;
  float currentTime;
} trips;
`;

const tripsUniforms = {
  name: 'trips',
  vs: uniformBlock,
  fs: uniformBlock,
  uniformTypes: {
    fadeTrail: 'f32',
    trailLength: 'f32',
    currentTime: 'f32'
  }
};

const defaultProps = {
  fadeTrail: true,
  trailLength: {type: 'number', value: 120, min: 0},
  currentTime: {type: 'number', value: 0, min: 0},
  getTimestamps: {type: 'accessor', value: d => d.timestamps}
};

/** Render animated paths that represent vehicle trips. */
export class TripsLayer extends PathLayer {
  getShaders() {
    const shaders = super.getShaders();
    shaders.inject = {
      'vs:#decl': `\
in float instanceTimestamps;
in float instanceNextTimestamps;
out float vTime;
`,
      // Timestamp of the vertex
      'vs:#main-end': `\
vTime = instanceTimestamps + (instanceNextTimestamps - instanceTimestamps) * vPathPosition.y / vPathLength;
`,
      'fs:#decl': `\
in float vTime;
`,
      // Drop the segments outside of the time window
      'fs:#main-start': `\
if(vTime > trips.currentTime || (trips.fadeTrail && (vTime < trips.currentTime - trips.trailLength))) {
  discard;
}
`,
      // Fade the color (currentTime - 100%, end of trail - 0%)
      'fs:DECKGL_FILTER_COLOR': `\
if(trips.fadeTrail) {
  color.a *= 1.0 - (trips.currentTime - vTime) / trips.trailLength;
}
`
    };
    shaders.modules = [...shaders.modules, tripsUniforms];
    return shaders;
  }

  initializeState() {
    super.initializeState();
    const attributeManager = this.getAttributeManager();
    attributeManager.addInstanced({
      timestamps: {
        size: 1,
        accessor: 'getTimestamps',
        shaderAttributes: {
          instanceTimestamps: {
            vertexOffset: 0
          },
          instanceNextTimestamps: {
            vertexOffset: 1
          }
        }
      }
    });
  }

  draw(params) {
    const {fadeTrail, trailLength, currentTime} = this.props;
    const tripsProps = {fadeTrail, trailLength, currentTime};
    const model = this.state.model;
    model.shaderInputs.setProps({trips: tripsProps});
    super.draw(params);
  }
}

TripsLayer.layerName = 'TripsLayer';
TripsLayer.defaultProps = defaultProps;
