using System;

namespace Tms.Agent.Core.Models
{
    public class AgentSettings
    {
        public string ServerUrl { get; set; } = "http://localhost:5007";
        public string MachineRole { get; set; } = "Both"; // SqlServer, Client, Both
        public string ApiKey { get; set; } = string.Empty;
    }
}
