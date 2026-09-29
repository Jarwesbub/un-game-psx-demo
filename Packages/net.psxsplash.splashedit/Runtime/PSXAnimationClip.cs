using System.Collections.Generic;
using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// An animation asset containing keyframed object/UI tracks (no camera).
    /// Multiple animations can play simultaneously at runtime.
    /// Create via right-click -> Create -> PSX -> Animation Clip.
    /// </summary>
    [CreateAssetMenu(fileName = "NewAnimation", menuName = "PSX/Animation Clip", order = 101)]
    [Icon("Packages/net.psxsplash.splashedit/Icons/PSXAnimationClip.png")]
    public class PSXAnimationClip : ScriptableObject
    {
        [Tooltip("Name used to reference this animation from Lua (max 24 chars). Must be unique per scene.")]
        public string AnimationName = "animation";

        [Tooltip("Total duration in frames at 30fps. E.g. 90 = 3 seconds.")]
        public int DurationFrames = 90;

        [Tooltip("Tracks driving object/UI properties over time. Camera tracks are not allowed.")]
        public List<PSXCutsceneTrack> Tracks = new List<PSXCutsceneTrack>();

        [Tooltip("Skinned mesh animation events triggered at specific frames.")]
        public List<PSXSkinAnimEvent> SkinAnimEvents = new List<PSXSkinAnimEvent>();
    }
}
