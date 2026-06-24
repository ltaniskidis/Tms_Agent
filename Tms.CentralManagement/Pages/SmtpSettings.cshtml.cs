using System;
using System.Linq;
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
    public class SmtpSettingsModel : PageModel
    {
        private readonly CentralDbContext _context;
        private readonly IConfiguration _configuration;

        public SmtpSettingsModel(CentralDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        [BindProperty]
        public SmtpSetting SmtpSetting { get; set; } = new();

        public bool IsLoadedFromDb { get; set; } = false;

        [TempData]
        public string SuccessMessage { get; set; } = string.Empty;

        [TempData]
        public string ErrorMessage { get; set; } = string.Empty;

        public async Task OnGetAsync()
        {
            var dbSetting = await _context.SmtpSettings.FirstOrDefaultAsync();
            if (dbSetting != null)
            {
                SmtpSetting = dbSetting;
                IsLoadedFromDb = true;
            }
            else
            {
                // Fallback to appsettings.json for default initial values
                var smtpSection = _configuration.GetSection("SmtpSettings");
                SmtpSetting = new SmtpSetting
                {
                    Server = smtpSection.GetValue<string>("Server") ?? string.Empty,
                    Port = smtpSection.GetValue<int>("Port", 587),
                    Username = smtpSection.GetValue<string>("Username") ?? string.Empty,
                    Password = smtpSection.GetValue<string>("Password") ?? string.Empty,
                    EnableSsl = smtpSection.GetValue<bool>("EnableSsl", true),
                    Sender = smtpSection.GetValue<string>("Sender") ?? string.Empty
                };
                IsLoadedFromDb = false;
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (!ModelState.IsValid)
            {
                ErrorMessage = "Παρακαλώ διορθώστε τα σφάλματα στη φόρμα.";
                return Page();
            }

            var dbSetting = await _context.SmtpSettings.FirstOrDefaultAsync();
            if (dbSetting != null)
            {
                dbSetting.Server = SmtpSetting.Server?.Trim() ?? string.Empty;
                dbSetting.Port = SmtpSetting.Port;
                dbSetting.Username = SmtpSetting.Username?.Trim() ?? string.Empty;
                dbSetting.Password = SmtpSetting.Password ?? string.Empty;
                dbSetting.EnableSsl = SmtpSetting.EnableSsl;
                dbSetting.Sender = SmtpSetting.Sender?.Trim() ?? string.Empty;
                
                _context.SmtpSettings.Update(dbSetting);
            }
            else
            {
                var newSetting = new SmtpSetting
                {
                    Server = SmtpSetting.Server?.Trim() ?? string.Empty,
                    Port = SmtpSetting.Port,
                    Username = SmtpSetting.Username?.Trim() ?? string.Empty,
                    Password = SmtpSetting.Password ?? string.Empty,
                    EnableSsl = SmtpSetting.EnableSsl,
                    Sender = SmtpSetting.Sender?.Trim() ?? string.Empty
                };
                _context.SmtpSettings.Add(newSetting);
            }

            await _context.SaveChangesAsync();
            SuccessMessage = "Οι ρυθμίσεις SMTP αποθηκεύτηκαν με επιτυχία στη βάση δεδομένων.";
            return RedirectToPage();
        }
    }
}
