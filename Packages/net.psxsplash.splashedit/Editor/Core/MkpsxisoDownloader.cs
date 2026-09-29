using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace SplashEdit.EditorCode
{
    /// <summary>
    /// Downloads and manages mkpsxiso — the tool that builds PlayStation CD images
    /// from an XML catalog. Used for the ISO build target.
    /// https://github.com/Lameguy64/mkpsxiso
    /// </summary>
    public static class MkpsxisoDownloader
    {
        private const string MKPSXISO_VERSION = "2.20";
        private const string MKPSXISO_RELEASE_BASE =
            "https://github.com/Lameguy64/mkpsxiso/releases/download/v" + MKPSXISO_VERSION + "/";

        private static readonly HttpClient _http = new HttpClient();

        /// <summary>
        /// Install directory for mkpsxiso inside .tools/
        /// </summary>
        public static string MkpsxisoDir =>
            Path.Combine(SplashBuildPaths.ToolsDir, "mkpsxiso");

        /// <summary>
        /// Path to the mkpsxiso binary.
        /// </summary>
        public static string MkpsxisoBinary
        {
            get
            {
                if (Application.platform == RuntimePlatform.WindowsEditor)
                    return Path.Combine(MkpsxisoDir, "mkpsxiso.exe");
                return Path.Combine(MkpsxisoDir, "bin", "mkpsxiso");
            }
        }

        /// <summary>
        /// Returns true if mkpsxiso is installed and ready to use.
        /// </summary>
        public static bool IsInstalled() => File.Exists(MkpsxisoBinary);

        /// <summary>
        /// Downloads and installs mkpsxiso from the official GitHub releases.
        /// </summary>
        public static async Task<bool> DownloadAndInstall(Action<string> log = null)
        {
            string archiveName;
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor:
                    archiveName = $"mkpsxiso-{MKPSXISO_VERSION}-win64.zip";
                    break;
                case RuntimePlatform.LinuxEditor:
                    archiveName = $"mkpsxiso-{MKPSXISO_VERSION}-Linux.zip";
                    break;
                case RuntimePlatform.OSXEditor:
                    archiveName = $"mkpsxiso-{MKPSXISO_VERSION}-Darwin.zip";
                    break;
                default:
                    log?.Invoke("Unsupported platform for mkpsxiso.");
                    return false;
            }

            string downloadUrl = $"{MKPSXISO_RELEASE_BASE}{archiveName}";
            log?.Invoke($"Downloading mkpsxiso: {downloadUrl}");

            try
            {
                string tempFile = Path.Combine(Path.GetTempPath(), archiveName);
                EditorUtility.DisplayProgressBar("Downloading mkpsxiso", "Downloading...", 0.1f);

                using (var client = new System.Net.WebClient())
                {
                    client.Headers.Add("User-Agent", "SplashEdit/1.0");

                    client.DownloadProgressChanged += (s, e) =>
                    {
                        float progress = 0.1f + 0.8f * (e.ProgressPercentage / 100f);
                        string sizeMB = $"{e.BytesReceived / (1024 * 1024)}/{e.TotalBytesToReceive / (1024 * 1024)} MB";
                        EditorUtility.DisplayProgressBar("Downloading mkpsxiso", $"Downloading... {sizeMB}", progress);
                    };

                    await client.DownloadFileTaskAsync(new Uri(downloadUrl), tempFile);
                }

                log?.Invoke("Extracting...");
                EditorUtility.DisplayProgressBar("Installing mkpsxiso", "Extracting...", 0.9f);

                string installDir = MkpsxisoDir;
                if (Directory.Exists(installDir))
                    Directory.Delete(installDir, true);
                Directory.CreateDirectory(installDir);

                System.IO.Compression.ZipFile.ExtractToDirectory(tempFile, installDir);

                // Fix nested directory (archives often have one extra level)
                SplashEdit.RuntimeCode.Utils.FixNestedDirectory(installDir);

                try { File.Delete(tempFile); } catch { }

                EditorUtility.ClearProgressBar();

                if (IsInstalled())
                {
                    // Make executable on Linux 
                    if (Application.platform != RuntimePlatform.WindowsEditor)
                    {
                        var chmod = Process.Start("chmod", $"+x \"{MkpsxisoBinary}\"");
                        chmod?.WaitForExit();
                    }
                    log?.Invoke("mkpsxiso installed successfully!");
                    return true;
                }

                log?.Invoke($"mkpsxiso binary not found at: {MkpsxisoBinary}");
                return false;
            }
            catch (Exception ex)
            {
                log?.Invoke($"mkpsxiso download failed: {ex.Message}");
                EditorUtility.ClearProgressBar();
                return false;
            }
        }

        /// <summary>
        /// Runs mkpsxiso with the given XML catalog to produce a BIN/CUE image.
        /// </summary>
        /// <param name="xmlPath">Path to the mkpsxiso XML catalog.</param>
        /// <param name="outputBin">Override output .bin path (optional, uses XML default if null).</param>
        /// <param name="outputCue">Override output .cue path (optional, uses XML default if null).</param>
        /// <param name="log">Logging callback.</param>
        /// <returns>True if mkpsxiso succeeded.</returns>
        public static bool BuildISO(string xmlPath, string outputBin = null,
            string outputCue = null, Action<string> log = null)
        {
            if (!IsInstalled())
            {
                log?.Invoke("mkpsxiso is not installed.");
                return false;
            }

            // Build arguments
            string args = $"-y \"{xmlPath}\"";
            if (!string.IsNullOrEmpty(outputBin))
                args += $" -o \"{outputBin}\"";
            if (!string.IsNullOrEmpty(outputCue))
                args += $" -c \"{outputCue}\"";

            log?.Invoke($"Running: mkpsxiso {args}");

            var psi = new ProcessStartInfo
            {
                FileName = MkpsxisoBinary,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            try
            {
                var process = Process.Start(psi);
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (!string.IsNullOrEmpty(stdout))
                    log?.Invoke(stdout.Trim());

                if (process.ExitCode != 0)
                {
                    if (!string.IsNullOrEmpty(stderr))
                        log?.Invoke($"mkpsxiso error: {stderr.Trim()}");
                    log?.Invoke($"mkpsxiso exited with code {process.ExitCode}");
                    return false;
                }

                log?.Invoke("ISO image built successfully.");
                return true;
            }
            catch (Exception ex)
            {
                log?.Invoke($"mkpsxiso execution failed: {ex.Message}");
                return false;
            }
        }
    }
}
