using System.Collections.Generic;
using UnityEngine;

namespace SplashEdit.RuntimeCode
{

    [CreateAssetMenu(fileName = "PSXData", menuName = "PSXSplash/PS1 Project Data")]
    [Icon("Packages/net.psxsplash.splashedit/Icons/PSXData.png")]
    public class PSXData : ScriptableObject
    {

        // Texture packing settings
        public Vector2 OutputResolution = new Vector2(320, 240);
        public bool DualBuffering = true;
        public bool VerticalBuffering = true;
        public List<ProhibitedArea> ProhibitedAreas = new List<ProhibitedArea>();

        // Memory card save settings (global, splashpack v21). These are packed
        // into every scene's splashpack so the runtime can build Sony-format
        // save files and show them in the BIOS memory card manager.
        public bool MemCardEnabled = false;
        public string MemCardRegion = "BA";          // BA=America, BE=Europe, BI=Japan
        public string MemCardProduct = "SLUS-00000";  // up to 10 chars
        public string MemCardTitle = "PSXSPLASH SAVE"; // ASCII, up to 32 chars
        public Texture2D[] MemCardIcons = new Texture2D[0]; // 1..3 frames, each 16x16
    }
}