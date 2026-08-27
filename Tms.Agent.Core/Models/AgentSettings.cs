using System;

namespace Tms.Agent.Core.Models
{
    public class AgentSettings
    {
        public string ServerUrl { get; set; } = "http://localhost:5007";
        public string ProductionServerUrl { get; set; } = "https://tmsagent.cdgr.dev";
        public string TestServerUrl { get; set; } = "http://home.dhsweb.gr:5007";
        public string SelectedEnvironment { get; set; } = "Production"; // "Production" or "Test"
        public string MachineRole { get; set; } = "Both"; // SqlServer, Client, Both
        public string ApiKey { get; set; } = string.Empty;
        public string ProductionApiKey { get; set; } = string.Empty;
        public string TestApiKey { get; set; } = string.Empty;
        public string? SavedUsername { get; set; }
        public string? SavedPassword { get; set; }
        public bool RememberMe { get; set; }
        public bool StartWithWindows { get; set; }
    }
}
