using System;
using System.Collections.Generic;

namespace Tms.Shared.Models
{
    public class UpdateCheckRequest
    {
        public string ClientId { get; set; } = string.Empty;
        public string MachineName { get; set; } = string.Empty;
        public string MachineRole { get; set; } = "Both"; // SqlServer, Client, Both
        public string AgentVersion { get; set; } = "1.0.0";
        public string ApiKey { get; set; } = string.Empty;
        public List<LocalProfileDto> Profiles { get; set; } = new();
        public List<DiscoveredDatabaseDto> DiscoveredDatabases { get; set; } = new();
    }

    public class DiscoveredDatabaseDto
    {
        public string InstanceName { get; set; } = string.Empty;
        public string DatabaseName { get; set; } = string.Empty;
        public string ConnectionString { get; set; } = string.Empty;
    }

    public class LocalProfileDto
    {
        public string ProfileId { get; set; } = string.Empty;
        public string ProfileName { get; set; } = string.Empty;
        public string Afm { get; set; } = string.Empty;
        public string CurrentVersion { get; set; } = "0.0.0";
        public string SerialNumber { get; set; } = string.Empty;
        public int ActiveUsersCount { get; set; } = 0;
        public string TargetFolder { get; set; } = string.Empty;
        public string TargetExeName { get; set; } = "TmsApp.exe";
        public string ConnectionString { get; set; } = string.Empty;
        public string ConnectionStringType { get; set; } = "Direct";
        public string DbServer { get; set; } = string.Empty;
        public string DbName { get; set; } = string.Empty;
        public string DbUser { get; set; } = string.Empty;
        public string DbPassword { get; set; } = string.Empty;
        public bool DbUseWindowsAuth { get; set; } = false;
        public string ConfigFilePath { get; set; } = string.Empty;
    }

    public class UpdateCheckResponse
    {
        public bool HasUpdates { get; set; }
        public bool IsUpgradeAllowed { get; set; } = true;
        public string CurrentSystemVersion { get; set; } = string.Empty;
        public List<ProfileUpdateDto> Updates { get; set; } = new();
        public List<string> MonitoredDatabaseNames { get; set; } = new();
        public List<ProfileConfigCommandDto> ConfigCommands { get; set; } = new();
        public List<AgentUserDto> LocalUsers { get; set; } = new();
        public AgentPermissionsDto Permissions { get; set; } = new();
        public List<BroadcastMessageDto> Broadcasts { get; set; } = new();
    }

    public class ProfileConfigCommandDto
    {
        public string CommandType { get; set; } = string.Empty; // SaveProfile, DeleteProfile
        public string ProfileId { get; set; } = string.Empty;
        public string ProfileName { get; set; } = string.Empty;
        public string Afm { get; set; } = string.Empty;
        public string TargetFolder { get; set; } = string.Empty;
        public string TargetExeName { get; set; } = "TmsApp.exe";
        public string ConnectionString { get; set; } = string.Empty;
        public string ConnectionStringType { get; set; } = "Direct";
        public string DbServer { get; set; } = string.Empty;
        public string DbName { get; set; } = string.Empty;
        public string DbUser { get; set; } = string.Empty;
        public string DbPassword { get; set; } = string.Empty;
        public bool DbUseWindowsAuth { get; set; } = false;
        public string ConfigFilePath { get; set; } = string.Empty;
        public string CurrentVersion { get; set; } = "1.0.0";
        public string SerialNumber { get; set; } = string.Empty;
        public int ActiveUsersCount { get; set; } = 0;
    }

    public class ProfileUpdateDto
    {
        public string ProfileId { get; set; } = string.Empty;
        public string Afm { get; set; } = string.Empty;
        public VersionDto NewVersion { get; set; } = new();
        public bool IsAuthorizedByAdmin { get; set; } = false; // Flag to indicate if Admin has authorized/scheduled this update
    }

    public class VersionDto
    {
        public int Id { get; set; }
        public string VersionNumber { get; set; } = string.Empty;
        public DateTime ReleaseDate { get; set; }
        public string Description { get; set; } = string.Empty;
        public string BinaryFileUrl { get; set; } = string.Empty;
        public string? SecurityCode { get; set; }
        public string TargetType { get; set; } = "Program"; // "System" or "Program"
        public List<string> ReleaseNotes { get; set; } = new();
        public List<ScriptDto> Scripts { get; set; } = new();
    }

    public class ScriptDto
    {
        public int Id { get; set; }
        public string ScriptName { get; set; } = string.Empty;
        public string ScriptContent { get; set; } = string.Empty;
        public int SequenceOrder { get; set; }
    }

    public class UpdateLogSubmissionDto
    {
        public string ClientId { get; set; } = string.Empty;
        public string MachineName { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string ProfileId { get; set; } = string.Empty;
        public string ProfileName { get; set; } = string.Empty;
        public string Afm { get; set; } = string.Empty;
        public string VersionNumber { get; set; } = string.Empty;
        public DateTime ExecutionTime { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string LogDetails { get; set; } = string.Empty;
    }

    public class AgentUserDto
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Role { get; set; } = "Operator"; // Admin, Operator
    }

    public class AgentPermissionsDto
    {
        public bool CanOperatorViewLogs { get; set; } = true;
        public bool CanOperatorRunUpdates { get; set; } = false;
    }

    public class SyncUsersRequest
    {
        public string ApiKey { get; set; } = string.Empty;
        public List<AgentUserDto> Users { get; set; } = new();
    }

    public class BroadcastMessageDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; }
    }
}
