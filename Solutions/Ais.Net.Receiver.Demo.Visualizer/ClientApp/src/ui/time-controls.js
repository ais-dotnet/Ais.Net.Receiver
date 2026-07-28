import {ANIMATION_SPEEDS, DEFAULT_SPEED_INDEX} from '../config.js';

export class TimeControls {
  constructor(timeRange, onTimeChange) {
    this.timeRange = timeRange;
    this.onTimeChange = onTimeChange;
    this.playing = false;
    this.speedIndex = DEFAULT_SPEED_INDEX;
    this.currentTime = timeRange.start;
    this._lastFrameTime = null;
    this._animId = null;

    this._bindElements();
    this._bindEvents();
    this._updateSpeedDisplay();
  }

  _bindElements() {
    this.btnPlay = document.getElementById('btn-play');
    this.btnSlower = document.getElementById('btn-slower');
    this.btnFaster = document.getElementById('btn-faster');
    this.slider = document.getElementById('time-slider');
    this.speedDisplay = document.getElementById('speed-display');

    this.slider.min = this.timeRange.start;
    this.slider.max = this.timeRange.end;
    this.slider.value = this.timeRange.start;
  }

  _bindEvents() {
    this.btnPlay.addEventListener('click', () => this.togglePlay());
    this.btnSlower.addEventListener('click', () => this.changeSpeed(-1));
    this.btnFaster.addEventListener('click', () => this.changeSpeed(1));
    this.slider.addEventListener('input', () => {
      this.currentTime = Number(this.slider.value);
      this.onTimeChange(this.currentTime);
    });

    document.addEventListener('keydown', (e) => {
      if (e.target.tagName === 'INPUT' && e.target.type !== 'range') return;
      switch (e.code) {
        case 'Space':
          e.preventDefault();
          this.togglePlay();
          break;
        case 'ArrowRight':
          e.preventDefault();
          this.step(60);
          break;
        case 'ArrowLeft':
          e.preventDefault();
          this.step(-60);
          break;
        case 'Equal':
        case 'NumpadAdd':
          this.changeSpeed(1);
          break;
        case 'Minus':
        case 'NumpadSubtract':
          this.changeSpeed(-1);
          break;
      }
    });
  }

  get speed() {
    return ANIMATION_SPEEDS[this.speedIndex];
  }

  togglePlay() {
    this.playing = !this.playing;
    this.btnPlay.textContent = this.playing ? 'Pause' : 'Play';
    this.btnPlay.classList.toggle('active', this.playing);

    if (this.playing) {
      this._lastFrameTime = performance.now();
      this._animate();
    } else {
      if (this._animId) cancelAnimationFrame(this._animId);
    }
  }

  changeSpeed(delta) {
    this.speedIndex = Math.max(0, Math.min(ANIMATION_SPEEDS.length - 1, this.speedIndex + delta));
    this._updateSpeedDisplay();
  }

  step(seconds) {
    this.currentTime = Math.max(
      this.timeRange.start,
      Math.min(this.timeRange.end, this.currentTime + seconds)
    );
    this.slider.value = this.currentTime;
    this.onTimeChange(this.currentTime);
  }

  _updateSpeedDisplay() {
    const s = this.speed;
    this.speedDisplay.textContent = s < 1 ? `${s}x` : `${s}x`;
  }

  _animate() {
    if (!this.playing) return;

    const now = performance.now();
    const dt = (now - this._lastFrameTime) / 1000; // real seconds elapsed
    this._lastFrameTime = now;

    this.currentTime += dt * this.speed;

    if (this.currentTime >= this.timeRange.end) {
      this.currentTime = this.timeRange.start;
    }

    this.slider.value = this.currentTime;
    this.onTimeChange(this.currentTime);

    this._animId = requestAnimationFrame(() => this._animate());
  }

  setTime(t) {
    this.currentTime = t;
    this.slider.value = t;
  }
}
