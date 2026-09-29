#if UNITY_EDITOR
using UnityEditor;
#endif
using System.Collections.Generic;
using UnityEngine;
using System.Linq;

namespace SplashEdit.RuntimeCode
{
#if UNITY_EDITOR
    public static class DataStorage
    {
        private static readonly string psxDataPath = "Assets/PSXData.asset";

        /// <summary>
        /// Loads stored PSX data from the asset.
        /// </summary>
        public static PSXData LoadData(out Vector2 selectedResolution, out bool dualBuffering, out bool verticalLayout, out List<ProhibitedArea> prohibitedAreas)
        {
            var _psxData = AssetDatabase.LoadAssetAtPath<PSXData>(psxDataPath);
            if (!_psxData)
            {
                _psxData = ScriptableObject.CreateInstance<PSXData>();
                AssetDatabase.CreateAsset(_psxData, psxDataPath);
                AssetDatabase.SaveAssets();
            }

            selectedResolution = _psxData.OutputResolution;
            dualBuffering = _psxData.DualBuffering;
            verticalLayout = _psxData.VerticalBuffering;
            prohibitedAreas = _psxData.ProhibitedAreas;
            return _psxData;
        }
        public static PSXData LoadData()
        {
            PSXData psxData = AssetDatabase.LoadAssetAtPath<PSXData>(psxDataPath);

            if (!psxData)
            {
                psxData = ScriptableObject.CreateInstance<PSXData>();
                AssetDatabase.CreateAsset(psxData, psxDataPath);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }

            return psxData;
        }

        public static void StoreData(PSXData psxData)
        {
            if (psxData != null)
            {
                EditorUtility.SetDirty(psxData);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
            }
        }
    }
#endif

    /// <summary>
    /// Represents a prohibited area in PlayStation 2D VRAM where textures should not be packed.
    /// This class provides conversion methods to and from Unity's Rect structure.
    /// </summary>
    public class ProhibitedArea
    {
        // X and Y coordinates of the prohibited area in VRAM.
        public int X;
        public int Y;
        // Width and height of the prohibited area.
        public int Width;
        public int Height;

        /// <summary>
        /// Creates a ProhibitedArea instance from a Unity Rect.
        /// The floating-point values of the Rect are rounded to the nearest integer.
        /// </summary>
        /// <param name="rect">The Unity Rect representing the prohibited area.</param>
        /// <returns>A new ProhibitedArea with integer dimensions.</returns>
        public static ProhibitedArea FromUnityRect(Rect rect)
        {
            return new ProhibitedArea
            {
                X = Mathf.RoundToInt(rect.x),
                Y = Mathf.RoundToInt(rect.y),
                Width = Mathf.RoundToInt(rect.width),
                Height = Mathf.RoundToInt(rect.height)
            };
        }

        /// <summary>
        /// Converts the ProhibitedArea back into a Unity Rect.
        /// </summary>
        /// <returns>A Unity Rect with the same area as defined by this ProhibitedArea.</returns>
        public Rect ToUnityRect()
        {
            return new Rect(X, Y, Width, Height);
        }
    }
    /// <summary>
    /// A utility class containing methods for converting Unity-specific data formats to PSX-compatible formats.
    /// This includes converting coordinates and rotations to PSX's 3.12 fixed-point format.
    /// </summary>
    public static class PSXTrig
    {
        /// <summary>
        /// Converts a floating-point coordinate to a PSX-compatible 4.12 fixed-point format (int16).
        /// The value is divided by GTEScaling, then scaled to 4.12 fixed-point and clamped to int16 range.
        /// Usable range: [-8.0, ~8.0) in GTE units (i.e. [-8*GTEScaling, 8*GTEScaling) in world units).
        /// Use this for mesh vertex positions (local space) and other data that fits in int16.
        /// </summary>
        /// <param name="value">The coordinate value in world units.</param>
        /// <param name="GTEScaling">World-to-GTE scaling factor (default 1.0f). Value is divided by this.</param>
        /// <returns>The converted coordinate in 4.12 fixed-point format as a 16-bit signed integer.</returns>
        public static short ConvertCoordinateToPSX(float value, float GTEScaling = 1.0f)
        {
            int fixedValue = Mathf.RoundToInt((value / GTEScaling) * 4096.0f);
            return (short)Mathf.Clamp(fixedValue, -32768, 32767);
        }

