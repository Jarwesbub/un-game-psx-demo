namespace SplashEdit.RuntimeCode
{

    // I tried to make this and now I'm scared to delete this.

    /// <summary>
    /// Implemented by MonoBehaviours that participate in the PSX scene export pipeline.
    /// Each exportable object converts its Unity representation into PSX-ready data.
    /// </summary>
    public interface IPSXExportable
    {
        /// <summary>
        /// Convert Unity textures into PSX texture data (palette-quantized, packed).
        /// </summary>
        void CreatePSXTextures2D();

        /// <summary>
        /// Convert the Unity mesh into a PSX-ready triangle list.
        /// </summary>
        /// <param name="gteScaling">GTE coordinate scaling factor.</param>
        void CreatePSXMesh(float gteScaling);
    }
}
