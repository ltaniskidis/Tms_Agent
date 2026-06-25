using System;
using System.Configuration;
using System.Data;
using System.Linq;
using System.Windows;

namespace Tms.Agent.Wpf;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    public static string LoggedInUser { get; set; } = string.Empty;
    public static string UserRole { get; set; } = string.Empty;

    private string? GetArgValue(string[] args, string argName)
    {
        var idx = Array.FindIndex(args, a => string.Equals(a, argName, StringComparison.OrdinalIgnoreCase));
        if (idx >= 0 && idx + 1 < args.Length)
        {
            return args[idx + 1];
        }
        return null;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // Check for self-installation command line arguments
        var installFromArgIndex = Array.FindIndex(e.Args, a => string.Equals(a, "--install-from", StringComparison.OrdinalIgnoreCase));
        if (installFromArgIndex >= 0 && installFromArgIndex + 1 < e.Args.Length)
        {
            try
            {
                string sourceExe = e.Args[installFromArgIndex + 1];
                
                // Parse other arguments
                string url = GetArgValue(e.Args, "--url") ?? string.Empty;
                string key = GetArgValue(e.Args, "--key") ?? string.Empty;
                string role = GetArgValue(e.Args, "--role") ?? "Client";
                bool startWithWindows = string.Equals(GetArgValue(e.Args, "--startup-windows"), "1");

                // Save settings
                var settingsManager = new Tms.Agent.Core.Services.SettingsManager();
                var settings = new Tms.Agent.Core.Models.AgentSettings
                {
                    ServerUrl = url,
                    ApiKey = key,
                    MachineRole = role,
                    StartWithWindows = startWithWindows
                };
                settingsManager.SaveSettings(settings);

                // Setup installation folder
                string targetFolder = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "TmsAgent");
                string targetExe = System.IO.Path.Combine(targetFolder, "Tms.Agent.Wpf.exe");

                if (!System.IO.Directory.Exists(targetFolder))
                {
                    System.IO.Directory.CreateDirectory(targetFolder);
                }

                // Copy source executable to target
                System.IO.File.Copy(sourceExe, targetExe, true);

                // Register Startup tasks pointing to the target
                RegisterStartupTask(targetExe);
                ServiceControlHelper.ApplyStartWithWindows(startWithWindows, targetExe);

                // Start the installed executable
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = targetExe,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Σφάλμα κατά την αυτόματη εγκατάσταση: {ex.Message}", "Σφάλμα Εγκατάστασης", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
            }
            
            Shutdown();
            return;
        }

        if (e.Args.Contains("--service", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                System.ServiceProcess.ServiceBase.Run(new TmsAgentService());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Service start failed: {ex.Message}");
            }
            Shutdown();
            return;
        }

        base.OnStartup(e);

        // Prevent application shutdown when the LoginWindow closes
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        // Auto-register startup task in Task Scheduler since we are running as Admin
        RegisterStartupTask();

        // If launched normally (not silent startup), require login
        if (!e.Args.Contains("--startup", StringComparer.OrdinalIgnoreCase))
        {
            // Check if settings exist and are configured
            var settingsManager = new Tms.Agent.Core.Services.SettingsManager();
            var settings = settingsManager.LoadSettings();

            if (string.IsNullOrEmpty(settings.ApiKey) || string.IsNullOrEmpty(settings.ServerUrl))
            {
                var wizard = new SetupWizardWindow();
                if (wizard.ShowDialog() != true)
                {
                    Shutdown();
                    return;
                }
            }

            var loginWindow = new LoginWindow();
            if (loginWindow.ShowDialog() != true)
            {
                Shutdown();
                return;
            }
            LoggedInUser = loginWindow.LoggedInUser;
            UserRole = loginWindow.UserRole;
        }

        // Restore shutdown mode to close when MainWindow closes
        ShutdownMode = ShutdownMode.OnMainWindowClose;

        var mainWindow = new MainWindow();

        // If launched with --startup argument, start minimized/hidden in tray
        if (e.Args.Contains("--startup", StringComparer.OrdinalIgnoreCase))
        {
            mainWindow.WindowState = WindowState.Minimized;
            mainWindow.Hide();
        }
        else
        {
            mainWindow.Show();
        }
    }

    public static void RegisterStartupTask(string? targetExePath = null)
    {
        try
        {
            string? exePath = targetExePath ?? Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;

            // Register task to run elevated without UAC prompts at logon
            string args = $"/create /tn \"TmsAgent\" /tr \"\\\"{exePath}\\\" --startup\" /sc onlogon /rl highest /f";

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "schtasks.exe",
                Arguments = args,
                CreateNoWindow = true,
                UseShellExecute = false
            };
            using var process = System.Diagnostics.Process.Start(psi);
            process?.WaitForExit();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to register startup task: {ex.Message}");
        }
    }
}

