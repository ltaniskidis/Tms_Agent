using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Tms.CentralManagement.Data;

namespace Tms.CentralManagement.Pages
{
    public class IndexModel : PageModel
    {
        private readonly CentralDbContext _context;

        public IndexModel(CentralDbContext context)
        {
            _context = context;
        }

        public IList<ClientMachine> Clients { get;set; } = default!;
        public IList<Customer> Customers { get; set; } = new List<Customer>();
        public IList<ClientMachine> UnassignedClients { get; set; } = new List<ClientMachine>();

        [BindProperty]
        public int EditProfileId { get; set; }
        [BindProperty]
        public string EditProfileName { get; set; } = string.Empty;
        [BindProperty]
        public string EditAfm { get; set; } = string.Empty;
        [BindProperty]
        public string EditSerialNumber { get; set; } = string.Empty;
        [BindProperty]
        public int EditActiveUsersCount { get; set; }
        [BindProperty]
        public string EditConnectionString { get; set; } = string.Empty;
        [BindProperty]
        public string EditTargetFolder { get; set; } = string.Empty;
        [BindProperty]
        public string EditTargetExeName { get; set; } = "TIMOLOGISI.exe";

        public async Task OnGetAsync()
        {
            Customers = await _context.Customers
                .Include(c => c.Machines)
                    .ThenInclude(m => m.Profiles)
                        .ThenInclude(p => p.UpdateLogs)
                .Include(c => c.Machines)
                    .ThenInclude(m => m.Databases)
                .OrderBy(c => c.Name)
                .ToListAsync();

            UnassignedClients = await _context.Clients
                .Where(c => c.CustomerId == null)
                .Include(c => c.Profiles)
                    .ThenInclude(p => p.UpdateLogs)
                .Include(c => c.Databases)
                .ToListAsync();

            Clients = await _context.Clients
                .Include(c => c.Profiles)
                    .ThenInclude(p => p.UpdateLogs)
                .Include(c => c.Databases)
                .ToListAsync();
        }

        public async Task<IActionResult> OnPostToggleDbAsync(int dbId)
        {
            var db = await _context.ClientDatabases.FindAsync(dbId);
            if (db != null)
            {
                db.IsMonitored = !db.IsMonitored;
                await _context.SaveChangesAsync();
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostSaveProfileSettingsAsync()
        {
            var profile = await _context.ClientProfiles.FindAsync(EditProfileId);
            if (profile != null)
            {
                profile.ProfileName = EditProfileName?.Trim() ?? string.Empty;
                profile.Afm = EditAfm?.Trim() ?? string.Empty;
                profile.SerialNumber = EditSerialNumber?.Trim() ?? string.Empty;
                profile.ActiveUsersCount = EditActiveUsersCount;
                profile.ConnectionString = EditConnectionString?.Trim() ?? string.Empty;
                profile.TargetFolder = EditTargetFolder?.Trim() ?? string.Empty;
                profile.TargetExeName = EditTargetExeName?.Trim() ?? "TIMOLOGISI.exe";
                
                await _context.SaveChangesAsync();
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostAuthorizeUpdateAsync(int profileId)
        {
            var profile = await _context.ClientProfiles.FindAsync(profileId);
            if (profile != null)
            {
                profile.IsAuthorizedForUpdate = !profile.IsAuthorizedForUpdate;
                await _context.SaveChangesAsync();
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteProfileAsync(int profileId)
        {
            var profile = await _context.ClientProfiles.FindAsync(profileId);
            if (profile != null)
            {
                profile.IsPendingDelete = true;
                await _context.SaveChangesAsync();
            }
            return RedirectToPage();
        }
    }
}
