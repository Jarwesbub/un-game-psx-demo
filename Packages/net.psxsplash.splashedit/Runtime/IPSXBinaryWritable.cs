using System.IO;

namespace SplashEdit.RuntimeCode
{
    /// <summary>
    /// Implemented by scene-level data builders that serialize their output
    /// into the splashpack binary stream.
    /// </summary>
    public interface IPSXBinaryWritable
    {
        /// <summary>
        /// Write binary data to the splashpack stream.
        /// </summary>
        /// <param name="writer">The binary writer positioned at the correct offset.</param>
        /// <param name="gteScaling">GTE coordinate scaling factor.</param>
        void WriteToBinary(BinaryWriter writer, float gteScaling);
    }
}