        /// <summary>
        /// Converts a quaternion rotation to a PSX-compatible 3x3 rotation matrix.
        /// The matrix is adjusted for the difference in the Y-axis orientation between Unity (Y-up) and PSX (Y-down).
        /// Each matrix element is converted to a 3.12 fixed-point format.
        /// </summary>
        /// <param name="rotation">The quaternion representing the rotation to convert.</param>
        /// <returns>A 3x3 matrix representing the PSX-compatible rotation.</returns>
        public static int[,] ConvertRotationToPSXMatrix(Quaternion rotation)
        {
            // Standard quaternion-to-matrix conversion.
            float x = rotation.x, y = rotation.y, z = rotation.z, w = rotation.w;

            float m00 = 1f - 2f * (y * y + z * z);
            float m01 = 2f * (x * y - z * w);
            float m02 = 2f * (x * z + y * w);

            float m10 = 2f * (x * y + z * w);
            float m11 = 1f - 2f * (x * x + z * z);
            float m12 = 2f * (y * z - x * w);

            float m20 = 2f * (x * z - y * w);
            float m21 = 2f * (y * z + x * w);
            float m22 = 1f - 2f * (x * x + y * y);

            // Apply Y-axis flip to match the PSX's Y-down convention.
            // This replicates the behavior of:
            // { m00, -m01, m02 },
            // { -m10, m11, -m12 },
            // { m20, -m21, m22 }
            float[,] fixedMatrix = new float[3, 3]
            {
        { m00, -m01, m02 },
        { -m10, m11, -m12 },
        { m20, -m21, m22 }
            };

            // Convert to PSX fixed-point format.
            int[,] psxMatrix = new int[3, 3];
            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    psxMatrix[i, j] = ConvertToFixed12(fixedMatrix[i, j]);
                }
            }

