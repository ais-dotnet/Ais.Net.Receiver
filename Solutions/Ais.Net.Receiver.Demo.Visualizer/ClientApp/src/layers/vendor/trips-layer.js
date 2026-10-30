// Vendored from @deck.gl/geo-layers 9.4.1 (dist/trips-layer/trips-layer.js, trips-layer-uniforms.js and
// trips-layer.wgsl.js), unmodified apart from merging the three files.
//
// Why vendored: TripsLayer is the only thing this app used from @deck.gl/geo-layers, and that package
// pulls in about 70 transitive packages (3D tiles, glTF, Draco, compression, zip and XML loaders, H3,
// turf) that the page never loads. They were the source of every Dependabot alert on this app.
//
// Keep in step with @deck.gl/layers: this subclasses PathLayer and injects into its shaders, and
// deck.gl minor releases can change both PathLayer's fragment shader and TripsLayer to match.
// package.json pins @deck.gl/* to 9.4.x for that reason. When upgrading deck.gl, re-copy this file from
// the matching @deck.gl/geo-layers release.
//
// deck.gl
// SPDX-License-Identifier: MIT
// Copyright (c) vis.gl contributors
import {createIterable} from '@deck.gl/core';
import {PathLayer} from '@deck.gl/layers';

const uniformBlockWGSL = /* wgsl */ `\
struct TripsUniforms {
  fadeTrail: f32,
  trailLength: f32,
  currentTime: f32,
};

@group(0) @binding(auto)
var<uniform> trips: TripsUniforms;
`;
const uniformBlockGLSL = `\
layout(std140) uniform tripsUniforms {
  bool fadeTrail;
  float trailLength;
  float currentTime;
} trips;
`;
const tripsUniforms = {
    name: 'trips',
    source: uniformBlockWGSL,
    vs: uniformBlockGLSL,
    fs: uniformBlockGLSL,
    uniformTypes: {
        fadeTrail: 'f32',
        trailLength: 'f32',
        currentTime: 'f32'
    }
};

/** Pack timestamps in the same padded instance order used by PathTesselator. */
function packTripTimestamps(timestamps, instanceCount = timestamps.length, isLoop = false) {
    const packedTimestamps = new Float32Array(instanceCount * 2);
    if (timestamps.length === 0) {
        return packedTimestamps;
    }
    const isClosed = instanceCount > timestamps.length;
    const cycleLength = isLoop ? timestamps.length : Math.max(timestamps.length - 1, 1);
    for (let index = 0; index < instanceCount; index++) {
        const timestampIndex = isClosed ? index % cycleLength : Math.min(index, timestamps.length - 1);
        const nextTimestampIndex = isClosed
            ? (timestampIndex + 1) % cycleLength
            : Math.min(timestampIndex + 1, timestamps.length - 1);
        packedTimestamps[index * 2] = timestamps[timestampIndex];
        packedTimestamps[index * 2 + 1] = timestamps[nextTimestampIndex];
    }
    return packedTimestamps;
}
/** Extend PathLayer's WGSL without duplicating its path geometry or changing its GLSL hooks. */
const tripsShaderInjectionsWGSL = {
    '  @location(12) rowIndexes: u32,': /* wgsl */ `
  @location(13) instanceTimestamps: vec2<f32>,`,
    '  @location(5) vJointType: f32,': /* wgsl */ `
  @location(6) vTime: f32,`,
    '    attributes.instanceColors.a * layer.opacity\n  );': /* wgsl */ `

  varyings.vTime = mix(
    attributes.instanceTimestamps.x,
    attributes.instanceTimestamps.y,
    varyings.vPathPosition.y / varyings.vPathLength
  );

  if (trips.fadeTrail > 0.5) {
    varyings.vColor.a *=
      1.0 - (trips.currentTime - varyings.vTime) / trips.trailLength;
  }`,
    '  // DECKGL_FILTER_COLOR': /* wgsl */ `
  if (
    varyings.vTime > trips.currentTime ||
    (trips.fadeTrail > 0.5 && varyings.vTime < trips.currentTime - trips.trailLength)
  ) {
    discard;
  }`
};

const defaultProps = {
    fadeTrail: true,
    trailLength: { type: 'number', value: 120, min: 0 },
    currentTime: { type: 'number', value: 0, min: 0 },
    getTimestamps: { type: 'accessor', value: (d) => d.timestamps }
};
const tripsShaderInjectionsGLSL = {
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
    // Drop segments only after PathLayer evaluates analytic-coverage derivatives, then
    // fade the color (currentTime - 100%, end of trail - 0%).
    'fs:DECKGL_FILTER_COLOR': `\
if(vTime > trips.currentTime || (trips.fadeTrail && (vTime < trips.currentTime - trips.trailLength))) {
  discard;
}
if(trips.fadeTrail) {
  color.a *= 1.0 - (trips.currentTime - vTime) / trips.trailLength;
}
`
};
/** Render animated paths that represent vehicle trips. */
export class TripsLayer extends PathLayer {
    getShaders() {
        const shaders = super.getShaders();
        shaders.inject =
            this.context.device.type === 'webgpu' ? tripsShaderInjectionsWGSL : tripsShaderInjectionsGLSL;
        shaders.modules = [...shaders.modules, tripsUniforms];
        return shaders;
    }
    initializeState() {
        super.initializeState();
        const attributeManager = this.getAttributeManager();
        attributeManager.addInstanced({
            ...(this.context.device.type === 'webgpu'
                ? {
                    instanceTimestamps: {
                        size: 2,
                        accessor: 'getTimestamps',
                        // eslint-disable-next-line @typescript-eslint/unbound-method
                        update: this.calculateWebGPUTimestamps,
                        bufferGroup: 'path-instance-data'
                    }
                }
                : {
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
                })
        });
    }
    /** Materialize timestamps in the same per-path instance layout as the path tessellator. */
    calculateWebGPUTimestamps(attribute, { data, props }) {
        const { pathTesselator } = this.state;
        const { instanceCount, vertexStarts } = pathTesselator;
        const packedTimestamps = new Float32Array(instanceCount * 2);
        const { iterable, objectInfo } = createIterable(data);
        for (const object of iterable) {
            objectInfo.index++;
            const vertexStart = vertexStarts[objectInfo.index];
            const vertexEnd = vertexStarts[objectInfo.index + 1] ?? instanceCount;
            if (vertexEnd <= vertexStart) {
                continue;
            }
            const timestamps = props.getTimestamps?.(object, objectInfo) ?? [];
            packedTimestamps.set(packTripTimestamps(timestamps, vertexEnd - vertexStart, props._pathType === 'loop'), vertexStart * 2);
        }
        attribute.startIndices = vertexStarts;
        attribute.value = packedTimestamps;
    }
    draw(params) {
        const { fadeTrail, trailLength, currentTime } = this.props;
        const tripsProps = { fadeTrail, trailLength, currentTime };
        const model = this.state.model;
        model.shaderInputs.setProps({ trips: tripsProps });
        super.draw(params);
    }
}
TripsLayer.layerName = 'TripsLayer';
TripsLayer.defaultProps = defaultProps;
