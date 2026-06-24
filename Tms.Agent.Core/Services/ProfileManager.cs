using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Tms.Agent.Core.Models;

namespace Tms.Agent.Core.Services
{
    public class ProfileManager
    {
        private readonly string _filePath;

        public ProfileManager()
        {
            var appFolder = PathHelper.GetAgentDataFolder();
            _filePath = Path.Combine(appFolder, "profiles.json");
        }

        public ProfileManager(string customFilePath)
        {
            _filePath = customFilePath;
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
        }

        public List<LocalProfile> LoadProfiles()
        {
            if (!File.Exists(_filePath))
            {
                return new List<LocalProfile>();
            }

            try
            {
                var json = File.ReadAllText(_filePath);
                return JsonSerializer.Deserialize<List<LocalProfile>>(json) ?? new List<LocalProfile>();
            }
            catch
            {
                return new List<LocalProfile>();
            }
        }

        public void SaveProfiles(List<LocalProfile> profiles)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(profiles, options);
                File.ReadAllText(_filePath); // Check read access
                File.WriteAllText(_filePath, json);
            }
            catch (FileNotFoundException)
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(profiles, options);
                File.WriteAllText(_filePath, json);
            }
            catch
            {
                // Fallback direct write
                var options = new JsonSerializerOptions { WriteIndented = true };
                var json = JsonSerializer.Serialize(profiles, options);
                File.WriteAllText(_filePath, json);
            }
        }
    }
}