            return psxMatrix;
        }

        /// <summary>
        /// Converts a floating-point value to 4.12 fixed-point format (int16).
        /// Suitable for values in [-8.0, ~8.0) range, such as rotation matrix elements or normals.
        /// </summary>
        /// <param name="value">The floating-point value to convert.</param>
        /// <returns>The converted value in 4.12 fixed-point format as a 16-bit signed integer.</returns>
        public static short ConvertToFixed12(float value)
        {
            int fixedValue = Mathf.RoundToInt(value * 4096.0f);
            return (short)Mathf.Clamp(fixedValue, -32768, 32767);
        }

        /// <summary>
        /// Converts a GTE-space value to 20.12 fixed-point format (int32).
        /// Use this for world-space positions, collision AABBs, BVH bounds, and any data
        /// that needs the full int32 range. Caller must divide by GTEScaling BEFORE calling,
        /// i.e. pass (worldValue / GTEScaling). This matches ConvertCoordinateToPSX's
        /// coordinate space but returns int32 instead of clamping to int16.
        /// Usable range: approximately [-524288.0, 524288.0).
        /// </summary>
        /// <param name="value">The value to convert (in GTE units, i.e. worldValue / GTEScaling).</param>
        /// <returns>The converted value in 20.12 fixed-point format as a 32-bit signed integer.</returns>
        public static int ConvertWorldToFixed12(float value)
        {
            long fixedValue = (long)Mathf.RoundToInt(value * 4096.0f);
            return (int)Mathf.Clamp(fixedValue, int.MinValue, int.MaxValue);
        }
    }
    /// <summary>
    /// Represents the attributes of a texture page in the PSX graphics system.
    /// Provides methods for setting various properties such as the page coordinates, transparency type, color mode, dithering, and display area.
    /// </summary>
    public struct TPageAttr
    {
        public ushort info; // Stores the packed attribute information as a 16-bit unsigned integer.

        /// <summary>
        /// Sets the X-coordinate of the texture page.
        /// The lower 4 bits of the 'info' field are used to store the X value.
        /// </summary>
        /// <param name="x">The X-coordinate value (0 to 15).</param>
        /// <returns>The updated TPageAttr instance.</returns>
        public TPageAttr SetPageX(byte x)
        {
            info &= 0xFFF0; // Clear lower 4 bits
            x &= 0x0F; // Ensure only lower 4 bits are used
            info |= x;
            return this;
        }

        /// <summary>
        /// Sets the Y-coordinate of the texture page.
        /// The 4th bit of the 'info' field is used to store the Y value (0 or 1).
        /// </summary>
        /// <param name="y">The Y-coordinate value (0 or 1).</param>
        /// <returns>The updated TPageAttr instance.</returns>
        public TPageAttr SetPageY(byte y)
        {
            info &= 0xFFEF; // Clear bit 4
            y &= 0x01; // Ensure only lower 1 bit is used
            info |= (ushort)(y << 4);
            return this;
        }

        /// <summary>
        /// Sets the transparency type of the texture page.
        /// The transparency type is stored in bits 5 and 6 of the 'info' field.
        /// </summary>
        /// <param name="trans">The transparency type to set.</param>
        /// <returns>The updated TPageAttr instance.</returns>
        public TPageAttr Set(SemiTrans trans)
        {
            info &= 0xFF9F; // Clear bits 5 and 6
            uint t = (uint)trans;
            info |= (ushort)(t << 5);
            return this;
        }

        /// <summary>
        /// Sets the color mode of the texture page.
        /// The color mode is stored in bits 7 and 8 of the 'info' field.
        /// </summary>
        /// <param name="mode">The color mode to set (4-bit, 8-bit, or 16-bit).</param>
        /// <returns>The updated TPageAttr instance.</returns>
        public TPageAttr Set(ColorMode mode)
        {
            info &= 0xFE7F; // Clear bits 7 and 8
            uint m = (uint)mode;
            info |= (ushort)(m << 7);
            return this;
        }

        /// <summary>
        /// Enables or disables dithering for the texture page.
        /// Dithering is stored in bit 9 of the 'info' field.
        /// </summary>
        /// <param name="dithering">True to enable dithering, false to disable it.</param>
        /// <returns>The updated TPageAttr instance.</returns>
        public TPageAttr SetDithering(bool dithering)
        {
            if (dithering)
                info |= 0x0200; // Set bit 9 to enable dithering
            else
                info &= 0xFDFF; // Clear bit 9 to disable dithering
            return this;
        }

        /// <summary>
        /// Disables the display area for the texture page.
        /// This will clear bit 10 of the 'info' field.
        /// </summary>
        /// <returns>The updated TPageAttr instance.</returns>
        public TPageAttr DisableDisplayArea()
        {
            info &= 0xFBFF; // Clear bit 10
            return this;
        }

        /// <summary>
        /// Enables the display area for the texture page.
        /// This will set bit 10 of the 'info' field.
        /// </summary>
        /// <returns>The updated TPageAttr instance.</returns>
        public TPageAttr EnableDisplayArea()
        {
            info |= 0x0400; // Set bit 10 to enable display area
            return this;
        }

        /// <summary>
        /// Returns a string representation of the TPageAttr instance, showing the 'info' value in hexadecimal.
        /// </summary>
        /// <returns>A string representing the 'info' value in hexadecimal format.</returns>
        public override string ToString() => $"Info: 0x{info:X4}";

        // Define the enums for SemiTrans and ColorMode (assuming their values)

        /// <summary>
        /// Defines the transparency types for a texture page.
        /// </summary>
        public enum SemiTrans : uint
        {
            None = 0,
            Type1 = 1,
            Type2 = 2,
            Type3 = 3
        }

        /// <summary>
        /// Defines the color modes for a texture page.
        /// </summary>
        public enum ColorMode : uint
        {
            Mode4Bit = 0,
            Mode8Bit = 1,
            Mode16Bit = 2
        }
    }


    public static class Utils
    {
        private static int MaxTextureSize => 256;

        /// <summary>
        /// If a directory contains exactly one subdirectory (common after archive extraction),
        /// flatten its contents up one level and remove the nested directory.
        /// </summary>
        public static void FixNestedDirectory(string dir)
        {
            var subdirs = System.IO.Directory.GetDirectories(dir);
            if (subdirs.Length == 1)
            {
                string nested = subdirs[0];
                foreach (string file in System.IO.Directory.GetFiles(nested))
                {
                    string dest = System.IO.Path.Combine(dir, System.IO.Path.GetFileName(file));
                    if (!System.IO.File.Exists(dest)) System.IO.File.Move(file, dest);
                }
                foreach (string sub in System.IO.Directory.GetDirectories(nested))
                {
                    string dest = System.IO.Path.Combine(dir, System.IO.Path.GetFileName(sub));
                    if (!System.IO.Directory.Exists(dest)) System.IO.Directory.Move(sub, dest);
                }
                try { System.IO.Directory.Delete(nested, true); } catch { }
            }
        }

        public static (Rect, Rect) BufferForResolution(Vector2 selectedResolution, bool verticalLayout, Vector2 offset = default)
        {
            if (offset == default)
            {
                offset = Vector2.zero;
            }
            Rect buffer1 = new Rect(offset.x, offset.y, selectedResolution.x, selectedResolution.y);
            Rect buffer2 = verticalLayout ? new Rect(offset.x, 256, selectedResolution.x, selectedResolution.y)
                                          : new Rect(offset.x + selectedResolution.x, offset.y, selectedResolution.x, selectedResolution.y);
            return (buffer1, buffer2);
        }

        public static TPageAttr.ColorMode ToColorMode(this PSXBPP depth)
        {
            return depth switch
            {
                PSXBPP.TEX_4BIT => TPageAttr.ColorMode.Mode4Bit,
                PSXBPP.TEX_8BIT => TPageAttr.ColorMode.Mode8Bit,
                PSXBPP.TEX_16BIT => TPageAttr.ColorMode.Mode16Bit,
                _ => throw new System.NotImplementedException(),
            };
        }

        public static byte ColorUnityToPSX(float v) => (byte)(Mathf.Clamp(v * 255, 0, 255));

#if UNITY_EDITOR
        public static void SetTextureImporterFormat(Texture2D texture, bool isReadable)
        {
            if (texture == null)
            {
                return;
            }
            string assetPath = AssetDatabase.GetAssetPath(texture);
            var tImporter = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            bool needReimport = false;
            if (tImporter != null)
            {
                if (tImporter.maxTextureSize > MaxTextureSize)
                {
                    tImporter.maxTextureSize = MaxTextureSize;
                    needReimport = true;
                }
                if (tImporter.isReadable != isReadable)
                {
                    tImporter.isReadable = isReadable;
                    needReimport = true;
                }
                if (needReimport)
                {
                    AssetDatabase.ImportAsset(assetPath);
                    AssetDatabase.Refresh();
                }
            }
        }
#endif
    }

}

