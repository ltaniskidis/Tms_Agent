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

    protected override void OnStartup(StartupEventArgs e)
    {
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

    private void RegisterStartupTask()
    {
        try
        {
            string? exePath = Environment.ProcessPath;
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

