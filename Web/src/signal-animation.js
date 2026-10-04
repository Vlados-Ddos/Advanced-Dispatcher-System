// LampControl exposes a sampled target, not a blink epoch/frequency. Its
// oscillator depends on local emission smoothing and distance optimisation.
// The dispatch map therefore animates the logical blinking aspect at a fixed
// one-second cycle, using one server-aligned monotonic clock for every head.
// Physical sample data remains available when rendering a static snapshot.
export function lampLit(lamp, time) {
  if(!lamp.on)return false;
  if(lamp.blinking && Number.isFinite(time))return Math.floor(time/500)%2===0;
  return lamp.phaseKnown?lamp.phaseOn:lamp.brightnessKnown?lamp.brightness>=.5:!lamp.blinking;
}
export function signalAnimation(renderer, now) {
  if(renderer.store.stale)return;
  const epoch=renderer.store.topology?.epoch;
  if(renderer.signalClockEpoch!==epoch || !Number.isFinite(renderer.signalClockOrigin)) {
    renderer.signalClockEpoch=epoch;
    renderer.signalClockOrigin=Date.now()+(renderer.store.serverOffset||0)-now;
  }
  renderer.signalTime=renderer.signalClockOrigin+now;
  const phase=Math.floor(renderer.signalTime/500);
  if(phase===renderer.signalAnimationPhase)return;
  renderer.signalAnimationPhase=phase;
  // Only visible, previously painted signals need an animation redraw.
  for(const id of renderer.signalPaintBounds?.keys()||[]) {
    const signal=renderer.store.signals.get(id);
    if(signal?.lampLayout?.some(l=>l.indicationBlinking||l.blinking) || signal?.blinkingLamps?.some(Boolean)) {
      renderer.infrastructureChanges ||= new Set();
      renderer.infrastructureChanges.add(id);
    }
  }
}
