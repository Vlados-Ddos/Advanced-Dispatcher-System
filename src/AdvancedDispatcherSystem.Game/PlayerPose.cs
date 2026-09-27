using AdvancedDispatcherSystem.Core;
using UnityEngine;

namespace AdvancedDispatcherSystem.Game
{
    public static class PlayerPose
    {
        // OccupiedCar/PlayerManager.Car is the game's actual occupancy link.
        // Capture both transforms together; never attach to the nearest train.
        public static PlayerState Attach(PlayerState state, TrainCar car)
        {
            if (car == null) { state.car = null; state.carPoseKnown = false; state.carX = state.carZ = state.carYaw = 0; return state; }
            var center = car.transform.TransformPoint(car.Bounds.center) - WorldMover.currentMove;
            double angle = car.transform.eulerAngles.y * System.Math.PI / 180;
            double dx = state.x - center.x, dz = state.z - center.z;
            state.car = car.CarGUID;
            state.carX = dx * System.Math.Cos(angle) - dz * System.Math.Sin(angle);
            state.carZ = dx * System.Math.Sin(angle) + dz * System.Math.Cos(angle);
            state.carYaw = Mathf.DeltaAngle(car.transform.eulerAngles.y, (float)state.yaw);
            state.carPoseKnown = true;
            return state;
        }
    }
}
