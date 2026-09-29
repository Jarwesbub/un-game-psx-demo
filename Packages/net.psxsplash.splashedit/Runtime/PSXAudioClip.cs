using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// Pre-converted audio clip data ready for splashpack serialization.
    /// </summary>
    public struct AudioClipExport
    {
        public byte[] adpcmData;
        public int sampleRate;
        public bool loop;
        public string clipName;
    }

    /// <summary>
    /// Attach to a GameObject to include an audio clip in the PS1 build.
    /// At export time, the AudioClip is converted to SPU ADPCM and packed
    /// into the splashpack for runtime loading.
    /// </summary>
    [AddComponentMenu("PSX/PSX Audio Clip")]
    [Icon("Packages/net.psxsplash.splashedit/Icons/PSXAudioClip.png")]
    public class PSXAudioClip : MonoBehaviour
    {
        [Tooltip("Name used to identify this clip in Lua (Audio.Play(\"name\"))." )]
        public string ClipName = "";

        [Tooltip("Unity AudioClip to convert to PS1 SPU ADPCM format.")]
        public AudioClip Clip;

        [Tooltip("Target sample rate for the PS1 (lower = smaller, max 44100).")]
        [Range(8000, 44100)]
        public int SampleRate = 22050;

        [Tooltip("Whether this clip should loop when played.")]
        public bool Loop = false;

        [Tooltip("Default playback volume (0-127).")]
        [Range(0, 127)]
        public int DefaultVolume = 100;
    }
}
