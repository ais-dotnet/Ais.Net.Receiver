import {MapboxOverlay} from '@deck.gl/mapbox';
import maplibregl from 'maplibre-gl';
import 'maplibre-gl/dist/maplibre-gl.css';

import {INITIAL_VIEW_STATE, loadConfig} from './config.js';
import {loadVesselData} from './utils/data-loader.js';
import {LiveVesselSource} from './live/nats-source.js';
import {formatTimestamp} from './utils/time-utils.js';
import {createTrackLayer} from './layers/track-layer.js';
import {createTripsLayer} from './layers/trips-layer.js';
import {createPositionLayer} from './layers/position-layer.js';
import {createLabelLayer} from './layers/label-layer.js';
import {TimeControls} from './ui/time-controls.js';
import {getTooltip} from './ui/tooltip.js';
import {VideoExporter, downloadBlob} from './video/encoder.js';
import {ExportUI} from './video/export-ui.js';

// State
let vesselData = null;
let metadata = null;
let overlay = null;
let map = null;
let currentTime = 0;
let videoExporter = null;
let liveSource = null;

// Layer visibility toggles
const layerVisibility = {
  tracks: true,
  trails: true,
  dots: true,
  labels: true
};

// DOM elements
const timeDisplay = document.getElementById('time-display');
const activeCount = document.getElementById('active-count');
const loadingEl = document.getElementById('loading');

async function init() {
  // Parse URL parameters for auto-record mode
  const params = new URLSearchParams(window.location.search);
  const autoRecord = params.has('record');
  const recordDuration = Number(params.get('duration')) || 60;
  const recordFramerate = Number(params.get('framerate')) || 30;

  // The host decides whether this page shows the live feed or a recording, and supplies the basemap
  // and (for live) the websocket the browser subscribes on.
  const config = await loadConfig();

  // Create MapLibre map with preserveDrawingBuffer for video capture
  map = new maplibregl.Map({
    container: 'map',
    style: config.basemapStyle,
    center: [INITIAL_VIEW_STATE.longitude, INITIAL_VIEW_STATE.latitude],
    zoom: INITIAL_VIEW_STATE.zoom,
    pitch: INITIAL_VIEW_STATE.pitch,
    bearing: INITIAL_VIEW_STATE.bearing,
    preserveDrawingBuffer: true
  });

  // Create deck.gl overlay
  overlay = new MapboxOverlay({
    interleaved: false,
    getTooltip: autoRecord ? undefined : getTooltip,
    layers: []
  });

  map.addControl(overlay);

  if (config.source === 'live') {
    await initLive(config);
    return;
  }

  // Load data
  const data = await loadVesselData();

  vesselData = data.vessels;
  metadata = data.metadata;

  if (autoRecord) {
    // Hide all UI overlays for a clean canvas
    for (const id of ['controls', 'time-display', 'vessel-count', 'export-ui']) {
      const el = document.getElementById(id);
      if (el) el.style.display = 'none';
    }

    // Hide loading overlay
    loadingEl.classList.add('hidden');

    // Initial render
    currentTime = metadata.timeRange.start;
    updateLayers();

    // Start recording automatically
    const exportLayerFactory = createExportLayerFactory(vesselData);
    videoExporter = new VideoExporter({map, getLayersForTime: exportLayerFactory, metadata, overlay});

    document.title = 'RECORDING 0%';

    videoExporter.start({
      durationSeconds: recordDuration,
      framerate: recordFramerate,
      onProgress: (fraction) => {
        document.title = `RECORDING ${Math.round(fraction * 100)}%`;
      },
      onComplete: (blob) => {
        downloadBlob(blob, 'vessel-movements.webm');
        document.title = 'RECORDING_DONE';
      }
    });
  } else {
    // Normal interactive mode
    map.addControl(new maplibregl.NavigationControl(), 'top-left');

    // Set up time controls
    const controls = new TimeControls(metadata.timeRange, (t) => {
      currentTime = t;
      updateLayers();
    });

    // Set up layer toggle checkboxes
    setupLayerToggles();

    // Set up video export
    setupVideoExport(controls);

    // Hide loading overlay
    loadingEl.classList.add('hidden');

    // Initial render
    currentTime = metadata.timeRange.start;
    updateLayers();
  }
}

