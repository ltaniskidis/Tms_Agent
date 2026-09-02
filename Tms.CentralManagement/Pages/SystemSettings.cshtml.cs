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
        private readonly Tms.CentralManagement.Services.IServerUrlValidator _urlValidator;

        public SystemSettingsModel(CentralDbContext context, IConfiguration configuration, Tms.CentralManagement.Services.IServerUrlValidator urlValidator)
        {
            _context = context;
            _configuration = configuration;
            _urlValidator = urlValidator;
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
            await LoadPageStateAsync();

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

        public async Task<IActionResult> OnGetValidateUrlAsync(string? url)
        {
            var result = await _urlValidator.ValidateAsync(url, allowEmpty: false);
            return new JsonResult(result);
        }

        public async Task<IActionResult> OnPostAsync()
        {
            try
            {
                // Validate Production URL if provided
                if (!string.IsNullOrWhiteSpace(AgentRedirectServerUrl))
                {
                    var prodResult = await _urlValidator.ValidateAsync(AgentRedirectServerUrl, allowEmpty: true);
                    if (!prodResult.IsValid)
                    {
                        ErrorMessage = $"❌ Αποτυχία ελέγχου Παραγωγικού URL: {prodResult.ErrorMessage} Οι ρυθμίσεις ΔΕΝ αποθηκεύτηκαν για να προστατευθούν οι Agents από αποσύνδεση.";
                        await LoadPageStateAsync();
                        return Page();
                    }
                    AgentRedirectServerUrl = prodResult.NormalizedUrl ?? AgentRedirectServerUrl.Trim();
                }
                else
                {
                    AgentRedirectServerUrl = string.Empty;
                }

                // Validate Test URL if provided
                if (!string.IsNullOrWhiteSpace(AgentRedirectTestServerUrl))
                {
                    var testResult = await _urlValidator.ValidateAsync(AgentRedirectTestServerUrl, allowEmpty: true);
                    if (!testResult.IsValid)
                    {
                        ErrorMessage = $"❌ Αποτυχία ελέγχου Δοκιμαστικού URL: {testResult.ErrorMessage} Οι ρυθμίσεις ΔΕΝ αποθηκεύτηκαν για να προστατευθούν οι Agents από αποσύνδεση.";
                        await LoadPageStateAsync();
                        return Page();
                    }
                    AgentRedirectTestServerUrl = testResult.NormalizedUrl ?? AgentRedirectTestServerUrl.Trim();
                }
                else
                {
                    AgentRedirectTestServerUrl = string.Empty;
                }

                var newSetting = new SystemSetting
                {
                    AgentRedirectServerUrl = AgentRedirectServerUrl,
                    AgentRedirectTestServerUrl = AgentRedirectTestServerUrl,
                    CreatedAt = DateTime.UtcNow,
                    ChangedBy = User.Identity?.Name ?? "admin"
                };

                _context.SystemSettings.Add(newSetting);
                await _context.SaveChangesAsync();
                SuccessMessage = "Οι ρυθμίσεις συστήματος αποθηκεύτηκαν με επιτυχία. Τα URLs επαληθεύτηκαν και είναι ενεργά.";
                return RedirectToPage();
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Σφάλμα κατά την αποθήκευση: {ex.Message}";
                await LoadPageStateAsync();
                return Page();
            }
        }

        private async Task LoadPageStateAsync()
        {
            History = await _context.SystemSettings
                .OrderByDescending(s => s.Id)
                .ToListAsync();

            // The last entry physically saved in DB represents the last saved configuration
            LastSavedRedirect = History.FirstOrDefault();

            // Find the most recent non-empty redirect setting, which represents the currently active redirection URL
            ActiveRedirect = History.FirstOrDefault(s => !string.IsNullOrEmpty(s.AgentRedirectServerUrl) || !string.IsNullOrEmpty(s.AgentRedirectTestServerUrl));
        }
    }
}
