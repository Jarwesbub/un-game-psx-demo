namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// UI element types matching the C++ UIElementType enum.
    /// Values must stay in sync with uisystem.hh.
    /// </summary>
    public enum PSXUIElementType : byte
    {
        Image    = 0,
        Box      = 1,
        Text     = 2,
        Progress = 3,
        Line     = 4,
    }
}
