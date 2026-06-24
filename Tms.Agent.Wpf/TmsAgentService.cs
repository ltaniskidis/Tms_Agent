using System;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Tms.Agent.Core.Models;
using Tms.Agent.Core.Services;
using Tms.Shared.Models;

namespace Tms.Agent.Wpf
{
    public class TmsAgentService : ServiceBase
    {
        private readonly CancellationTokenSource _cts = new();
        private Task? _workerTask;

        public TmsAgentService()
        {
            ServiceName = "TmsAgent";
        }

        protected override void OnStart(string[] args)
        {
            _workerTask = Task.Run(() => RunServiceLoopAsync(_cts.Token));
        }

        protected override void OnStop()
        {
            _cts.Cancel();
            try
            {
                _workerTask?.Wait(5000);
            }
            catch
            {
                // Ignored
            }
        }

        private async Task RunServiceLoopAsync(CancellationToken cancellationToken)
        {
            var updateEngine = new UpdateEngine();
            var settingsManager = new SettingsManager();
            var profileManager = new ProfileManager();

            // Client ID logic: read or create a GUID for this installation
            var appDataPath = PathHelper.GetAgentDataFolder();
            var idFile = Path.Combine(appDataPath, "client_id.txt");
            string clientId;
            if (File.Exists(idFile))
            {
                clientId = File.ReadAllText(idFile).Trim();
            }
            else
            {
                clientId = Guid.NewGuid().ToString();
                File.WriteAllText(idFile, clientId);
            }

            string machineName = Environment.MachineName;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Reload settings and profiles at each tick to get latest changes from GUI
                    var settings = settingsManager.LoadSettings();
                    var profiles = profileManager.LoadProfiles();

                    if (!string.IsNullOrEmpty(settings.ApiKey) && !string.IsNullOrEmpty(settings.ServerUrl))
                    {
                        var response = await updateEngine.CheckForUpdatesAsync(
                            settings.ServerUrl,
                            clientId,
                            machineName,
                            settings.MachineRole,
                            "1.5.17", // Bumped version
                            settings.ApiKey,
                            profiles,
                            settings.StartWithWindows,
                            false
                        );

                        if (response != null)
                        {
                            // 1. Handle Configuration Sync Commands from Server
                            if (response.ConfigCommands != null && response.ConfigCommands.Any())
                            {
                                bool profilesChanged = false;
                                foreach (var cmd in response.ConfigCommands)
                                {
                                    if (cmd.CommandType == "SaveProfile")
                                    {
                                        var existing = profiles.FirstOrDefault(p => p.ProfileId == cmd.ProfileId);
                                        if (existing == null)
                                        {
                                            profiles.Add(new LocalProfile
                                            {
                                                ProfileId = cmd.ProfileId,
                                                ProfileName = cmd.ProfileName,
                                                Afm = cmd.Afm,
                                                TargetFolder = cmd.TargetFolder,
                                                TargetExeName = cmd.TargetExeName,
                                                ConnectionString = cmd.ConnectionString,
                                                CurrentVersion = cmd.CurrentVersion,
                                                SerialNumber = cmd.SerialNumber,
                                                ActiveUsersCount = cmd.ActiveUsersCount
                                            });
                                            profilesChanged = true;
                                        }
                                        else
                                        {
                                            existing.ProfileName = cmd.ProfileName;
                                            existing.Afm = cmd.Afm;
                                            existing.TargetFolder = cmd.TargetFolder;
                                            existing.TargetExeName = cmd.TargetExeName;
                                            existing.ConnectionString = cmd.ConnectionString;
                                            existing.CurrentVersion = cmd.CurrentVersion;
                                            existing.SerialNumber = cmd.SerialNumber;
                                            existing.ActiveUsersCount = cmd.ActiveUsersCount;
                                            profilesChanged = true;
                                        }
                                    }
                                    else if (cmd.CommandType == "DeleteProfile")
                                    {
                                        var existing = profiles.FirstOrDefault(p => p.ProfileId == cmd.ProfileId);
                                        if (existing != null)
                                        {
                                            profiles.Remove(existing);
                                            profilesChanged = true;
                                        }
                                    }
                                }

                                if (profilesChanged)
                                {
                                    profileManager.SaveProfiles(profiles);
                                }
                            }

                            // 2. Sync local users
                            if (response.LocalUsers != null)
                            {
                                try
                                {
                                    var usersFilePath = Path.Combine(appDataPath, "users.json");
                                    var json = JsonSerializer.Serialize(response.LocalUsers, new JsonSerializerOptions { WriteIndented = true });
                                    File.WriteAllText(usersFilePath, json);
                                }
                                catch
                                {
                                    // Ignored
                                }
                            }
                            
                            // 3. Sync StartWithWindows configuration down from server
                            if (response.StartWithWindows != settings.StartWithWindows)
                            {
                                settings.StartWithWindows = response.StartWithWindows;
                                settingsManager.SaveSettings(settings);
                                
                                // Apply the service install/uninstall change
                                ServiceControlHelper.ApplyStartWithWindows(response.StartWithWindows);
                            }
                        }
                    }
                }
                catch
                {
                    // Ignored to prevent service crash
                }

                // Poll every 2 minutes
                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(2), cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }
    }
}
