using UnityEditor;

namespace SplashEdit.EditorCode
{
    /// <summary>
    /// Automatically opens the SplashEdit Control Panel on the first editor
    /// session if the MIPS toolchain has not been installed yet.
    /// </summary>
    [InitializeOnLoad]
    public static class DependencyCheckInitializer
    {
        private const string SessionKey = "SplashEditOpenedThisSession";

        static DependencyCheckInitializer()
        {
            EditorApplication.update += OpenControlPanelOnStart;
        }

        private static void OpenControlPanelOnStart()
        {
            EditorApplication.update -= OpenControlPanelOnStart;

            if (SessionState.GetBool(SessionKey, false))
                return;

            SessionState.SetBool(SessionKey, true);

            // Only auto-open the Control Panel when the toolchain is missing
            bool toolchainReady = ToolchainChecker.IsToolAvailable("mips") ||
                                  ToolchainChecker.IsToolAvailable("mipsel-none-elf-gcc") ||
                                  ToolchainChecker.IsToolAvailable("mipsel-linux-gnu-gcc");

            if (!toolchainReady)
            {
                SplashControlPanel.ShowWindow();
            }
        }
    }
}
