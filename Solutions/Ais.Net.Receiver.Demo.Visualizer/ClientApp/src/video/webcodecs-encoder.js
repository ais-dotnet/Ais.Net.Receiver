/**
 * WebCodecs-based VP9 encoder using webm-muxer.
 *
 * VideoFrame is created directly from the canvas (GPU-to-GPU, no pixel
 * readback), and VideoEncoder produces compressed chunks that are muxed
 * into a seekable WebM container.
 */
import {Muxer, ArrayBufferTarget} from 'webm-muxer';

const BITRATE = 8_000_000; // 8 Mbps
const KEYFRAME_INTERVAL_S = 2; // keyframe every 2 seconds

export class WebCodecsEncoder {
  constructor({width, height, framerate}) {
    this.width = width;
    this.height = height;
    this.framerate = framerate;
    this.frameIndex = 0;
    this.encoder = null;
    this.muxer = null;
    this.target = null;
  }

  start() {
    this.target = new ArrayBufferTarget();
    this.muxer = new Muxer({
      target: this.target,
      video: {
        codec: 'V_VP9',
        width: this.width,
        height: this.height,
        frameRate: this.framerate
      },
      type: 'webm'
    });

    this.encoder = new VideoEncoder({
      output: (chunk, meta) => this.muxer.addVideoChunk(chunk, meta),
      error: (e) => console.error('VideoEncoder error:', e)
    });

    this.encoder.configure({
      codec: 'vp09.00.10.08', // VP9 profile 0, level 1.0, 8-bit
      width: this.width,
      height: this.height,
      bitrate: BITRATE,
      framerate: this.framerate
    });

    this.frameIndex = 0;
  }

  async add(canvas) {
    // Back-pressure: wait if the encoder queue is building up
    while (this.encoder.encodeQueueSize > 5) {
      await new Promise(r => setTimeout(r, 1));
    }

    const timestampUs = (this.frameIndex / this.framerate) * 1_000_000;
    const durationUs = (1 / this.framerate) * 1_000_000;
    const keyFrame = this.frameIndex % (this.framerate * KEYFRAME_INTERVAL_S) === 0;

    const frame = new VideoFrame(canvas, {
      timestamp: timestampUs,
      duration: durationUs
    });

    this.encoder.encode(frame, {keyFrame});
    frame.close();
    this.frameIndex++;
  }

  async save() {
    await this.encoder.flush();
    this.muxer.finalize();
    const buf = this.target.buffer;
    return new Blob([buf], {type: 'video/webm'});
  }
}
