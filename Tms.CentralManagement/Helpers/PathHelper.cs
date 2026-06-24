using System;
using System.IO;

namespace Tms.CentralManagement.Helpers
{
    public static class PathHelper
    {
        public static string GetPublishAndSetupPath()
        {
            var current = Directory.GetCurrentDirectory();
            
            // 1. Check if "PublishAndSetup" folder is in current directory
            var path = Path.Combine(current, "PublishAndSetup");
            if (Directory.Exists(path)) return Path.GetFullPath(path);

            // 2. Check if parent directory is "PublishAndSetup" (meaning we are running inside PublishAndSetup/CentralServer)
            var parent = Directory.GetParent(current);
            if (parent != null && parent.Name.Equals("PublishAndSetup", StringComparison.OrdinalIgnoreCase))
            {
                return parent.FullName;
            }

            // 3. Check if parent contains "PublishAndSetup"
            if (parent != null)
            {
                path = Path.Combine(parent.FullName, "PublishAndSetup");
                if (Directory.Exists(path)) return Path.GetFullPath(path);
            }

            // Fallback: create "PublishAndSetup" next to current directory if not found, or in current directory
            return Path.GetFullPath(Path.Combine(current, "..", "PublishAndSetup"));
        }
    }
}
