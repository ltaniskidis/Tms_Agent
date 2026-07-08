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
    public class ErpChangelogModel : PageModel
    {
        private readonly CentralDbContext _context;

        public ErpChangelogModel(CentralDbContext context)
        {
            _context = context;
        }

        public IList<VersionInfo> Versions { get; set; } = default!;

        [BindProperty]
        public int? EditingVersionId { get; set; }

        [BindProperty]
        public string VersionNumber { get; set; } = string.Empty;

        [BindProperty]
        public string? Description { get; set; }

        [BindProperty]
        public DateTime ReleaseDate { get; set; } = DateTime.UtcNow;

        [BindProperty]
        public string? ReleaseNotesText { get; set; }

        [BindProperty]
        public string? BinaryFileUrl { get; set; }

        [BindProperty]
        public string? SecurityCode { get; set; }

        [BindProperty]
        public string? ScriptName { get; set; } = "01_update.sql";

        [BindProperty]
        public string? ScriptContent { get; set; }

        public async Task OnGetAsync(int? editId)
        {
            await LoadVersionsAsync();

            if (editId.HasValue)
            {
                var editVersion = await _context.Versions
                    .Include(v => v.ReleaseNotes)
                    .Include(v => v.Scripts)
                    .FirstOrDefaultAsync(v => v.Id == editId.Value && v.TargetType == "Program");

                if (editVersion != null)
                {
                    EditingVersionId = editVersion.Id;
                    VersionNumber = editVersion.VersionNumber;
                    Description = editVersion.Description;
                    ReleaseDate = editVersion.ReleaseDate;
                    BinaryFileUrl = editVersion.BinaryFileUrl;
                    SecurityCode = editVersion.SecurityCode;
                    
                    ReleaseNotesText = string.Join(Environment.NewLine, editVersion.ReleaseNotes.Select(n => n.NotesContent));
                    
                    var firstScript = editVersion.Scripts.FirstOrDefault();
                    if (firstScript != null)
                    {
                        ScriptName = firstScript.ScriptName;
                        ScriptContent = firstScript.ScriptContent;
                    }
                    else
                    {
                        ScriptName = "01_update.sql";
                        ScriptContent = string.Empty;
                    }
                }
            }
        }

        public async Task<IActionResult> OnPostAsync()
        {
            if (string.IsNullOrWhiteSpace(VersionNumber))
            {
                ModelState.AddModelError("VersionNumber", "Ο αριθμός έκδοσης είναι υποχρεωτικός.");
            }

            if (!ModelState.IsValid)
            {
                await LoadVersionsAsync();
                return Page();
            }

            VersionInfo version;

            if (EditingVersionId.HasValue && EditingVersionId.Value > 0)
            {
                // Edit existing version
                version = await _context.Versions
                    .Include(v => v.ReleaseNotes)
                    .Include(v => v.Scripts)
                    .FirstOrDefaultAsync(v => v.Id == EditingVersionId.Value && v.TargetType == "Program");

                if (version == null)
                {
                    return NotFound();
                }

                version.VersionNumber = VersionNumber.Trim();
                version.ReleaseDate = ReleaseDate;
                version.Description = (Description ?? string.Empty).Trim();
                version.BinaryFileUrl = (BinaryFileUrl ?? string.Empty).Trim();
                version.SecurityCode = SecurityCode?.Trim();

                // Update Release Notes: Clear and recreate
                _context.ReleaseNotes.RemoveRange(version.ReleaseNotes);
                version.ReleaseNotes.Clear();
                if (!string.IsNullOrWhiteSpace(ReleaseNotesText))
                {
                    var lines = ReleaseNotesText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        version.ReleaseNotes.Add(new ReleaseNote { NotesContent = line.Trim() });
                    }
                }

                // Update Script: Clear and recreate if there is content
                _context.SqlScripts.RemoveRange(version.Scripts);
                version.Scripts.Clear();
                if (!string.IsNullOrWhiteSpace(ScriptContent))
                {
                    version.Scripts.Add(new SqlScript
                    {
                        ScriptName = string.IsNullOrWhiteSpace(ScriptName) ? "01_update.sql" : ScriptName.Trim(),
                        ScriptContent = ScriptContent,
                        SequenceOrder = 1
                    });
                }
            }
            else
            {
                // Create new version
                version = new VersionInfo
                {
                    VersionNumber = VersionNumber.Trim(),
                    ReleaseDate = ReleaseDate,
                    Description = (Description ?? string.Empty).Trim(),
                    BinaryFileUrl = (BinaryFileUrl ?? string.Empty).Trim(),
                    SecurityCode = SecurityCode?.Trim(),
                    TargetType = "Program",
                    AffectedSystemComponent = "Both",
                    IsCurrent = false,
                    IsActive = true
                };

                if (!string.IsNullOrWhiteSpace(ReleaseNotesText))
                {
                    var lines = ReleaseNotesText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        version.ReleaseNotes.Add(new ReleaseNote { NotesContent = line.Trim() });
                    }
                }

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
            }

            await _context.SaveChangesAsync();
            return RedirectToPage("ErpChangelog");
        }

        public async Task<IActionResult> OnPostToggleAsync(int id)
        {
            var version = await _context.Versions.FindAsync(id);
            if (version != null && version.TargetType == "Program")
            {
                version.IsActive = !version.IsActive;
                await _context.SaveChangesAsync();
            }
            return RedirectToPage("ErpChangelog");
        }

        public async Task<IActionResult> OnPostDeleteAsync(int id)
        {
            var version = await _context.Versions
                .Include(v => v.Scripts)
                .Include(v => v.ReleaseNotes)
                .FirstOrDefaultAsync(v => v.Id == id);

            if (version != null && version.TargetType == "Program")
            {
                _context.Versions.Remove(version);
                await _context.SaveChangesAsync();
            }
            return RedirectToPage("ErpChangelog");
        }

        private async Task LoadVersionsAsync()
        {
            var rawVersions = await _context.Versions
                .Where(v => v.TargetType == "Program")
                .Include(v => v.Scripts)
                .Include(v => v.ReleaseNotes)
                .ToListAsync();

            Versions = rawVersions
                .OrderByDescending(v => Version.TryParse(v.VersionNumber, out var ver) ? ver : new Version(0, 0, 0))
                .ToList();
        }
    }
}
