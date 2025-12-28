using System;
using Microsoft.Win32;
using System.Diagnostics;
using System.Windows.Forms;

namespace HardwareMonitorTray
{
    /// <summary>
    /// Helper class to manage tray icon visibility in Windows notification area
    /// </summary>
    public static class TrayIconHelper
    {
        private const string NotifyIconSettingsPath = @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\TrayNotify";
        private const string ExplorerPoliciesPath = @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";

        /// <summary>
        /// Attempts to make the tray icon always visible (not hidden in overflow)
        /// Note: This requires Explorer restart or user interaction to take effect
        /// </summary>
        public static void PromoteToAlwaysShow()
        {
            try
            {
                // Method 1: Try to disable auto-hide for notification icons (Windows 10/11)
                using (var key = Registry.CurrentUser.CreateSubKey(ExplorerPoliciesPath))
                {
                    // 0 = use user settings, 1 = always show all icons
                    key?.SetValue("NoAutoTrayNotify", 1, RegistryValueKind.DWord);
                }
            }
            catch
            {
                // Ignore - user may not have permissions
            }
        }

        /// <summary>
        /// Shows instructions to user how to pin the icon
        /// </summary>
        public static void ShowPinInstructions()
        {
            var message = "To keep the icon always visible in the taskbar:\n\n" +
                          "1. Click the ▲ arrow in the taskbar (notification area)\n" +
                          "2. Find the Hardware Monitor icon\n" +
                          "3.  Drag it to the taskbar\n\n" +
                          "Or:\n" +
                          "1. Right-click on the taskbar\n" +
                          "2. Select 'Taskbar settings'\n" +
                          "3. Click 'Select which icons appear on the taskbar'\n" +
                          "4. Enable 'HardwareMonitorTray'";

            MessageBox.Show(message, "Pin Icon to Taskbar",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        /// <summary>
        /// Opens Windows taskbar settings directly
        /// </summary>
        public static void OpenTaskbarSettings()
        {
            try
            {
                // Windows 10/11 - opens taskbar settings
                Process.Start(new ProcessStartInfo
                {
                    FileName = "ms-settings:taskbar",
                    UseShellExecute = true
                });
            }
            catch
            {
                try
                {
                    // Fallback - open control panel
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "control. exe",
                        Arguments = "/name Microsoft.NotificationAreaIcons",
                        UseShellExecute = true
                    });
                }
                catch
                {
                    ShowPinInstructions();
                }
            }
        }
    }
}