/**
 * Live mode. The vessel list is fed by NATS instead of a recording, so there is no timeline to
 * scrub: playback controls are hidden and the clock simply advances in real time. Everything below
 * that - the layers, the tooltip, the toggles - is the same code the replay uses, because the live
 * source maintains vessels in the same shape.
 */
async function initLive(config) {
  map.addControl(new maplibregl.NavigationControl(), 'top-left');

  liveSource = new LiveVesselSource({
    url: config.nats.url,
    subject: config.nats.subject,
    user: config.nats.user,
    pass: config.nats.pass,
    inactivitySeconds: config.vesselInactivitySeconds
  });

  await liveSource.start(err => {
    console.error('Live vessel stream failed:', err);
    loadingEl.textContent = `Live stream error: ${err.message}`;
    loadingEl.classList.remove('hidden');
  });

  document.getElementById('controls')?.style.setProperty('display', 'none');
  setupLayerToggles();
  loadingEl.classList.add('hidden');

  let lastPrune = 0;

  const frame = () => {
    vesselData = liveSource.vesselList;
    metadata = liveSource.metadata;
    currentTime = liveSource.currentTime;

    // Pruning walks every vessel, so it runs on a slow cadence rather than every frame.
    if (currentTime - lastPrune > 30) {
      liveSource.prune();
      lastPrune = currentTime;
    }

    updateLayers();
    requestAnimationFrame(frame);
  };

  requestAnimationFrame(frame);
}

function getLayersForTime(t) {
  const {layer: posLayer, activeVessels} = createPositionLayer(vesselData, t, true);
  return [
    createTrackLayer(vesselData, true),
    createTripsLayer(vesselData, t, true),
    posLayer,
    createLabelLayer(activeVessels, true)
  ];
}

/**
 * Creates a layer factory optimised for export: the static track layer is
 * built once and reused for every frame instead of being recreated.
 */
function createExportLayerFactory(vessels) {
  const cachedTrackLayer = createTrackLayer(vessels, true);
  return function getExportLayersForTime(t) {
    const {layer: posLayer, activeVessels} = createPositionLayer(vessels, t, true);
    return [
      cachedTrackLayer,
      createTripsLayer(vessels, t, true),
      posLayer,
      createLabelLayer(activeVessels, true)
    ];
  };
}

function updateLayers() {
  if (!overlay || !vesselData) return;

  // Update time display
  timeDisplay.textContent = formatTimestamp(metadata.baseEpoch, currentTime);

  // Build position layer (also computes active vessels)
  const {layer: posLayer, activeCount: count} = createPositionLayer(
    vesselData, currentTime, layerVisibility.dots
  );

  // Get active vessels (those with _currentPos set)
  const activeVessels = vesselData.filter(v => v._currentPos);

  activeCount.textContent = count;

  const layers = [
    createTrackLayer(vesselData, layerVisibility.tracks),
    createTripsLayer(vesselData, currentTime, layerVisibility.trails),
    posLayer,
    createLabelLayer(activeVessels, layerVisibility.labels)
  ];

  overlay.setProps({layers});
}

function setupLayerToggles() {
  const toggleMap = {
    'tog-tracks': 'tracks',
    'tog-trails': 'trails',
    'tog-dots': 'dots',
    'tog-labels': 'labels'
  };

  for (const [elemId, key] of Object.entries(toggleMap)) {
    const el = document.getElementById(elemId);
    el.addEventListener('change', () => {
      layerVisibility[key] = el.checked;
      updateLayers();
    });
  }
}

function setupVideoExport(controls) {
  const exportLayerFactory = createExportLayerFactory(vesselData);
  videoExporter = new VideoExporter({
    map,
    getLayersForTime: exportLayerFactory,
    metadata,
    overlay
  });

  const exportUI = new ExportUI(
    // onExport
    () => {
      // Pause the playback during export
      if (controls.playing) controls.togglePlay();

      videoExporter.start({
        durationSeconds: 60,
        framerate: 30,
        speed: controls.speed,
        startTime: controls.currentTime,
        onProgress: (fraction) => {
          exportUI.updateProgress(fraction);
        },
        onComplete: (blob) => {
          exportUI.complete();
          downloadBlob(blob, 'vessel-movements.webm');
        }
      });
    },
    // onCancel
    () => {
      videoExporter.stop();
    }
  );
}

init().catch(err => {
  console.error('Failed to initialize:', err);
  loadingEl.textContent = `Error: ${err.message}`;
});
