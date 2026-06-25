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
    public class VersionsModel : PageModel
    {
        private readonly CentralDbContext _context;

        public VersionsModel(CentralDbContext context)
        {
            _context = context;
        }

        public IList<VersionInfo> Versions { get; set; } = default!;

        [BindProperty]
        public string VersionNumber { get; set; } = string.Empty;

        [BindProperty]
        public string Description { get; set; } = string.Empty;

        [BindProperty]
        public string BinaryFileUrl { get; set; } = string.Empty;

        [BindProperty]
        public string? SecurityCode { get; set; }

        [BindProperty]
        public IFormFile? UploadedPackage { get; set; }

        [BindProperty]
        public IFormFile? UploadedScriptsFile { get; set; }

        [BindProperty]
        public string ReleaseNotesText { get; set; } = string.Empty;

        [BindProperty]
        public string ScriptName { get; set; } = "01_update.sql";

        [BindProperty]
        public string ScriptContent { get; set; } = string.Empty;

        [BindProperty]
        public string TargetType { get; set; } = "Program"; // "System" or "Program"

        [BindProperty]
        public string AffectedSystemComponent { get; set; } = "Both"; // "Console", "Agent", "Both"

        [BindProperty]
        public bool IsCurrent { get; set; } = false;

        public async Task OnGetAsync()
        {
            var rawVersions = await _context.Versions
                .Include(v => v.Scripts)
                .Include(v => v.ReleaseNotes)
                .ToListAsync();

            Versions = rawVersions
                .OrderByDescending(v => Version.TryParse(v.VersionNumber, out var ver) ? ver : new Version(0, 0, 0))
                .ToList();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrWhiteSpace(VersionNumber))
            {
                ModelState.AddModelError("VersionNumber", "Ο αριθμός έκδοσης είναι υποχρεωτικός.");
            }

            if (!ModelState.IsValid)
            {
                await OnGetAsync();
                return Page();
            }

            if (TargetType == "System" && IsCurrent)
            {
                // Unmark other versions
                var otherCurrentVersions = await _context.Versions.Where(v => v.IsCurrent).ToListAsync();
                foreach (var v in otherCurrentVersions)
                {
                    v.IsCurrent = false;
                }
            }

            // Handle uploaded scripts file
            if (UploadedScriptsFile != null && UploadedScriptsFile.Length > 0)
            {
                using var reader = new System.IO.StreamReader(UploadedScriptsFile.OpenReadStream());
                ScriptContent = await reader.ReadToEndAsync();
                ScriptName = UploadedScriptsFile.FileName;
            }

            string finalUrl = BinaryFileUrl.Trim();
            
            if (UploadedPackage != null && UploadedPackage.Length > 0)
            {
                var packagesDir = Helpers.PathHelper.GetPublishAndSetupPath();
                if (!Directory.Exists(packagesDir))
                {
                    Directory.CreateDirectory(packagesDir);
                }

                var fileName = $"app_{VersionNumber.Trim()}.zip";
                var filePath = Path.Combine(packagesDir, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await UploadedPackage.CopyToAsync(stream);
                }

                finalUrl = $"/packages/{fileName}";
            }
            else if (string.IsNullOrWhiteSpace(finalUrl))
            {
                if (!string.IsNullOrWhiteSpace(ScriptContent))
                {
                    finalUrl = string.Empty;
                }
                else
                {
                    finalUrl = $"/packages/app_{VersionNumber.Trim()}.zip";
                }
            }

            var version = new VersionInfo
            {
                VersionNumber = VersionNumber.Trim(),
                ReleaseDate = DateTime.UtcNow,
                Description = Description.Trim(),
                BinaryFileUrl = finalUrl,
                SecurityCode = SecurityCode?.Trim(),
                TargetType = TargetType.Trim(),
                AffectedSystemComponent = TargetType == "System" ? AffectedSystemComponent.Trim() : "Both",
                IsCurrent = TargetType == "System" ? IsCurrent : false,
                IsActive = true
            };

            // Parse release notes from newline
            if (!string.IsNullOrWhiteSpace(ReleaseNotesText))
            {
                var lines = ReleaseNotesText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines)
                {
                    version.ReleaseNotes.Add(new ReleaseNote { NotesContent = line.Trim() });
                }
            }

            

            // Parse SQL scripts from textarea or uploaded file
            if (!string.IsNullOrWhiteSpace(ScriptContent))
            {
                version.Scripts.Add(new SqlScript
                {
                    ScriptName = string.IsNullOrWhiteSpace(ScriptName) ? "01_update.sql" : ScriptName.Trim(),
                    ScriptContent = ScriptContent,
                    SequenceOrder = 1
                });
            }

            _context.Versions.Add(version);
            await _context.SaveChangesAsync();

            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostToggleAsync(int id)
        {
            var version = await _context.Versions.FindAsync(id);
            if (version != null)
            {
                version.IsActive = !version.IsActive;
                await _context.SaveChangesAsync();
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            var version = await _context.Versions.FindAsync(id);
            if (version != null)
            {
                _context.Versions.Remove(version);
                await _context.SaveChangesAsync();
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostSetCurrentAsync(int id)
        {
            var targetVersion = await _context.Versions.FindAsync(id);
            if (targetVersion != null && targetVersion.TargetType == "System")
            {
                var allVersions = await _context.Versions.ToListAsync();
                foreach (var v in allVersions)
                {
                    v.IsCurrent = (v.Id == targetVersion.Id);
                }
                await _context.SaveChangesAsync();
            }
            return RedirectToPage();
        }
    }
}
