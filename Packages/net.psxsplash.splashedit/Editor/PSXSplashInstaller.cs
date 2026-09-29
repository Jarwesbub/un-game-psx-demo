using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SplashEdit.EditorCode
{
    /// <summary>
    /// Manages downloading and updating the psxsplash native project from GitHub releases.
    /// Uses the GitHub REST API (HTTP) to list releases and git to clone/checkout
    /// (required for recursive submodule support).
    /// </summary>
    public static class PSXSplashInstaller
    {
        // ───── Public config ─────
        public static readonly string RepoOwner = "psxsplash";
        public static readonly string RepoName = "psxsplash";
        public static readonly string RepoUrl = "https://github.com/psxsplash/psxsplash.git";
        public static readonly string InstallPath = "Assets/psxsplash";
        public static readonly string FullInstallPath;

        private static readonly string GitHubApiReleasesUrl =
            $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases";

        // ───── Cached release list ─────
        private static List<ReleaseInfo> _cachedReleases = new List<ReleaseInfo>();
        private static bool _isFetchingReleases;

        /// <summary>
        /// Represents a GitHub release.
        /// </summary>
        [Serializable]
        public class ReleaseInfo
        {
            public string TagName;      // e.g. "v1.2.0"
            public string Name;         // human-readable name
            public string Body;         // release notes (markdown)
            public string PublishedAt;  // ISO 8601 date
            public bool IsPrerelease;
            public bool IsDraft;
        }

        static PSXSplashInstaller()
        {
            FullInstallPath = Path.Combine(Application.dataPath, "psxsplash");
        }

        // ═══════════════════════════════════════════════════════════════
        // Queries
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Is the native project cloned on disk?</summary>
        public static bool IsInstalled()
        {
            return Directory.Exists(FullInstallPath) &&
                   Directory.EnumerateFileSystemEntries(FullInstallPath).Any();
        }

        /// <summary>Are we currently fetching releases from GitHub?</summary>
        public static bool IsFetchingReleases => _isFetchingReleases;

        /// <summary>Cached list of releases (call FetchReleasesAsync to populate).</summary>
        public static IReadOnlyList<ReleaseInfo> CachedReleases => _cachedReleases;

        /// <summary>
        /// Returns the tag currently checked out, or null if unknown / not a git repo.
        /// </summary>
        public static string GetCurrentTag()
        {
            if (!IsInstalled()) return null;
            try
            {
                string result = RunGitCommandSync("describe --tags --exact-match HEAD", FullInstallPath);
                return string.IsNullOrWhiteSpace(result) ? null : result.Trim();
            }
            catch
            {
                return null;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Fetch Releases (HTTP — no git required)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Fetches the list of releases from the GitHub REST API.
        /// Does NOT require git — uses UnityWebRequest.
        /// </summary>
        public static async Task<List<ReleaseInfo>> FetchReleasesAsync()
        {
            _isFetchingReleases = true;
            try
            {
                string json = await HttpGetAsync(GitHubApiReleasesUrl);
                if (string.IsNullOrEmpty(json))
                {
                    UnityEngine.Debug.LogWarning("[PSXSplashInstaller] Failed to fetch releases from GitHub.");
                    return _cachedReleases;
                }

                var releases = ParseReleasesJson(json);
                // Filter out drafts, sort by newest first
                releases = releases
                    .Where(r => !r.IsDraft)
                    .OrderByDescending(r => r.PublishedAt)
                    .ToList();

                _cachedReleases = releases;
                return releases;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[PSXSplashInstaller] Error fetching releases: {ex.Message}");
                return _cachedReleases;
            }
            finally
            {
                _isFetchingReleases = false;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Install / Clone at a specific release tag
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Clones the repository at the specified release tag with --recursive.
        /// Uses a shallow clone (--depth 1) for speed.
        /// Requires git to be installed (submodules cannot be fetched via HTTP archives).
        /// </summary>
        /// <param name="tag">The release tag to clone, e.g. "v1.2.0". If null, clones the default branch.</param>
        /// <param name="onProgress">Optional progress callback.</param>
        public static async Task<bool> InstallRelease(string tag, Action<string> onProgress = null)
        {
            if (IsInstalled())
            {
                onProgress?.Invoke("Already installed. Use SwitchToRelease to change version.");
                return true;
            }

            if (!IsGitAvailable())
            {
                UnityEngine.Debug.LogError(
                    "[PSXSplashInstaller] git is required for recursive submodule clone but was not found on PATH.\n" +
                    "Please install git: https://git-scm.com/downloads");
                return false;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FullInstallPath));

                string branchArg = string.IsNullOrEmpty(tag) ? "" : $"--branch {tag}";
                string cmd = $"clone --recursive --depth 1 {branchArg} {RepoUrl} \"{FullInstallPath}\"";

                onProgress?.Invoke($"Cloning {RepoUrl} at {tag ?? "HEAD"}...");
                string result = await RunGitCommandAsync(cmd, Application.dataPath, onProgress);

                if (!IsInstalled())
                {
                    UnityEngine.Debug.LogError("[PSXSplashInstaller] Clone completed but directory is empty.");
                    return false;
                }

                onProgress?.Invoke("Clone complete.");
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[PSXSplashInstaller] Clone failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Switches an existing clone to a different release tag.
        /// Fetches tags, checks out the tag, and updates submodules recursively.
        /// </summary>
        public static async Task<bool> SwitchToReleaseAsync(string tag, Action<string> onProgress = null)
        {
            if (!IsInstalled())
            {
                UnityEngine.Debug.LogError("[PSXSplashInstaller] Not installed — clone first.");
                return false;
            }

            if (!IsGitAvailable())
            {
                UnityEngine.Debug.LogError("[PSXSplashInstaller] git not found on PATH.");
                return false;
            }

            try
            {
                onProgress?.Invoke("Fetching tags...");
                await RunGitCommandAsync("fetch --tags --depth=1", FullInstallPath, onProgress);
                await RunGitCommandAsync($"fetch origin tag {tag} --no-tags", FullInstallPath, onProgress);

                onProgress?.Invoke($"Checking out {tag}...");
                await RunGitCommandAsync($"checkout {tag}", FullInstallPath, onProgress);

                onProgress?.Invoke("Updating submodules...");
                await RunGitCommandAsync("submodule update --init --recursive", FullInstallPath, onProgress);

                onProgress?.Invoke($"Switched to {tag}.");
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[PSXSplashInstaller] Switch failed: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Legacy compatibility: Install without specifying a tag (clones default branch).
        /// </summary>
        public static Task<bool> Install()
        {
            return InstallRelease(null);
        }

        /// <summary>
        /// Fetches latest remote data (tags, branches).
        /// Requires git.
        /// </summary>
        public static async Task<bool> FetchLatestAsync()
        {
            if (!IsInstalled()) return false;

            try
            {
                await RunGitCommandAsync("fetch --all --tags", FullInstallPath);
                return true;
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.LogError($"[PSXSplashInstaller] Fetch failed: {ex.Message}");
                return false;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Git helpers
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Checks whether git is available on the system PATH.
        /// </summary>
        public static bool IsGitAvailable()
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = "--version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using (var p = Process.Start(psi))
                {
                    p.WaitForExit(5000);
                    return p.ExitCode == 0;
                }
            }
            catch
            {
                return false;
            }
        }

        private static string RunGitCommandSync(string arguments, string workingDirectory)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (var process = Process.Start(psi))
            {
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(10000);
                return output;
            }
        }

        private static async Task<string> RunGitCommandAsync(
            string arguments, string workingDirectory, Action<string> onProgress = null)
        {
            var psi = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (var process = new Process())
            {
                process.StartInfo = psi;
                process.EnableRaisingEvents = true;

                var stdout = new System.Text.StringBuilder();
                var stderr = new System.Text.StringBuilder();

                process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        stdout.AppendLine(e.Data);
                        onProgress?.Invoke(e.Data);
                    }
                };
                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrEmpty(e.Data))
                    {
                        stderr.AppendLine(e.Data);
                        // git writes progress to stderr (clone progress, etc.)
                        onProgress?.Invoke(e.Data);
                    }
                };

                var tcs = new TaskCompletionSource<int>();
                process.Exited += (s, e) => tcs.TrySetResult(process.ExitCode);

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(10));
                var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    try { process.Kill(); } catch { }
                    throw new TimeoutException("Git command timed out after 10 minutes.");
                }

                int exitCode = await tcs.Task;
                process.Dispose();

                string output = stdout.ToString();
                string error = stderr.ToString();

                if (exitCode != 0)
                {
                    UnityEngine.Debug.LogError($"[git {arguments}] exit code {exitCode}\n{error}");
                }

                return output + error;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // HTTP helpers (no git needed)
        // ═══════════════════════════════════════════════════════════════

        private static Task<string> HttpGetAsync(string url)
        {
            var tcs = new TaskCompletionSource<string>();
            var request = UnityWebRequest.Get(url);
            request.SetRequestHeader("User-Agent", "SplashEdit-Unity");
            request.SetRequestHeader("Accept", "application/vnd.github.v3+json");

            var op = request.SendWebRequest();
            op.completed += _ =>
            {
                if (request.result == UnityWebRequest.Result.Success)
                    tcs.TrySetResult(request.downloadHandler.text);
                else
                {
                    UnityEngine.Debug.LogWarning($"[PSXSplashInstaller] HTTP GET {url} failed: {request.error}");
                    tcs.TrySetResult(null);
                }
                request.Dispose();
            };

            return tcs.Task;
        }

        // ═══════════════════════════════════════════════════════════════
        // JSON parsing (minimal, avoids external dependency)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Minimal JSON parser for the GitHub releases API response.
        /// Uses Unity's JsonUtility via a wrapper since it can't parse top-level arrays.
        /// </summary>
        private static List<ReleaseInfo> ParseReleasesJson(string json)
        {
            var releases = new List<ReleaseInfo>();

            string wrapped = "{\"items\":" + json + "}";
            var wrapper = JsonUtility.FromJson<GitHubReleaseArrayWrapper>(wrapped);

            if (wrapper?.items == null) return releases;

            foreach (var item in wrapper.items)
            {
                releases.Add(new ReleaseInfo
                {
                    TagName = item.tag_name ?? "",
                    Name = item.name ?? item.tag_name ?? "",
                    Body = item.body ?? "",
                    PublishedAt = item.published_at ?? "",
                    IsPrerelease = item.prerelease,
                    IsDraft = item.draft
                });
            }

            return releases;
        }

        [Serializable]
        private class GitHubReleaseArrayWrapper
        {
            public GitHubReleaseJson[] items;
        }

        [Serializable]
        private class GitHubReleaseJson
        {
            public string tag_name;
            public string name;
            public string body;
            public string published_at;
            public bool prerelease;
            public bool draft;
        }
    }
}