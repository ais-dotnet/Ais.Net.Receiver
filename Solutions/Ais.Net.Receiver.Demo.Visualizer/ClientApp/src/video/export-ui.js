/**
 * Export UI: button, progress bar, and configuration.
 */
export class ExportUI {
  constructor(onExport, onCancel) {
    this.onExport = onExport;
    this.onCancel = onCancel;
    this.exporting = false;
    this._createElements();
  }

  _createElements() {
    // Export button (top-right, below time display)
    this.container = document.createElement('div');
    this.container.id = 'export-ui';
    this.container.innerHTML = `
      <button id="btn-export" title="Export video (WebM)">Export Video</button>
      <div id="export-progress" style="display:none;">
        <div id="export-progress-bar"></div>
        <span id="export-progress-text">0%</span>
        <button id="btn-cancel-export" title="Cancel export">Cancel</button>
      </div>
    `;
    document.body.appendChild(this.container);

    // Add styles
    const style = document.createElement('style');
    style.textContent = `
      #export-ui {
        position: absolute;
        top: 56px;
        right: 16px;
        z-index: 10;
      }
      #btn-export {
        background: rgba(0, 0, 0, 0.75);
        border: 1px solid rgba(255, 255, 255, 0.2);
        color: #fff;
        padding: 8px 16px;
        border-radius: 6px;
        cursor: pointer;
        font-size: 13px;
        backdrop-filter: blur(8px);
        transition: background 0.15s;
      }
      #btn-export:hover { background: rgba(60, 120, 200, 0.6); }
      #btn-export:disabled { opacity: 0.4; cursor: not-allowed; }
      #export-progress {
        background: rgba(0, 0, 0, 0.85);
        border-radius: 6px;
        padding: 10px 14px;
        display: flex;
        align-items: center;
        gap: 10px;
        backdrop-filter: blur(8px);
      }
      #export-progress-bar {
        width: 120px;
        height: 6px;
        background: rgba(255, 255, 255, 0.1);
        border-radius: 3px;
        overflow: hidden;
        position: relative;
      }
      #export-progress-bar::after {
        content: '';
        position: absolute;
        left: 0;
        top: 0;
        height: 100%;
        width: var(--progress, 0%);
        background: #4af;
        border-radius: 3px;
        transition: width 0.1s;
      }
      #export-progress-text {
        color: #aaa;
        font-size: 12px;
        min-width: 32px;
      }
      #btn-cancel-export {
        background: rgba(255, 80, 80, 0.3);
        border: 1px solid rgba(255, 80, 80, 0.4);
        color: #fff;
        padding: 4px 10px;
        border-radius: 4px;
        cursor: pointer;
        font-size: 11px;
      }
    `;
    document.head.appendChild(style);

    // Bind events
    document.getElementById('btn-export').addEventListener('click', () => {
      if (!this.exporting) this._startExport();
    });
    document.getElementById('btn-cancel-export').addEventListener('click', () => {
      this.onCancel();
      this._reset();
    });
  }

  _startExport() {
    this.exporting = true;
    document.getElementById('btn-export').style.display = 'none';
    document.getElementById('export-progress').style.display = 'flex';
    this.onExport();
  }

  updateProgress(fraction) {
    const pct = Math.round(fraction * 100);
    document.getElementById('export-progress-bar').style.setProperty('--progress', `${pct}%`);
    document.getElementById('export-progress-text').textContent = `${pct}%`;
  }

  _reset() {
    this.exporting = false;
    document.getElementById('btn-export').style.display = '';
    document.getElementById('export-progress').style.display = 'none';
    this.updateProgress(0);
  }

  complete() {
    this._reset();
  }
}
