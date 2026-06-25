using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Tms.CentralManagement.Data;
using Tms.Shared.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Http;
using System.IO;

namespace Tms.CentralManagement.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UpdatesController : ControllerBase
    {
        private readonly CentralDbContext _context;
        private readonly IConfiguration _configuration;

        public UpdatesController(CentralDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        // 1. Check for updates from Agents
        [HttpPost("check")]
        public async Task<ActionResult<UpdateCheckResponse>> CheckForUpdates([FromBody] UpdateCheckRequest request)
        {
            if (string.IsNullOrEmpty(request.ApiKey))
            {
                return Unauthorized("API Key is required.");
            }

            // Register / Update Client Machine in DB by ApiKey
            var client = await _context.Clients
                .Include(c => c.Profiles)
                .Include(c => c.Databases)
                .Include(c => c.LocalUsers)
                .Include(c => c.Permissions)
                .FirstOrDefaultAsync(c => c.ApiKey == request.ApiKey);

            if (client == null)
            {
                return Unauthorized("Invalid API Key.");
            }

            // Pair/Sync details
            client.ClientGuid = request.ClientId;
            client.MachineName = request.MachineName;
            client.MachineRole = request.MachineRole;
            client.AgentVersion = request.AgentVersion ?? "1.0.0";

            if (request.ForceSyncStartWithWindows)
            {
                client.StartWithWindows = request.StartWithWindows;
            }

            // Sync discovered databases
            if (request.DiscoveredDatabases != null)
            {
                foreach (var discDb in request.DiscoveredDatabases)
                {
                    var existingDb = client.Databases.FirstOrDefault(d => 
                        d.InstanceName == discDb.InstanceName && 
                        d.DatabaseName == discDb.DatabaseName);

                    if (existingDb == null)
                    {
                        existingDb = new ClientDatabase
                        {
                            InstanceName = discDb.InstanceName,
                            DatabaseName = discDb.DatabaseName,
                            ConnectionString = discDb.ConnectionString,
                            IsMonitored = false
                        };
                        client.Databases.Add(existingDb);
                    }
                    else
                    {
                        existingDb.ConnectionString = discDb.ConnectionString;
                    }
                }
            }

            var response = new UpdateCheckResponse();
            response.IsUpgradeAllowed = client.IsUpgradeEnabled;
            response.StartWithWindows = client.StartWithWindows;

            var currentSystemVersionObj = await _context.Versions
                .Where(v => v.TargetType == "System" && v.IsCurrent)
                .FirstOrDefaultAsync();
            response.CurrentSystemVersion = currentSystemVersionObj?.VersionNumber ?? "1.5.0";
            response.SystemBinaryUrl = currentSystemVersionObj?.BinaryFileUrl ?? string.Empty;

            // Sync users
            response.LocalUsers = client.LocalUsers.Select(u => new AgentUserDto
            {
                Username = u.Username,
                Password = u.Password,
                Role = u.Role
            }).ToList();

            // Inject console users that have Scope 'Agent' or 'Both'
            var consoleSyncedUsers = await _context.ConsoleUsers
                .Where(u => u.Scope == "Agent" || u.Scope == "Both")
                .ToListAsync();

            foreach (var cu in consoleSyncedUsers)
            {
                var existing = response.LocalUsers.FirstOrDefault(u => u.Username.Equals(cu.Username, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    existing.Password = cu.PasswordHash;
                    existing.Role = cu.Username.Equals("owner", StringComparison.OrdinalIgnoreCase) ? "Owner" : (cu.Role == "SuperAdmin" ? "Admin" : "Operator");
                }
                else
                {
                    response.LocalUsers.Add(new AgentUserDto
                    {
                        Username = cu.Username,
                        Password = cu.PasswordHash,
                        Role = cu.Username.Equals("owner", StringComparison.OrdinalIgnoreCase) ? "Owner" : (cu.Role == "SuperAdmin" ? "Admin" : "Operator")
                    });
                }
            }

            // Sync permissions
            if (client.Permissions != null)
            {
                response.Permissions = new AgentPermissionsDto
                {
                    CanOperatorViewLogs = client.Permissions.CanOperatorViewLogs,
                    CanOperatorRunUpdates = client.Permissions.CanOperatorRunUpdates
                };
            }
            else
            {
                response.Permissions = new AgentPermissionsDto
                {
                    CanOperatorViewLogs = true,
                    CanOperatorRunUpdates = false
                };
            }

            response.MonitoredDatabaseNames = client.Databases
                .Where(d => d.IsMonitored)
                .Select(d => d.DatabaseName)
                .ToList();

            var allActiveVersions = await _context.Versions
                .Include(v => v.Scripts)
                .Include(v => v.ReleaseNotes)
                .Where(v => v.IsActive && v.TargetType == "Program")
                .ToListAsync();

            // 1. Process client reported profiles
            foreach (var localProfile in request.Profiles)
            {
                var dbProfile = client.Profiles.FirstOrDefault(p => p.ProfileId == localProfile.ProfileId);
                if (dbProfile == null)
                {
                    // Registration
                    dbProfile = new ClientProfile
                    {
                        ProfileId = localProfile.ProfileId,
                        ProfileName = localProfile.ProfileName,
                        Afm = localProfile.Afm,
                        SerialNumber = localProfile.SerialNumber,
                        ActiveUsersCount = localProfile.ActiveUsersCount,
                        LastUpdatedVersion = localProfile.CurrentVersion,
                        LastUpdatedProgramVersion = localProfile.CurrentProgramVersion ?? localProfile.CurrentVersion,
                        LastUpdatedDbVersion = localProfile.CurrentDbVersion ?? localProfile.CurrentVersion,
                        LastUpdateStatus = "Registered",
                        LastUpdatedTime = DateTime.UtcNow,
                        TargetFolder = localProfile.TargetFolder ?? string.Empty,
                        TargetExeName = localProfile.TargetExeName ?? "TIMOLOGISI.exe",
                        ConnectionString = localProfile.ConnectionString ?? string.Empty,
                        ConnectionStringType = localProfile.ConnectionStringType ?? "Direct",
                        DbServer = localProfile.DbServer ?? string.Empty,
                        DbName = localProfile.DbName ?? string.Empty,
                        DbUser = localProfile.DbUser ?? string.Empty,
                        DbPassword = localProfile.DbPassword ?? string.Empty,
                        DbUseWindowsAuth = localProfile.DbUseWindowsAuth,
                        ConfigFilePath = localProfile.ConfigFilePath ?? string.Empty
                    };
                    client.Profiles.Add(dbProfile);
                }
                else
                {
                    // Clean up profile from server if it is marked as pending delete and client has deleted it (or we delete it now)
                    if (dbProfile.IsPendingDelete)
                    {
                        response.ConfigCommands.Add(new ProfileConfigCommandDto
                        {
                            CommandType = "DeleteProfile",
                            ProfileId = dbProfile.ProfileId
                        });
                        continue;
                    }

                    // Check if server config is different. If so, push to client.
                    bool needsSync = dbProfile.ProfileName != localProfile.ProfileName ||
                                     dbProfile.Afm != localProfile.Afm ||
                                     dbProfile.SerialNumber != localProfile.SerialNumber ||
                                     dbProfile.ActiveUsersCount != localProfile.ActiveUsersCount ||
                                     dbProfile.TargetFolder != localProfile.TargetFolder ||
                                     dbProfile.TargetExeName != localProfile.TargetExeName ||
                                     dbProfile.ConnectionString != localProfile.ConnectionString ||
                                     dbProfile.ConnectionStringType != localProfile.ConnectionStringType ||
                                     dbProfile.DbServer != localProfile.DbServer ||
                                     dbProfile.DbName != localProfile.DbName ||
                                     dbProfile.DbUser != localProfile.DbUser ||
                                     dbProfile.DbPassword != localProfile.DbPassword ||
                                     dbProfile.DbUseWindowsAuth != localProfile.DbUseWindowsAuth ||
                                     dbProfile.ConfigFilePath != localProfile.ConfigFilePath;

                    if (needsSync)
                    {
                        response.ConfigCommands.Add(new ProfileConfigCommandDto
                        {
                            CommandType = "SaveProfile",
                            ProfileId = dbProfile.ProfileId,
                            ProfileName = dbProfile.ProfileName,
                            Afm = dbProfile.Afm,
                            TargetFolder = dbProfile.TargetFolder,
                            TargetExeName = dbProfile.TargetExeName,
                            ConnectionString = dbProfile.ConnectionString,
                            ConnectionStringType = dbProfile.ConnectionStringType,
                            DbServer = dbProfile.DbServer,
                            DbName = dbProfile.DbName,
                            DbUser = dbProfile.DbUser,
                            DbPassword = dbProfile.DbPassword,
                            DbUseWindowsAuth = dbProfile.DbUseWindowsAuth,
                            ConfigFilePath = dbProfile.ConfigFilePath,
                            CurrentVersion = dbProfile.LastUpdatedVersion,
                            CurrentProgramVersion = dbProfile.LastUpdatedProgramVersion,
                            CurrentDbVersion = dbProfile.LastUpdatedDbVersion,
                            SerialNumber = dbProfile.SerialNumber,
                            ActiveUsersCount = dbProfile.ActiveUsersCount
                        });
                    }
                    else
                    {
                        dbProfile.LastUpdatedVersion = localProfile.CurrentVersion;
                        dbProfile.LastUpdatedProgramVersion = localProfile.CurrentProgramVersion ?? localProfile.CurrentVersion;
                        dbProfile.LastUpdatedDbVersion = localProfile.CurrentDbVersion ?? localProfile.CurrentVersion;
                    }
                }

                // Check for updates (only if upgrade is enabled)
                if (client.IsUpgradeEnabled)
                {
                    if (!Version.TryParse(localProfile.CurrentProgramVersion, out var currentProgVer))
                    {
                        if (!Version.TryParse(localProfile.CurrentVersion, out currentProgVer))
                        {
                            currentProgVer = new Version(0, 0, 0);
                        }
                    }

                    if (!Version.TryParse(localProfile.CurrentDbVersion, out var currentDbVer))
                    {
                        if (!Version.TryParse(localProfile.CurrentVersion, out currentDbVer))
                        {
                            currentDbVer = new Version(0, 0, 0);
                        }
                    }

                    var newerVersions = allActiveVersions
                        .Select(v => new { VersionInfo = v, Parsed = Version.TryParse(v.VersionNumber, out var ver) ? ver : new Version(0, 0, 0) })
                        .Where(x => x.Parsed > currentProgVer || (x.VersionInfo.Scripts != null && x.VersionInfo.Scripts.Any() && x.Parsed > currentDbVer))
                        .OrderByDescending(x => x.Parsed)
                        .ToList();

                    if (newerVersions.Any())
                    {
                        var latestUpdate = newerVersions.First().VersionInfo;
                        var latestParsed = newerVersions.First().Parsed;

                        bool isWaitingForDb = false;
                        if (latestUpdate.Scripts != null && latestUpdate.Scripts.Any() && request.MachineRole == "Client")
                        {
                            if (!Version.TryParse(dbProfile.LastUpdatedDbVersion, out var dbVer) || dbVer < latestParsed)
                            {
                                isWaitingForDb = true;
                            }
                        }

                        response.Updates.Add(new ProfileUpdateDto
                        {
                            ProfileId = localProfile.ProfileId,
                            Afm = localProfile.Afm,
                            IsAuthorizedByAdmin = dbProfile.IsAuthorizedForUpdate,
                            IsWaitingForDb = isWaitingForDb,
                            NewVersion = new VersionDto
                            {
                                Id = latestUpdate.Id,
                                VersionNumber = latestUpdate.VersionNumber,
                                ReleaseDate = latestUpdate.ReleaseDate,
                                Description = latestUpdate.Description,
                                BinaryFileUrl = latestUpdate.BinaryFileUrl,
                                SecurityCode = latestUpdate.SecurityCode,
                                ReleaseNotes = latestUpdate.ReleaseNotes.Select(rn => rn.NotesContent).ToList(),
                                Scripts = (latestUpdate.Scripts ?? new List<SqlScript>()).OrderBy(s => s.SequenceOrder).Select(s => new ScriptDto
                                {
                                    Id = s.Id,
                                    ScriptName = s.ScriptName,
                                    ScriptContent = s.ScriptContent,
                                    SequenceOrder = s.SequenceOrder
                                }).ToList()
                            }
                        });
                    }
                }
            }

            // 2. Scan DB for profiles to delete (client acknowledged deletion by not sending it) or profiles to add
            var clientProfileIds = request.Profiles.Select(p => p.ProfileId).ToList();
            
            // Remove profiles that are marked as deleted and no longer reported by the client
            var deletedProfiles = client.Profiles.Where(p => p.IsPendingDelete && !clientProfileIds.Contains(p.ProfileId)).ToList();
            foreach (var del in deletedProfiles)
            {
                client.Profiles.Remove(del);
            }

            // Push server-created profiles to the client
            var serverOnlyProfiles = client.Profiles
                .Where(p => !p.IsPendingDelete && !clientProfileIds.Contains(p.ProfileId))
                .ToList();

            foreach (var serverProfile in serverOnlyProfiles)
            {
                response.ConfigCommands.Add(new ProfileConfigCommandDto
                {
                    CommandType = "SaveProfile",
                    ProfileId = serverProfile.ProfileId,
                    ProfileName = serverProfile.ProfileName,
                    Afm = serverProfile.Afm,
                    TargetFolder = serverProfile.TargetFolder,
                    TargetExeName = serverProfile.TargetExeName,
                    ConnectionString = serverProfile.ConnectionString,
                    ConnectionStringType = serverProfile.ConnectionStringType,
                    DbServer = serverProfile.DbServer,
                    DbName = serverProfile.DbName,
                    DbUser = serverProfile.DbUser,
                    DbPassword = serverProfile.DbPassword,
                    DbUseWindowsAuth = serverProfile.DbUseWindowsAuth,
                    ConfigFilePath = serverProfile.ConfigFilePath,
                    CurrentVersion = serverProfile.LastUpdatedVersion,
                    CurrentProgramVersion = serverProfile.LastUpdatedProgramVersion,
                    CurrentDbVersion = serverProfile.LastUpdatedDbVersion,
                    SerialNumber = serverProfile.SerialNumber,
                    ActiveUsersCount = serverProfile.ActiveUsersCount
                });
            }

            await _context.SaveChangesAsync();

            // Fetch active broadcast messages for this client
            var broadcasts = await _context.BroadcastMessages
                .Where(b => b.IsActive && (string.IsNullOrEmpty(b.TargetClientApiKey) || b.TargetClientApiKey == request.ApiKey))
                .OrderByDescending(b => b.CreatedDate)
                .Select(b => new BroadcastMessageDto
                {
                    Id = b.Id,
                    Title = b.Title,
                    Content = b.Content,
                    CreatedDate = b.CreatedDate
                })
                .ToListAsync();
            response.Broadcasts = broadcasts;

            response.HasUpdates = response.Updates.Any();
            return Ok(response);
        }

        // POST: api/updates/authorize
        [HttpPost("authorize")]
        public async Task<IActionResult> AuthorizeUpdate([FromBody] AuthorizeUpdateRequest request)
        {
            if (string.IsNullOrEmpty(request.ApiKey))
            {
                return Unauthorized("API Key is required.");
            }

            var client = await _context.Clients
                .Include(c => c.Profiles)
                .FirstOrDefaultAsync(c => c.ApiKey == request.ApiKey);

            if (client == null)
            {
                return Unauthorized("Invalid API Key.");
            }

            var profile = client.Profiles.FirstOrDefault(p => p.ProfileId == request.ProfileId);
            if (profile == null)
            {
                return NotFound("Profile not found.");
            }

            profile.IsAuthorizedForUpdate = true;
            await _context.SaveChangesAsync();
            return Ok();
        }

        // 2. Submit logs from Agents
        [HttpPost("log")]
        public async Task<IActionResult> SubmitLog([FromBody] UpdateLogSubmissionDto request)
        {
            if (string.IsNullOrEmpty(request.ApiKey))
            {
                return Unauthorized("API Key is required.");
            }

            var client = await _context.Clients
                .Include(c => c.Profiles)
                .FirstOrDefaultAsync(c => c.ApiKey == request.ApiKey);

            if (client == null)
            {
                return Unauthorized("Invalid API Key.");
            }

            var profile = client.Profiles.FirstOrDefault(p => p.ProfileId == request.ProfileId);
            if (profile == null)
            {
                // Fallback: create profile if not exists
                profile = new ClientProfile
                {
                    ProfileId = request.ProfileId,
                    ProfileName = request.ProfileName,
                    Afm = request.Afm
                };
                client.Profiles.Add(profile);
                await _context.SaveChangesAsync();
            }

            // Create log
            var log = new UpdateLog
            {
                ClientProfileId = profile.Id,
                VersionNumber = request.VersionNumber,
                ProgramVersion = !string.IsNullOrEmpty(request.ProgramVersion) ? request.ProgramVersion : request.VersionNumber,
                DbVersion = !string.IsNullOrEmpty(request.DbVersion) ? request.DbVersion : request.VersionNumber,
                ExecutionTime = request.ExecutionTime,
                Success = request.Success,
                ErrorMessage = request.ErrorMessage,
                LogDetails = request.LogDetails
            };

            _context.UpdateLogs.Add(log);

            // Update profile status
            profile.LastUpdatedVersion = request.VersionNumber;
            profile.LastUpdatedProgramVersion = !string.IsNullOrEmpty(request.ProgramVersion) ? request.ProgramVersion : request.VersionNumber;
            profile.LastUpdatedDbVersion = !string.IsNullOrEmpty(request.DbVersion) ? request.DbVersion : request.VersionNumber;
            profile.LastUpdateStatus = request.Success ? "Success" : "Failed";
            profile.LastUpdatedTime = request.ExecutionTime;
            if (request.Success)
            {
                profile.IsAuthorizedForUpdate = false; // Clear authorization on success
            }

            await _context.SaveChangesAsync();
            return Ok();
        }

        // 3. Admin: Get all clients and their profiles
        [HttpGet("admin/clients")]
        public async Task<ActionResult<IEnumerable<ClientMachine>>> GetClients()
        {
            var clients = await _context.Clients
                .Include(c => c.Profiles)
                    .ThenInclude(p => p.UpdateLogs)
                .ToListAsync();

            return Ok(clients);
        }

        // 4. Admin: Get all versions
        [HttpGet("admin/versions")]
        public async Task<ActionResult<IEnumerable<VersionInfo>>> GetVersions()
        {
            var versions = await _context.Versions
                .Include(v => v.Scripts)
                .Include(v => v.ReleaseNotes)
                .ToListAsync();

            return Ok(versions);
        }

        // 5. Admin: Create a new version
        [HttpPost("admin/versions")]
        public async Task<ActionResult<VersionInfo>> CreateVersion([FromBody] VersionDto dto)
        {
            if (string.IsNullOrEmpty(dto.VersionNumber))
            {
                return BadRequest("Version number is required.");
            }

            var version = new VersionInfo
                {
                    VersionNumber = dto.VersionNumber,
                    ReleaseDate = dto.ReleaseDate == default ? DateTime.UtcNow : dto.ReleaseDate,
                    Description = dto.Description,
                    BinaryFileUrl = dto.BinaryFileUrl,
                    SecurityCode = dto.SecurityCode,
                    TargetType = dto.TargetType ?? "Program",
                    IsActive = true
                };

            // Add release notes
            foreach (var noteText in dto.ReleaseNotes)
            {
                version.ReleaseNotes.Add(new ReleaseNote { NotesContent = noteText });
            }

            // Add SQL Scripts
            foreach (var scriptDto in dto.Scripts)
            {
                version.Scripts.Add(new SqlScript
                {
                    ScriptName = scriptDto.ScriptName,
                    ScriptContent = scriptDto.ScriptContent,
                    SequenceOrder = scriptDto.SequenceOrder
                });
            }

            _context.Versions.Add(version);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetVersions), new { id = version.Id }, version);
        }

        // 6. Admin: Toggle version status
        [HttpPost("admin/versions/{id}/toggle")]
        public async Task<IActionResult> ToggleVersionActive(int id)
        {
            var version = await _context.Versions.FindAsync(id);
            if (version == null)
            {
                return NotFound();
            }

            version.IsActive = !version.IsActive;
            await _context.SaveChangesAsync();

            return Ok(new { id = version.Id, isActive = version.IsActive });
        }

        // 7. Agent: Sync local users back to Central Server
        [HttpPost("sync-users")]
        public async Task<IActionResult> SyncUsers([FromBody] SyncUsersRequest request)
        {
            if (string.IsNullOrEmpty(request.ApiKey))
            {
                return Unauthorized("API Key is required.");
            }

            var client = await _context.Clients
                .Include(c => c.LocalUsers)
                .FirstOrDefaultAsync(c => c.ApiKey == request.ApiKey);

            if (client == null)
            {
                return Unauthorized("Invalid API Key.");
            }

            // Remove existing local users
            _context.AgentUsers.RemoveRange(client.LocalUsers);

            // Add new local users (filtering out owner username just in case)
            foreach (var userDto in request.Users)
            {
                if (userDto.Username.ToLower() == "owner" || userDto.Role.ToLower() == "owner")
                {
                    continue;
                }

                client.LocalUsers.Add(new AgentUser
                {
                    ClientMachineId = client.Id,
                    Username = userDto.Username,
                    Password = userDto.Password,
                    Role = userDto.Role
                });
            }

            await _context.SaveChangesAsync();
            return Ok();
        }

        [HttpGet("support-tickets")]
        public async Task<IActionResult> GetSupportTickets([FromQuery] string apiKey, [FromQuery] string clientId)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                return BadRequest("API Key is required.");
            }
            if (string.IsNullOrEmpty(clientId))
            {
                return BadRequest("Client ID is required.");
            }

            var client = await _context.Clients.AnyAsync(c => c.ApiKey == apiKey);
            if (!client)
            {
                return Unauthorized("Invalid API Key.");
            }

            var tickets = await _context.SupportTickets
                .Where(t => t.ApiKey == apiKey && t.ClientGuid == clientId)
                .OrderByDescending(t => t.CreatedDate)
                .Select(t => new SupportTicketDto
                {
                    Id = t.Id,
                    Subject = t.Subject,
                    Body = t.Body,
                    CreatedDate = t.CreatedDate,
                    Status = t.Status,
                    AdminResponse = t.AdminResponse,
                    ResponseDate = t.ResponseDate,
                    AttachmentFileName = t.AttachmentFileName
                })
                .ToListAsync();

            return Ok(tickets);
        }

        // 8. Agent: Send support email
        [HttpPost("send-support-email")]
        public async Task<IActionResult> SendSupportEmail(
            [FromForm] string subject,
            [FromForm] string body,
            [FromForm] string apiKey,
            IFormFile? attachment)
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                return BadRequest("API Key is required.");
            }

            var client = await _context.Clients
                .Include(c => c.Profiles)
                .FirstOrDefaultAsync(c => c.ApiKey == apiKey);

            if (client == null)
            {
                return Unauthorized("Invalid API Key.");
            }

            // Save attachment physical file
            string? attachmentFileName = null;
            var sentEmailsDir = Path.Combine(Directory.GetCurrentDirectory(), "SentEmails");
            if (attachment != null && attachment.Length > 0)
            {
                try
                {
                    if (!Directory.Exists(sentEmailsDir))
                    {
                        Directory.CreateDirectory(sentEmailsDir);
                    }

                    var uniqueId = Guid.NewGuid().ToString("N");
                    attachmentFileName = $"{uniqueId}_{Path.GetFileName(attachment.FileName)}";
                    var targetPath = Path.Combine(sentEmailsDir, attachmentFileName);
                    using (var fs = new FileStream(targetPath, FileMode.Create))
                    {
                        await attachment.CopyToAsync(fs);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error saving attachment: {ex}");
                }
            }

            // Create and save Support Ticket entity
            var ticket = new SupportTicket
            {
                ClientGuid = client.ClientGuid,
                MachineName = client.MachineName,
                ApiKey = apiKey,
                Subject = subject,
                Body = body,
                CreatedDate = DateTime.UtcNow,
                AttachmentFileName = attachmentFileName,
                Status = "Open"
            };

            _context.SupportTickets.Add(ticket);
            await _context.SaveChangesAsync();

            // Get all profile names for this client
            var companyNames = client.Profiles != null && client.Profiles.Any()
                ? string.Join(", ", client.Profiles.Select(p => p.ProfileName))
                : "Δεν βρέθηκαν καταχωρημένα προφίλ";

            // Append machine detail info to email body
            var fullBody = $"Στοιχεία Μηχανήματος:\n" +
                           $"Επιχείρηση/Εταιρείες: {companyNames}\n" +
                           $"Όνομα Μηχανήματος: {client.MachineName}\n" +
                           $"ApiKey: {client.ApiKey}\n" +
                           $"Έκδοση Agent: {client.AgentVersion}\n" +
                           $"Ημερομηνία: {DateTime.Now}\n\n" +
                           $"Κείμενο:\n{body}";

            var mailSubject = $"[TMS Agent Support] {subject} - {client.MachineName}";

            // Target Emails
            var toAddresses = new[] { "support@cleverdata.gr", "l.taniskidis@cleverdata.gr", "e.kordouli@cleverdata.gr" };
            bool emailSent = false;
            string smtpError = string.Empty;

            try
            {
                var dbSetting = await _context.SmtpSettings.FirstOrDefaultAsync();
                
                string? server;
                string? portStr;
                string? username;
                string? password;
                string? enableSslStr;
                string? sender;

                if (dbSetting != null)
                {
                    server = dbSetting.Server;
                    portStr = dbSetting.Port.ToString();
                    username = dbSetting.Username;
                    password = dbSetting.Password;
                    enableSslStr = dbSetting.EnableSsl.ToString();
                    sender = dbSetting.Sender;
                }
                else
                {
                    var smtpSection = _configuration.GetSection("SmtpSettings");
                    server = smtpSection["Server"];
                    portStr = smtpSection["Port"];
                    username = smtpSection["Username"];
                    password = smtpSection["Password"];
                    enableSslStr = smtpSection["EnableSsl"];
                    sender = smtpSection["Sender"];
                }

                if (!string.IsNullOrEmpty(server) && !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                {
                    using (var mail = new System.Net.Mail.MailMessage())
                    {
                        mail.From = new System.Net.Mail.MailAddress(sender ?? username, "Clever_Support");
                        foreach (var addr in toAddresses)
                        {
                            mail.To.Add(addr);
                        }
                        mail.Subject = mailSubject;
                        mail.Body = fullBody;

                        if (!string.IsNullOrEmpty(attachmentFileName))
                        {
                            var targetPath = Path.Combine(sentEmailsDir, attachmentFileName);
                            if (System.IO.File.Exists(targetPath))
                            {
                                var mailAttachment = new System.Net.Mail.Attachment(targetPath);
                                mail.Attachments.Add(mailAttachment);
                            }
                        }

                        int port = int.TryParse(portStr, out var p) ? p : 587;
                        bool enableSsl = bool.TryParse(enableSslStr, out var ssl) ? ssl : true;

                        using (var smtp = new System.Net.Mail.SmtpClient(server, port))
                        {
                            smtp.Credentials = new System.Net.NetworkCredential(username, password);
                            smtp.EnableSsl = enableSsl;
                            await smtp.SendMailAsync(mail);
                            emailSent = true;
                        }
                    }
                }
                else
                {
                    smtpError = "SMTP settings are missing or incomplete.";
                }
            }
            catch (Exception ex)
            {
                smtpError = ex.ToString();
            }

            if (!emailSent)
            {
                try
                {
                    if (!Directory.Exists(sentEmailsDir))
                    {
                        Directory.CreateDirectory(sentEmailsDir);
                    }

                    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    var textFile = Path.Combine(sentEmailsDir, $"SupportEmail_{ticket.Id}_{timestamp}.txt");

                    var fileContent = $"SMTP Error: {smtpError}\n" +
                                      $"To: {string.Join(", ", toAddresses)}\n" +
                                      $"Subject: {mailSubject}\n" +
                                      $"Body:\n{fullBody}\n";

                    if (!string.IsNullOrEmpty(attachmentFileName))
                    {
                        fileContent += $"Attachment Saved Locally As: {attachmentFileName}\n";
                    }

                    await System.IO.File.WriteAllTextAsync(textFile, fileContent, System.Text.Encoding.UTF8);
                }
                catch (Exception fileEx)
                {
                    Console.WriteLine($"Failed to write local email file: {fileEx}");
                }
            }

            return Ok(new { Success = true });
        }
    }
}
