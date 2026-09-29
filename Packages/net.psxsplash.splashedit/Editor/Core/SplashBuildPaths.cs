using System.IO;
using UnityEngine;

namespace SplashEdit.EditorCode
{
    /// <summary>
    /// Manages all build-related paths for the SplashEdit pipeline.
    /// All output goes outside Assets/ to avoid Unity import overhead.
    /// </summary>
    public static class SplashBuildPaths
    {
        /// <summary>
        /// The build output directory at the Unity project root.
        /// Contains exported splashpacks, manifest, compiled .ps-exe, ISO, build log.
        /// </summary>
        public static string BuildOutputDir =>
            Path.Combine(ProjectRoot, "PSXBuild");

        /// <summary>
        /// The tools directory at the Unity project root.
        /// Contains auto-downloaded tools like PCSX-Redux.
        /// </summary>
        public static string ToolsDir =>
            Path.Combine(ProjectRoot, ".tools");

        /// <summary>
        /// PCSX-Redux install directory inside .tools/.
        /// </summary>
        public static string PCSXReduxDir =>
            Path.Combine(ToolsDir, "pcsx-redux");

        /// <summary>
        /// Platform-specific PCSX-Redux binary path.
        /// </summary>
        public static string PCSXReduxBinary
        {
            get
            {
                switch (Application.platform)
                {
                    case RuntimePlatform.WindowsEditor:
                        return Path.Combine(PCSXReduxDir, "pcsx-redux.exe");
                    case RuntimePlatform.LinuxEditor:
                        return Path.Combine(PCSXReduxDir, "PCSX-Redux-HEAD-x86_64.AppImage");
                    default:
                        return Path.Combine(PCSXReduxDir, "pcsx-redux");
                }
            }
        }

        /// <summary>
        /// The Unity project root (parent of Assets/).
        /// </summary>
        public static string ProjectRoot =>
            Directory.GetParent(Application.dataPath).FullName;

        /// <summary>
        /// Path to the native psxsplash source.
        /// First checks SplashSettings override, then looks for common locations.
        /// </summary>
        public static string NativeSourceDir
        {
            get
            {
                // 1. Check the user-configured path from SplashSettings
                string custom = SplashSettings.NativeProjectPath;
                if (!string.IsNullOrEmpty(custom) && Directory.Exists(custom))
                    return custom;

                // 2. Look inside the Unity project's Assets/ folder (git clone location)
                string assetsClone = Path.Combine(UnityEngine.Application.dataPath, "psxsplash");
                if (Directory.Exists(assetsClone) && File.Exists(Path.Combine(assetsClone, "Makefile")))
                    return assetsClone;

                // 3. Look for Native/ inside the package
                string packageNative = Path.GetFullPath(
                    Path.Combine("Packages", "net.psxsplash.splashedit", "Native"));
                if (Directory.Exists(packageNative))
                    return packageNative;

                return "";
            }
        }

        /// <summary>
        /// The compiled .ps-exe output from the native build.
        /// </summary>
        public static string CompiledExePath =>
            Path.Combine(BuildOutputDir, "psxsplash.ps-exe");

        /// <summary>
        /// The scene manifest file path.
        /// </summary>
        public static string ManifestPath =>
            Path.Combine(BuildOutputDir, "manifest.bin");

        /// <summary>
        /// Build log file path.
        /// </summary>
        public static string BuildLogPath =>
            Path.Combine(BuildOutputDir, "build.log");

        /// <summary>
        /// Gets the splashpack output path for a scene by index.
        /// Uses a deterministic naming scheme: scene_0.splashpack, scene_1.splashpack, etc.
        /// </summary>
        public static string GetSceneSplashpackPath(int sceneIndex, string sceneName)
        {
            return Path.Combine(BuildOutputDir, $"scene_{sceneIndex}.splashpack");
        }

        /// <summary>
        /// Gets the VRAM data output path for a scene by index.
        /// Contains atlas pixel data, CLUT data, and font pixel data.
        /// </summary>
        public static string GetSceneVramPath(int sceneIndex, string sceneName)
        {
            return Path.Combine(BuildOutputDir, $"scene_{sceneIndex}.vram");
        }

        /// <summary>
        /// Gets the SPU data output path for a scene by index.
        /// Contains audio ADPCM data for upload to SPU RAM.
        /// </summary>
        public static string GetSceneSpuPath(int sceneIndex, string sceneName)
        {
            return Path.Combine(BuildOutputDir, $"scene_{sceneIndex}.spu");
        }

