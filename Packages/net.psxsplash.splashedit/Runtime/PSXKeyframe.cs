using System;
using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// A single keyframe in a cutscene track.
    /// Value interpretation depends on track type:
    ///   CameraPosition / ObjectPosition: Unity world-space position (x, y, z)
    ///   CameraRotation: Euler angles in degrees (x=pitch, y=yaw, z=roll)
    ///   ObjectRotation: y component = rotation in degrees
    ///   ObjectActive: x component = 0.0 (inactive) or 1.0 (active)
    ///   CameraH: x component = H register value (projection distance, 1-1024)
    /// </summary>
    [Serializable]
    public class PSXKeyframe
    {
        [Tooltip("Frame number (0 = start of cutscene). At 30fps, frame 30 = 1 second.")]
        public int Frame;

        [Tooltip("Keyframe value. Interpretation depends on track type.")]
        public Vector3 Value;

        [Tooltip("Interpolation mode from this keyframe to the next.")]
        public PSXInterpMode Interp = PSXInterpMode.Linear;
    }
}
