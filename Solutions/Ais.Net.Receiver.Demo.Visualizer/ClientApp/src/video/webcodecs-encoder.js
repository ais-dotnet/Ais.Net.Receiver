/**
 * WebCodecs-based VP9 encoder using Mediabunny for the WebM container.
 *
 * VideoFrame is created directly from the canvas (GPU-to-GPU, no pixel
 * readback), and VideoEncoder produces compressed chunks that are muxed
 * into a seekable WebM container.
 */
import {BufferTarget, EncodedPacket, EncodedVideoPacketSource, Output, WebMOutputFormat} from 'mediabunny';

const BITRATE = 8_000_000; // 8 Mbps
const KEYFRAME_INTERVAL_S = 2; // keyframe every 2 seconds

export class WebCodecsEncoder {
  constructor({width, height, framerate}) {
    this.width = width;
    this.height = height;
    this.framerate = framerate;
    this.frameIndex = 0;
    this.encoder = null;
    this.output = null;
    this.source = null;
    this.muxing = Promise.resolve();
  }

  async start() {
    this.output = new Output({
      format: new WebMOutputFormat(),
      target: new BufferTarget()
    });
    this.source = new EncodedVideoPacketSource('vp9');
    this.output.addVideoTrack(this.source, {frameRate: this.framerate});
    await this.output.start();

    this.muxing = Promise.resolve();
    this.encoder = new VideoEncoder({
      // Mediabunny wants packets added in decode order, one at a time, and add() is async. The
      // encoder calls output() synchronously for each chunk, so chain the adds; save() awaits the
      // chain, which also surfaces any muxing error instead of losing it in a floating promise.
      output: (chunk, meta) => {
        const packet = EncodedPacket.fromEncodedChunk(chunk);
        this.muxing = this.muxing.then(() => this.source.add(packet, meta));
      },
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
    await this.muxing;
    await this.output.finalize();
    return new Blob([this.output.target.buffer], {type: 'video/webm'});
  }
}
