using System.Collections.Generic;
using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// A cutscene asset containing keyframed tracks and audio events.
    /// Create via right-click → Create → PSX → Cutscene Clip.
    /// Reference these assets anywhere in the project; the exporter collects
    /// all PSXCutsceneClip assets via Resources.FindObjectsOfTypeAll.
    /// </summary>
    [CreateAssetMenu(fileName = "NewCutscene", menuName = "PSX/Cutscene Clip", order = 100)]
    [Icon("Packages/net.psxsplash.splashedit/Icons/PSXCutsceneClip.png")]
    public class PSXCutsceneClip : ScriptableObject
    {
        [Tooltip("Name used to reference this cutscene from Lua (max 24 chars). Must be unique per scene.")]
        public string CutsceneName = "cutscene";

        [Tooltip("Total duration in frames at 30fps. E.g. 90 = 3 seconds.")]
        public int DurationFrames = 90;

        [Tooltip("Tracks driving properties over time.")]
        public List<PSXCutsceneTrack> Tracks = new List<PSXCutsceneTrack>();

        [Tooltip("Audio events triggered at specific frames.")]
        public List<PSXAudioEvent> AudioEvents = new List<PSXAudioEvent>();

        [Tooltip("Skinned mesh animation events triggered at specific frames.")]
        public List<PSXSkinAnimEvent> SkinAnimEvents = new List<PSXSkinAnimEvent>();
    }
}
