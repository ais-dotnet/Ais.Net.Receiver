// Visual constants. These stay baked into the bundle: the layer modules import them at load time, and
// they are look-and-feel rather than deployment configuration.
export const INITIAL_VIEW_STATE = {
  longitude: 10.0,
  latitude: 58.0,
  zoom: 7.5,
  pitch: 0,
  bearing: 0
};

export const TRAIL_LENGTH_SECONDS = 600; // 10 minutes of trail
export const ANIMATION_SPEEDS = [0.5, 1, 2, 5, 10, 20, 30, 60, 120, 240, 600];
export const DEFAULT_SPEED_INDEX = 4; // 10x

export const TRACK_OPACITY = 40;
export const TRAIL_WIDTH = 3;
export const DOT_RADIUS = 4;
export const LABEL_SIZE = 12;

// Replay data comes from the host, which reads it from a file or an Azure blob and caches the result.
export const TRACKS_URL = '/api/tracks';

/**
 * Fetches the runtime configuration. This has to come from the server rather than the bundle: in live
 * mode the NATS websocket port is assigned by the AppHost when it starts, so it cannot be known when
 * the bundle is built.
 */
export async function loadConfig() {
  const response = await fetch('/api/config');
  if (!response.ok) throw new Error(`Failed to load configuration: ${response.status}`);
  return response.json();
}
