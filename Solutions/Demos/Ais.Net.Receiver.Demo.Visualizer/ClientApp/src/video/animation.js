/**
 * Defines the animation sequence for video export.
 * Maps video time to simulation time linearly.
 */
export function createAnimationTimeline(metadata, videoDurationSeconds) {
  const {timeRange, baseEpoch} = metadata;
  const simDuration = timeRange.end - timeRange.start;

  return {
    simDuration,
    videoDurationSeconds,
    timeScale: simDuration / videoDurationSeconds,

    // Convert video time (seconds) to simulation time
    videoToSimTime(videoTimeSec) {
      const frac = videoTimeSec / videoDurationSeconds;
      return timeRange.start + frac * simDuration;
    }
  };
}
