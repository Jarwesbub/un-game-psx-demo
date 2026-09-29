using System;
using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// A frame-based skinned mesh animation trigger within a cutscene or animation.
    /// When playback reaches this frame, the named animation clip starts on the target skinned mesh.
    /// Follows the same pattern as PSXAudioEvent.
    /// </summary>
    [Serializable]
    public class PSXSkinAnimEvent
    {
        [Tooltip("Frame at which to trigger this skinned mesh animation.")]
        public int Frame;

        [Tooltip("Name of the target skinned mesh object (must have a PSXSkinnedObjectExporter).")]
        public string TargetObjectName = "";

        [Tooltip("Name of the animation clip on the target (must match an AnimationClip name on the PSXSkinnedObjectExporter).")]
        public string ClipName = "";

        [Tooltip("Whether the triggered animation should loop.")]
        public bool Loop = false;
    }
}
