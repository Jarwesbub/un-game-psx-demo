using System;
using System.Collections.Generic;
using UnityEngine;

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// A single track within a cutscene, driving one property on one target.
    /// </summary>
    [Serializable]
    public class PSXCutsceneTrack
    {
        [Tooltip("What property this track drives.")]
        public PSXTrackType TrackType;

        [Tooltip("Target GameObject name (must match a PSXObjectExporter). Leave empty for camera/UI tracks.")]
        public string ObjectName = "";

        [Tooltip("For UI tracks: canvas name (e.g. 'hud'). Used by UICanvasVisible and to resolve elements.")]
        public string UICanvasName = "";

        [Tooltip("For UI element tracks: element name within the canvas. Used by UIElementVisible, UIProgress, UIPosition, UIColor.")]
        public string UIElementName = "";

        [Tooltip("Keyframes for this track. Sort by frame number.")]
        public List<PSXKeyframe> Keyframes = new List<PSXKeyframe>();

        /// <summary>Returns true if this track type targets a UI canvas or element.</summary>
        public bool IsUITrack => TrackType >= PSXTrackType.UICanvasVisible && TrackType <= PSXTrackType.UIColor;

        /// <summary>Returns true if this track type targets a UI element (not just a canvas).</summary>
        public bool IsUIElementTrack => TrackType >= PSXTrackType.UIElementVisible && TrackType <= PSXTrackType.UIColor;

        /// <summary>Returns true if this is a camera track (position, rotation, or H).</summary>
        public bool IsCameraTrack => TrackType == PSXTrackType.CameraPosition ||
                                     TrackType == PSXTrackType.CameraRotation ||
                                     TrackType == PSXTrackType.CameraH;

        /// <summary>Returns true if this is a vibration/rumble track.</summary>
        public bool IsVibrationTrack => TrackType == PSXTrackType.RumbleSmall ||
                                       TrackType == PSXTrackType.RumbleLarge;
    }
}
