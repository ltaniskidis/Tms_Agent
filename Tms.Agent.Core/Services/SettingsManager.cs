using System;
using System.IO;
using System.Text.Json;
using Tms.Agent.Core.Models;

namespace Tms.Agent.Core.Services
{
    public class SettingsManager
    {
        private readonly string _filePath;

        public SettingsManager()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var appFolder = Path.Combine(appDataPath, "TmsAgent");
            
            if (!Directory.Exists(appFolder))
            {
                Directory.CreateDirectory(appFolder);
            }

            _filePath = Path.Combine(appFolder, "settings.json");
        }

        public SettingsManager(string customFilePath)
        {
            _filePath = customFilePath;
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        public AgentSettings LoadSettings()
        {
            if (!File.Exists(_filePath))
            {
                return new AgentSettings();
            }

            try
            {
                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<AgentSettings>(json) ?? new AgentSettings();
            }
            catch
            {
                return new AgentSettings();
            }
        }

        public void SaveSettings(AgentSettings settings)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(settings, options);
                File.WriteAllText(_filePath, json);
            }
            catch
            {
                // Ignored
            }
        }
    }
}
