using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Tms.CentralManagement.Data;

namespace Tms.CentralManagement.Pages
{
    [Authorize(Roles = "SuperAdmin")]
    public class BroadcastsModel : PageModel
    {
        private readonly CentralDbContext _context;

        public BroadcastsModel(CentralDbContext context)
        {
            _context = context;
        }

        public List<BroadcastMessage> Broadcasts { get; set; } = new();

        [TempData]
        public string SuccessMessage { get; set; } = string.Empty;

        [TempData]
        public string ErrorMessage { get; set; } = string.Empty;

        public async Task OnGetAsync()
        {
            Broadcasts = await _context.BroadcastMessages
                .OrderByDescending(b => b.CreatedDate)
                .ToListAsync();
        }

        public async Task<IActionResult> OnPostAddBroadcastAsync(string title, string content, string targetClientApiKey)
        {
            if (string.IsNullOrWhiteSpace(title) || string.IsNullOrWhiteSpace(content))
            {
                ErrorMessage = "Ο τίτλος και το περιεχόμενο είναι υποχρεωτικά.";
                return RedirectToPage();
            }

            var newBroadcast = new BroadcastMessage
            {
                Title = title.Trim(),
                Content = content.Trim(),
                CreatedDate = DateTime.UtcNow,
                IsActive = true,
                TargetClientApiKey = targetClientApiKey?.Trim() ?? string.Empty
            };

            _context.BroadcastMessages.Add(newBroadcast);
            await _context.SaveChangesAsync();

            SuccessMessage = "Η ενημέρωση/διαφήμιση δημιουργήθηκε επιτυχώς.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostToggleActiveAsync(int id)
        {
            var msg = await _context.BroadcastMessages.FindAsync(id);
            if (msg != null)
            {
                msg.IsActive = !msg.IsActive;
                await _context.SaveChangesAsync();
                SuccessMessage = msg.IsActive ? "Η ενημέρωση ενεργοποιήθηκε." : "Η ενημέρωση απενεργοποιήθηκε.";
            }
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteBroadcastAsync(int id)
        {
            var msg = await _context.BroadcastMessages.FindAsync(id);
            if (msg != null)
            {
                _context.BroadcastMessages.Remove(msg);
                await _context.SaveChangesAsync();
                SuccessMessage = "Η ενημέρωση διαγράφηκε επιτυχώς.";
            }
            return RedirectToPage();
        }
    }
}
