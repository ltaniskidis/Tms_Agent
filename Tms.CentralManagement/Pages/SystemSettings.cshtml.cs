using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Tms.CentralManagement.Data;

namespace Tms.CentralManagement.Pages
{
    [Authorize(Roles = "SuperAdmin")]
    public class SystemSettingsModel : PageModel
    {
        private readonly CentralDbContext _context;
        private readonly IConfiguration _configuration;

        public SystemSettingsModel(CentralDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        [BindProperty]
        public string AgentRedirectServerUrl { get; set; } = string.Empty;

        [BindProperty]
        public string AgentRedirectTestServerUrl { get; set; } = string.Empty;

        public SystemSetting? ActiveRedirect { get; set; }

        public SystemSetting? LastSavedRedirect { get; set; }

        public System.Collections.Generic.List<SystemSetting> History { get; set; } = new();

        [TempData]
        public string SuccessMessage { get; set; } = string.Empty;

        [TempData]
        public string ErrorMessage { get; set; } = string.Empty;

        public async Task OnGetAsync()
        {
            History = await _context.SystemSettings
                .OrderByDescending(s => s.Id)
                .ToListAsync();

            // The last entry physically saved in DB represents the last saved configuration
            LastSavedRedirect = History.FirstOrDefault();

            // Find the most recent non-empty redirect setting, which represents the currently active redirection URL
            ActiveRedirect = History.FirstOrDefault(s => !string.IsNullOrEmpty(s.AgentRedirectServerUrl) || !string.IsNullOrEmpty(s.AgentRedirectTestServerUrl));

            if (LastSavedRedirect != null)
            {
                AgentRedirectServerUrl = LastSavedRedirect.AgentRedirectServerUrl ?? string.Empty;
                AgentRedirectTestServerUrl = LastSavedRedirect.AgentRedirectTestServerUrl ?? string.Empty;
            }
            else
            {
                AgentRedirectServerUrl = _configuration["AgentRedirectServerUrl"] ?? string.Empty;
                AgentRedirectTestServerUrl = _configuration["AgentRedirectTestServerUrl"] ?? string.Empty;
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            try
            {
                var newSetting = new SystemSetting
                {
                    AgentRedirectServerUrl = AgentRedirectServerUrl?.Trim(),
                    AgentRedirectTestServerUrl = AgentRedirectTestServerUrl?.Trim(),
                    CreatedAt = DateTime.UtcNow,
                    ChangedBy = User.Identity?.Name ?? "admin"
                };

                _context.SystemSettings.Add(newSetting);
                await _context.SaveChangesAsync();
                SuccessMessage = "Οι ρυθμίσεις συστήματος αποθηκεύτηκαν με επιτυχία.";
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Σφάλμα κατά την αποθήκευση: {ex.Message}";
            }

            return RedirectToPage();
        }
    }
}
