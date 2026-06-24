using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace Tms.CentralManagement.Data
{
    public class CentralDbContext : DbContext
    {
        public CentralDbContext(DbContextOptions<CentralDbContext> options) : base(options)
        {
        }

        public DbSet<VersionInfo> Versions { get; set; } = null!;
        public DbSet<SqlScript> SqlScripts { get; set; } = null!;
        public DbSet<ReleaseNote> ReleaseNotes { get; set; } = null!;
        public DbSet<ClientMachine> Clients { get; set; } = null!;
        public DbSet<ClientProfile> ClientProfiles { get; set; } = null!;
        public DbSet<UpdateLog> UpdateLogs { get; set; } = null!;
        public DbSet<ClientDatabase> ClientDatabases { get; set; } = null!;
        public DbSet<ConsoleUser> ConsoleUsers { get; set; } = null!;
        public DbSet<AgentUser> AgentUsers { get; set; } = null!;
        public DbSet<AgentPermissions> AgentPermissions { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<VersionInfo>()
                .HasMany(v => v.Scripts)
                .WithOne()
                .HasForeignKey(s => s.VersionInfoId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<VersionInfo>()
                .HasMany(v => v.ReleaseNotes)
                .WithOne()
                .HasForeignKey(r => r.VersionInfoId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ClientMachine>()
                .HasMany(c => c.Profiles)
                .WithOne()
                .HasForeignKey(p => p.ClientMachineId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ClientMachine>()
                .HasMany(c => c.Databases)
                .WithOne()
                .HasForeignKey(d => d.ClientMachineId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ClientMachine>()
                .HasMany(c => c.LocalUsers)
                .WithOne()
                .HasForeignKey(u => u.ClientMachineId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ClientMachine>()
                .HasOne(c => c.Permissions)
                .WithOne()
                .HasForeignKey<AgentPermissions>(p => p.ClientMachineId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ClientProfile>()
                .HasMany(p => p.UpdateLogs)
                .WithOne()
                .HasForeignKey(l => l.ClientProfileId)
                .OnDelete(DeleteBehavior.Cascade);
        }
    }

    public class VersionInfo
    {
        public int Id { get; set; }
        public string VersionNumber { get; set; } = string.Empty;
        public DateTime ReleaseDate { get; set; } = DateTime.UtcNow;
        public string Description { get; set; } = string.Empty;
        public string BinaryFileUrl { get; set; } = string.Empty;
        public string? SecurityCode { get; set; }
        public bool IsActive { get; set; } = true;
        public string TargetType { get; set; } = "Program"; // "System" (Console/Agent) or "Program" (Desktop App)
        public string? AffectedSystemComponent { get; set; } = "Both"; // "Console", "Agent", "Both"
        public bool IsCurrent { get; set; } = false;
        public List<SqlScript> Scripts { get; set; } = new();
        public List<ReleaseNote> ReleaseNotes { get; set; } = new();
    }

    public class SqlScript
    {
        public int Id { get; set; }
        public int VersionInfoId { get; set; }
        public string ScriptName { get; set; } = string.Empty;
        public string ScriptContent { get; set; } = string.Empty;
        public int SequenceOrder { get; set; }
    }

    public class ReleaseNote
    {
        public int Id { get; set; }
        public int VersionInfoId { get; set; }
        public string Language { get; set; } = "el"; // Default to Greek
        public string NotesContent { get; set; } = string.Empty;
    }

    public class ClientMachine
    {
        public int Id { get; set; }
        public string ClientGuid { get; set; } = string.Empty; // Unique hardware ID or name
        public string MachineName { get; set; } = string.Empty;
        public string MachineRole { get; set; } = "Both"; // SqlServer, Client, Both
        public string AgentVersion { get; set; } = "1.0.0";
        public DateTime RegistrationDate { get; set; } = DateTime.UtcNow;
        public string ApiKey { get; set; } = string.Empty;
        public bool IsUpgradeEnabled { get; set; } = true;

        public List<ClientProfile> Profiles { get; set; } = new();
        public List<ClientDatabase> Databases { get; set; } = new();
        public List<AgentUser> LocalUsers { get; set; } = new();
        public AgentPermissions? Permissions { get; set; }
    }

    public class ClientProfile
    {
        public int Id { get; set; }
        public int ClientMachineId { get; set; }
        public string ProfileId { get; set; } = string.Empty; // Local profile identifier on agent
        public string ProfileName { get; set; } = string.Empty;
        public string Afm { get; set; } = string.Empty;
        public string TargetFolder { get; set; } = string.Empty;
        public string TargetExeName { get; set; } = "TmsApp.exe";
        public string ConnectionString { get; set; } = string.Empty;
        public string ConnectionStringType { get; set; } = "Direct"; // Direct, Builder, ConfigFile
        public string DbServer { get; set; } = string.Empty;
        public string DbName { get; set; } = string.Empty;
        public string DbUser { get; set; } = string.Empty;
        public string DbPassword { get; set; } = string.Empty;
        public bool DbUseWindowsAuth { get; set; } = false;
        public string ConfigFilePath { get; set; } = string.Empty;
        public string SerialNumber { get; set; } = string.Empty;
        public int ActiveUsersCount { get; set; } = 0;
        public string LastUpdatedVersion { get; set; } = "0.0.0";
        public string LastUpdateStatus { get; set; } = "Unknown";
        public DateTime? LastUpdatedTime { get; set; }
        
        public bool IsPendingDelete { get; set; } = false;
        public bool IsAuthorizedForUpdate { get; set; } = false;

        public List<UpdateLog> UpdateLogs { get; set; } = new();
    }

    public class ClientDatabase
    {
        public int Id { get; set; }
        public int ClientMachineId { get; set; }
        public string InstanceName { get; set; } = string.Empty;
        public string DatabaseName { get; set; } = string.Empty;
        public string ConnectionString { get; set; } = string.Empty;
        public bool IsMonitored { get; set; } = false;
        public string LastUpdatedVersion { get; set; } = "0.0.0";
        public string LastUpdateStatus { get; set; } = "Unknown";
        public DateTime? LastUpdatedTime { get; set; }
    }

    public class UpdateLog
    {
        public int Id { get; set; }
        public int ClientProfileId { get; set; }
        public string VersionNumber { get; set; } = string.Empty;
        public DateTime ExecutionTime { get; set; } = DateTime.UtcNow;
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string LogDetails { get; set; } = string.Empty;
    }

    public class ConsoleUser
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string Role { get; set; } = "Operator"; // SuperAdmin, Operator
        public string Scope { get; set; } = "Console"; // Console, Agent, Both
    }

    public class AgentUser
    {
        public int Id { get; set; }
        public int ClientMachineId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty; // local plaintext or hashed password
        public string Role { get; set; } = "Operator"; // Admin, Operator
    }

    public class AgentPermissions
    {
        public int Id { get; set; }
        public int ClientMachineId { get; set; }
        public bool CanOperatorViewLogs { get; set; } = true;
        public bool CanOperatorRunUpdates { get; set; } = false;
    }
}