        /// <summary>
        /// Default license file path (SPLASHLICENSE.DAT) shipped in the package Data folder.
        /// Resolved relative to the Unity project so it works on any machine.
        /// </summary>
        public static string DefaultLicenseFilePath =>
            Path.GetFullPath(Path.Combine("Packages", "net.psxsplash.splashedit", "Data", "SPLASHLICENSE.DAT"));

        /// <summary>
        /// Gets the loader pack (loading screen) output path for a scene by index.
        /// Uses a deterministic naming scheme: scene_0.loading, scene_1.loading, etc.
        /// </summary>
        public static string GetSceneLoaderPackPath(int sceneIndex, string sceneName)
        {
            return Path.Combine(BuildOutputDir, $"scene_{sceneIndex}.loading");
        }

        /// <summary>
        /// ISO output path for release builds.
        /// </summary>
        public static string ISOOutputPath =>
            Path.Combine(BuildOutputDir, "psxsplash.bin");

        /// <summary>
        /// CUE sheet path for release builds.
        /// </summary>
        public static string CUEOutputPath =>
            Path.Combine(BuildOutputDir, "psxsplash.cue");

        /// <summary>
        /// XML catalog path used by mkpsxiso to build the ISO image.
        /// </summary>
        public static string ISOCatalogPath =>
            Path.Combine(BuildOutputDir, "psxsplash.xml");

        /// <summary>
        /// SYSTEM.CNF file path generated for the ISO image.
        /// The PS1 BIOS reads this to find and launch the executable.
        /// </summary>
        public static string SystemCnfPath =>
            Path.Combine(BuildOutputDir, "SYSTEM.CNF");

        /// <summary>
        /// Checks if mkpsxiso is installed in the tools directory.
        /// </summary>
        public static bool IsMkpsxisoInstalled() => MkpsxisoDownloader.IsInstalled();

        /// <summary>
        /// Ensures the build output and tools directories exist.
        /// Also appends entries to the project .gitignore if not present.
        /// </summary>
        public static void EnsureDirectories()
        {
            Directory.CreateDirectory(BuildOutputDir);
            Directory.CreateDirectory(ToolsDir);
            EnsureGitIgnore();
        }

        // ───── Lua bytecode compilation paths ─────

        /// <summary>
        /// Directory for Lua source files extracted during export.
        /// </summary>
        public static string LuaSrcDir =>
            Path.Combine(BuildOutputDir, "lua_src");

        /// <summary>
        /// Directory for compiled Lua bytecode files.
        /// </summary>
        public static string LuaCompiledDir =>
            Path.Combine(BuildOutputDir, "lua_compiled");

        /// <summary>
        /// Manifest file listing input/output pairs for the PS1 Lua compiler.
        /// </summary>
        public static string LuaManifestPath =>
            Path.Combine(LuaSrcDir, "manifest.txt");

        /// <summary>
        /// Sentinel file written by luac_psx when compilation is complete.
        /// Contains "OK" on success or "ERROR" on failure.
        /// </summary>
        public static string LuaDoneSentinel =>
            Path.Combine(LuaSrcDir, "__done__");

        /// <summary>
        /// Path to the luac_psx PS1 compiler executable (built from tools/luac_psx/).
        /// </summary>
        public static string LuacPsxExePath =>
            Path.Combine(NativeSourceDir, "tools", "luac_psx", "luac_psx.ps-exe");

        /// <summary>
        /// Path to the luac_psx tools directory (for building the compiler).
        /// </summary>
        public static string LuacPsxDir =>
            Path.Combine(NativeSourceDir, "tools", "luac_psx");

        /// <summary>
        /// Checks if PCSX-Redux is installed in the tools directory.
        /// </summary>
        public static bool IsPCSXReduxInstalled()
        {
            return File.Exists(PCSXReduxBinary);
        }

        private static void EnsureGitIgnore()
        {
            string gitignorePath = Path.Combine(ProjectRoot, ".gitignore");

            string[] entriesToAdd = new[] { "/PSXBuild/", "/.tools/" };

            string existingContent = "";
            if (File.Exists(gitignorePath))
            {
                existingContent = File.ReadAllText(gitignorePath);
            }

            bool modified = false;
            string toAppend = "";

            foreach (string entry in entriesToAdd)
            {
                // Check if entry already exists (exact line match)
                if (!existingContent.Contains(entry))
                {
                    if (!modified)
                    {
                        toAppend += "\n# SplashEdit build output\n";
                        modified = true;
                    }
                    toAppend += entry + "\n";
                }
            }

            if (modified)
            {
                File.AppendAllText(gitignorePath, toAppend);
            }
        }
    }
}
