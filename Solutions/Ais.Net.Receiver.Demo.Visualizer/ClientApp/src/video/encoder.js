/**
 * Video export using WebCodecs + webm-muxer for frame-by-frame capture,
 * with a custom rendering loop that steps through the animation timeline.
 *
 * Captures directly from the MapLibre canvas using VideoFrame (GPU-to-GPU,
 * no pixel readback) for dramatically faster encoding than toDataURL.
 */
import {WebCodecsEncoder} from './webcodecs-encoder.js';

export class VideoExporter {
  constructor({map, getLayersForTime, metadata, overlay}) {
    this.map = map;
    this.getLayersForTime = getLayersForTime;
    this.metadata = metadata;
    this.overlay = overlay;
    this.recording = false;
    this.cancelled = false;
    this.encoder = null;
    this.onProgress = null;
    this.onComplete = null;
  }

  async start({
    durationSeconds = 60,
    framerate = 30,
    speed = null,
    startTime = null,
    onProgress = null,
    onComplete = null
  }) {
    if (this.recording) return;
    this.recording = true;
    this.cancelled = false;
    this.onProgress = onProgress;
    this.onComplete = onComplete;

    const totalFrames = durationSeconds * framerate;
    const timeRange = this.metadata.timeRange;
    const simDuration = timeRange.end - timeRange.start;
    const timeStep = speed != null ? speed / framerate : simDuration / totalFrames;
    const simStart = startTime != null ? startTime : timeRange.start;

    const mapCanvas = this.map.getCanvas();

    // Offscreen canvas for compositing MapLibre + deck.gl layers
    const compositeCanvas = new OffscreenCanvas(mapCanvas.width, mapCanvas.height);
    const ctx = compositeCanvas.getContext('2d');

    // Create WebCodecs encoder sized to the canvas
    this.encoder = new WebCodecsEncoder({
      width: mapCanvas.width,
      height: mapCanvas.height,
      framerate
    });
    this.encoder.start();

    for (let frame = 0; frame < totalFrames; frame++) {
      if (!this.recording) break;

      let simTime = simStart + frame * timeStep;
      if (simTime > timeRange.end) {
        simTime = timeRange.start + (simTime - timeRange.end) % simDuration;
      }

      // Update layers to this time
      const layers = this.getLayersForTime(simTime);
      this.overlay.setProps({layers});

      // Force a synchronous repaint
      this.map.triggerRepaint();

      // Wait for both MapLibre and deck.gl to finish rendering
      await this._waitForRender();

      // Composite MapLibre base map + deck.gl overlay into one canvas
      ctx.clearRect(0, 0, compositeCanvas.width, compositeCanvas.height);
      ctx.drawImage(mapCanvas, 0, 0);
      const deckCanvas = this.overlay.getCanvas();
      if (deckCanvas) {
        ctx.drawImage(deckCanvas, 0, 0);
      }

      // Encode the composited frame
      await this.encoder.add(compositeCanvas);

      if (this.onProgress) {
        this.onProgress((frame + 1) / totalFrames);
      }
    }

    if (this.cancelled) {
      // A cancelled export must not behave like a successful one: flush the encoder so its
      // resources are released, but discard the partial video and skip the completion callback
      // (which is what triggers the download).
      try {
        await this.encoder.save();
      } catch {
        // The partial encoding is being thrown away regardless.
      }

      this.encoder = null;
      this.recording = false;
      return null;
    }

    // Save the video
    const blob = await this.encoder.save();
    this.recording = false;

    if (blob && this.onComplete) {
      this.onComplete(blob);
    }

    return blob;
  }

  stop() {
    this.cancelled = true;
    this.recording = false;
  }

  _waitForRender() {
    return new Promise(resolve => {
      const timeout = setTimeout(resolve, 100);
      // Step 1: wait for MapLibre to finish painting (this also triggers deck.gl's viewstate sync)
      this.map.once('render', () => {
        clearTimeout(timeout);
        // Step 2: yield one frame so deck.gl completes its render pass
        requestAnimationFrame(() => resolve());
      });
    });
  }
}

export function downloadBlob(blob, filename) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}
