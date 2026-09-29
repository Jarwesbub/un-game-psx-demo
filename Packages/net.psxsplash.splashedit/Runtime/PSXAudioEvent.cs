using System;
using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// A frame-based audio trigger within a cutscene.
    /// When the cutscene reaches this frame, the named audio clip is played.
    /// </summary>
    [Serializable]
    public class PSXAudioEvent
    {
        [Tooltip("Frame at which to trigger this audio clip.")]
        public int Frame;

        [Tooltip("Name of the audio clip (must match a PSXAudioClip ClipName in the scene).")]
        public string ClipName = "";

        [Tooltip("Playback volume (0 = silent, 128 = max).")]
        [Range(0, 128)]
        public int Volume = 100;

        [Tooltip("Stereo pan (0 = hard left, 64 = center, 127 = hard right).")]
        [Range(0, 127)]
        public int Pan = 64;
    }
}
