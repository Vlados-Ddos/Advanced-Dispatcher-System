using System;

namespace AdvancedDispatcherSystem.Signals
{
    // DV Signals only evaluates a controller in its normal update loop while
    // the controller is within 1.5 km of the active camera.  Advanced Dispatch
    // samples the complete signal registry for a remote map, so an MP client
    // otherwise publishes the controller's last (often OFF) aspect forever.
    internal static class SignalRefreshPolicy
    {
        internal const float NativeControllerUpdateDistanceSqr = 2250000f;

        internal static bool NeedsRemoteRefresh(float cameraDistanceSqr, bool multiplayer) =>
            multiplayer && !float.IsNaN(cameraDistanceSqr) && !float.IsInfinity(cameraDistanceSqr) &&
            cameraDistanceSqr >= NativeControllerUpdateDistanceSqr;

        internal static bool Refresh(
            float cameraDistanceSqr,
            bool multiplayer,
            Action updateBlocks,
            Action updateAspects)
        {
            if (!NeedsRemoteRefresh(cameraDistanceSqr, multiplayer)) return false;
            updateBlocks?.Invoke();
            updateAspects?.Invoke();
            return true;
        }
    }
}
