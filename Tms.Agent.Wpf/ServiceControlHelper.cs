using System;
using System.Diagnostics;

namespace Tms.Agent.Wpf
{
    public static class ServiceControlHelper
    {
        public static void ApplyStartWithWindows(bool enable)
        {
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;

            if (enable)
            {
                InstallService(exePath);
            }
            else
            {
                UninstallService();
            }
        }

        private static void InstallService(string exePath)
        {
            try
            {
                // Stop and delete if already exists to ensure updated path
                UninstallService();

                // Create service using sc.exe
                var createPsi = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = $"create TmsAgent binPath= \"\\\"{exePath}\\\" --service\" start= auto",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using (var process = Process.Start(createPsi))
                {
                    process?.WaitForExit();
                }

                // Set description
                var descPsi = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = "description TmsAgent \"TMS Agent Background Update and Synchronization Service\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using (var process = Process.Start(descPsi))
                {
                    process?.WaitForExit();
                }

                // Start service
                var startPsi = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = "start TmsAgent",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using (var process = Process.Start(startPsi))
                {
                    process?.WaitForExit();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to install service: {ex.Message}");
            }
        }

        private static void UninstallService()
        {
            try
            {
                // Stop service using sc.exe
                var stopPsi = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = "stop TmsAgent",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using (var process = Process.Start(stopPsi))
                {
                    process?.WaitForExit();
                }

                // Delete service
                var deletePsi = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    Arguments = "delete TmsAgent",
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                using (var process = Process.Start(deletePsi))
                {
                    process?.WaitForExit();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to uninstall service: {ex.Message}");
            }
        }
    }
}
