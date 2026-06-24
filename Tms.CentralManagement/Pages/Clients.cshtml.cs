using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Tms.CentralManagement.Data;

namespace Tms.CentralManagement.Pages
{
    public class ClientsModel : PageModel
    {
        private readonly CentralDbContext _context;

        public ClientsModel(CentralDbContext context)
        {
            _context = context;
        }

        public List<ClientMachine> Clients { get; set; } = new();

        public string CurrentSystemVersion { get; set; } = "1.5.0";

        [TempData]
        public string SuccessMessage { get; set; } = string.Empty;

        [TempData]
        public string ErrorMessage { get; set; } = string.Empty;

        public async Task OnGetAsync()
        {
            Clients = await _context.Clients
                .Include(c => c.Profiles)
                .Include(c => c.LocalUsers)
                .Include(c => c.Permissions)
                .Include(c => c.Databases)
                .ToListAsync();

            var currentSystemVersionObj = await _context.Versions
                .Where(v => v.TargetType == "System" && v.IsCurrent)
                .FirstOrDefaultAsync();
            CurrentSystemVersion = currentSystemVersionObj?.VersionNumber ?? "1.5.0";
        }

        public async Task<IActionResult> OnPostCreateClientAsync(string machineName, string machineRole)
        {
            if (string.IsNullOrWhiteSpace(machineName))
            {
                ErrorMessage = "Το όνομα μηχανήματος είναι υποχρεωτικό.";
                return RedirectToPage();
            }

            var newClient = new ClientMachine
            {
                ClientGuid = Guid.NewGuid().ToString().ToUpper(),
                MachineName = machineName,
                MachineRole = machineRole ?? "Both",
                ApiKey = GenerateRandomApiKey(),
                IsUpgradeEnabled = true,
                RegistrationDate = DateTime.UtcNow,
                Permissions = new AgentPermissions
                {
                    CanOperatorViewLogs = true,
                    CanOperatorRunUpdates = false
                }
            };

            _context.Clients.Add(newClient);
            await _context.SaveChangesAsync();

            SuccessMessage = $"Ο πελάτης '{machineName}' δημιουργήθηκε επιτυχώς με API Key: {newClient.ApiKey}";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteClientAsync(int id)
        {
            var client = await _context.Clients
                .Include(c => c.Permissions)
                .Include(c => c.LocalUsers)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (client != null)
            {
                _context.Clients.Remove(client);
                await _context.SaveChangesAsync();
                SuccessMessage = "Ο πελάτης διαγράφηκε επιτυχώς.";
            }
            else
            {
                ErrorMessage = "Ο πελάτης δεν βρέθηκε.";
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostUpdateClientSettingsAsync(int id, string machineName, string machineRole, bool isUpgradeEnabled, bool canOperatorViewLogs, bool canOperatorRunUpdates, bool startWithWindows)
        {
            var client = await _context.Clients
                .Include(c => c.Permissions)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (client == null)
            {
                ErrorMessage = "Ο πελάτης δεν βρέθηκε.";
                return RedirectToPage();
            }

            if (!string.IsNullOrWhiteSpace(machineName))
            {
                client.MachineName = machineName;
            }
            if (!string.IsNullOrWhiteSpace(machineRole))
            {
                client.MachineRole = machineRole;
            }

            client.IsUpgradeEnabled = isUpgradeEnabled;
            client.StartWithWindows = startWithWindows;

            if (client.Permissions == null)
            {
                client.Permissions = new AgentPermissions();
            }
            client.Permissions.CanOperatorViewLogs = canOperatorViewLogs;
            client.Permissions.CanOperatorRunUpdates = canOperatorRunUpdates;

            await _context.SaveChangesAsync();
            SuccessMessage = "Οι ρυθμίσεις του πελάτη ενημερώθηκαν επιτυχώς.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostRegenerateApiKeyAsync(int id)
        {
            var client = await _context.Clients.FindAsync(id);
            if (client == null)
            {
                ErrorMessage = "Ο πελάτης δεν βρέθηκε.";
                return RedirectToPage();
            }

            client.ApiKey = GenerateRandomApiKey();
            await _context.SaveChangesAsync();

            SuccessMessage = $"Νέο API Key για τον πελάτη '{client.MachineName}': {client.ApiKey}";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostAddAgentUserAsync(int clientMachineId, string username, string password, string role)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                ErrorMessage = "Το όνομα χρήστη και ο κωδικός είναι υποχρεωτικά.";
                return RedirectToPage();
            }

            var client = await _context.Clients
                .Include(c => c.LocalUsers)
                .FirstOrDefaultAsync(c => c.Id == clientMachineId);

            if (client == null)
            {
                ErrorMessage = "Ο πελάτης δεν βρέθηκε.";
                return RedirectToPage();
            }

            if (client.LocalUsers.Any(u => u.Username.ToLower() == username.ToLower()))
            {
                ErrorMessage = $"Υπάρχει ήδη χρήστης με το όνομα '{username}' για αυτόν τον πελάτη.";
                return RedirectToPage();
            }

            var newUser = new AgentUser
            {
                ClientMachineId = clientMachineId,
                Username = username,
                Password = password, // Store plain-text or custom hashed for easy transfer/sync
                Role = role ?? "Operator"
            };

            client.LocalUsers.Add(newUser);
            await _context.SaveChangesAsync();

            SuccessMessage = $"Ο χρήστης '{username}' προστέθηκε επιτυχώς.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteAgentUserAsync(int userId)
        {
            var user = await _context.AgentUsers.FindAsync(userId);
            if (user != null)
            {
                _context.AgentUsers.Remove(user);
                await _context.SaveChangesAsync();
                SuccessMessage = "Ο χρήστης του Agent διαγράφηκε επιτυχώς.";
            }
            else
            {
                ErrorMessage = "Ο χρήστης δεν βρέθηκε.";
            }

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostAddClientProfileAsync(
            int clientMachineId,
            string profileId,
            string profileName,
            string afm,
            string serialNumber,
            int activeUsersCount,
            string connectionString,
            string connectionStringType,
            string dbServer,
            string dbName,
            string dbUser,
            string dbPassword,
            bool dbUseWindowsAuth,
            string configFilePath,
            string targetFolder,
            string targetExeName,
            string emails)
        {
            if (string.IsNullOrWhiteSpace(profileId) || string.IsNullOrWhiteSpace(profileName))
            {
                ErrorMessage = "Το ID Προφίλ και το Όνομα Εταιρείας είναι υποχρεωτικά.";
                return RedirectToPage();
            }

            var client = await _context.Clients
                .Include(c => c.Profiles)
                .FirstOrDefaultAsync(c => c.Id == clientMachineId);

            if (client == null)
            {
                ErrorMessage = "Ο πελάτης δεν βρέθηκε.";
                return RedirectToPage();
            }

            if (client.Profiles.Any(p => p.ProfileId.ToLower() == profileId.ToLower().Trim()))
            {
                ErrorMessage = $"Υπάρχει ήδη εταιρεία με ID Προφίλ '{profileId}' για αυτόν τον πελάτη.";
                return RedirectToPage();
            }

            var newProfile = new ClientProfile
            {
                ClientMachineId = clientMachineId,
                ProfileId = profileId.Trim(),
                ProfileName = profileName.Trim(),
                Afm = afm?.Trim() ?? string.Empty,
                SerialNumber = serialNumber?.Trim() ?? string.Empty,
                ActiveUsersCount = activeUsersCount,
                ConnectionString = connectionString?.Trim() ?? string.Empty,
                ConnectionStringType = connectionStringType?.Trim() ?? "Direct",
                DbServer = dbServer?.Trim() ?? string.Empty,
                DbName = dbName?.Trim() ?? string.Empty,
                DbUser = dbUser?.Trim() ?? string.Empty,
                DbPassword = dbPassword?.Trim() ?? string.Empty,
                DbUseWindowsAuth = dbUseWindowsAuth,
                ConfigFilePath = configFilePath?.Trim() ?? string.Empty,
                TargetFolder = targetFolder?.Trim() ?? string.Empty,
                TargetExeName = string.IsNullOrWhiteSpace(targetExeName) ? "TmsApp.exe" : targetExeName.Trim(),
                Emails = emails?.Trim() ?? string.Empty,
                IsAuthorizedForUpdate = false,
                IsPendingDelete = false
            };

            client.Profiles.Add(newProfile);
            await _context.SaveChangesAsync();

            SuccessMessage = $"Η εταιρεία '{profileName}' προστέθηκε επιτυχώς.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostSaveClientProfileSettingsAsync(
            int editProfileId,
            string profileName,
            string afm,
            string serialNumber,
            int activeUsersCount,
            string connectionString,
            string connectionStringType,
            string dbServer,
            string dbName,
            string dbUser,
            string dbPassword,
            bool dbUseWindowsAuth,
            string configFilePath,
            string targetFolder,
            string targetExeName,
            string emails)
        {
            var profile = await _context.ClientProfiles.FindAsync(editProfileId);
            if (profile == null)
            {
                ErrorMessage = "Η εταιρεία δεν βρέθηκε.";
                return RedirectToPage();
            }

            profile.ProfileName = profileName?.Trim() ?? string.Empty;
            profile.Afm = afm?.Trim() ?? string.Empty;
            profile.SerialNumber = serialNumber?.Trim() ?? string.Empty;
            profile.ActiveUsersCount = activeUsersCount;
            profile.ConnectionString = connectionString?.Trim() ?? string.Empty;
            profile.ConnectionStringType = connectionStringType?.Trim() ?? "Direct";
            profile.DbServer = dbServer?.Trim() ?? string.Empty;
            profile.DbName = dbName?.Trim() ?? string.Empty;
            profile.DbUser = dbUser?.Trim() ?? string.Empty;
            profile.DbPassword = dbPassword?.Trim() ?? string.Empty;
            profile.DbUseWindowsAuth = dbUseWindowsAuth;
            profile.ConfigFilePath = configFilePath?.Trim() ?? string.Empty;
            profile.TargetFolder = targetFolder?.Trim() ?? string.Empty;
            profile.TargetExeName = targetExeName?.Trim() ?? "TmsApp.exe";
            profile.Emails = emails?.Trim() ?? string.Empty;

            await _context.SaveChangesAsync();
            SuccessMessage = $"Οι αλλαγές για την εταιρεία '{profile.ProfileName}' αποθηκεύτηκαν επιτυχώς.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteClientProfileAsync(int profileId)
        {
            var profile = await _context.ClientProfiles.FindAsync(profileId);
            if (profile != null)
            {
                profile.IsPendingDelete = true;
                await _context.SaveChangesAsync();
                SuccessMessage = $"Ζητήθηκε η διαγραφή της εταιρείας '{profile.ProfileName}'. Θα διαγραφεί μόλις συγχρονίσει ο Agent.";
            }
            else
            {
                ErrorMessage = "Η εταιρεία δεν βρέθηκε.";
            }
            return RedirectToPage();
        }

        private string GenerateRandomApiKey()
        {
            return "TMS-KEY-" + Guid.NewGuid().ToString("N").Substring(0, 16).ToUpper();
        }
    }
}
