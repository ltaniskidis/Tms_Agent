using System;
using System.IO;

namespace Tms.Agent.Core.Services
{
    public static class PathHelper
    {
        public static string GetAgentDataFolder()
        {
            var oldDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TmsAgent");
            var newDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "TmsAgent");

            try
            {
                if (!Directory.Exists(newDir))
                {
                    Directory.CreateDirectory(newDir);
                }

                // Migrate files from user-specific AppData to ProgramData if they exist in old but not in new
                if (Directory.Exists(oldDir))
                {
                    foreach (var file in Directory.GetFiles(oldDir))
                    {
                        try
                        {
                            var destFile = Path.Combine(newDir, Path.GetFileName(file));
                            if (!File.Exists(destFile))
                            {
                                File.Copy(file, destFile, true);
                            }
                        }
                        catch
                        {
                            // Ignore single file migration failure
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Directory migration warning: {ex.Message}");
            }

            return newDir;
        }
    }
}
