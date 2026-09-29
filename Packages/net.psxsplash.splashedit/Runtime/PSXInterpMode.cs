namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// Per-keyframe interpolation mode. Must match the C++ InterpMode enum in cutscene.hh.
    /// Packed into the upper 3 bits of the 16-bit frame field on export.
    /// </summary>
    public enum PSXInterpMode : byte
    {
        Linear    = 0,
        Step      = 1,
        EaseIn    = 2,
        EaseOut   = 3,
        EaseInOut = 4,
    }
}
