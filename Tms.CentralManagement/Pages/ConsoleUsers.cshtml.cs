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
    public class ConsoleUsersModel : PageModel
    {
        private readonly CentralDbContext _context;

        public ConsoleUsersModel(CentralDbContext context)
        {
            _context = context;
        }

        public List<ConsoleUser> Users { get; set; } = new();

        [TempData]
        public string SuccessMessage { get; set; } = string.Empty;

        [TempData]
        public string ErrorMessage { get; set; } = string.Empty;

        public async Task OnGetAsync()
        {
            Users = await _context.ConsoleUsers.ToListAsync();
        }

        public async Task<IActionResult> OnPostAddUserAsync(string username, string password, string role, string scope)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
            {
                ErrorMessage = "Το όνομα χρήστη και ο κωδικός πρόσβασης είναι υποχρεωτικά.";
                return RedirectToPage();
            }

            var trimmedUsername = username.Trim();
            if (await _context.ConsoleUsers.AnyAsync(u => u.Username.ToLower() == trimmedUsername.ToLower()))
            {
                ErrorMessage = $"Υπάρχει ήδη χρήστης με το όνομα '{trimmedUsername}'.";
                return RedirectToPage();
            }

            var newUser = new ConsoleUser
            {
                Username = trimmedUsername,
                PasswordHash = password.Trim(), // plaintext as per database configuration
                Role = role ?? "Operator",
                Scope = scope ?? "Console"
            };

            _context.ConsoleUsers.Add(newUser);
            await _context.SaveChangesAsync();

            SuccessMessage = $"Ο χρήστης '{trimmedUsername}' προστέθηκε με επιτυχία.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostEditUserAsync(int userId, string password, string role, string scope)
        {
            var user = await _context.ConsoleUsers.FindAsync(userId);
            if (user == null)
            {
                ErrorMessage = "Ο χρήστης δεν βρέθηκε.";
                return RedirectToPage();
            }

            // Update password if provided
            if (!string.IsNullOrWhiteSpace(password))
            {
                user.PasswordHash = password.Trim();
            }

            // Update role (cannot change own role if it would lock them out, but we allow simple edit)
            user.Role = role ?? "Operator";
            user.Scope = scope ?? "Console";

            await _context.SaveChangesAsync();
            SuccessMessage = $"Τα στοιχεία του χρήστη '{user.Username}' ενημερώθηκαν με επιτυχία.";
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostDeleteUserAsync(int userId)
        {
            var user = await _context.ConsoleUsers.FindAsync(userId);
            if (user == null)
            {
                ErrorMessage = "Ο χρήστης δεν βρέθηκε.";
                return RedirectToPage();
            }

            // Prevent self-deletion
            if (User.Identity?.Name?.Equals(user.Username, StringComparison.OrdinalIgnoreCase) == true)
            {
                ErrorMessage = "Δεν μπορείτε να διαγράψετε τον εαυτό σας.";
                return RedirectToPage();
            }

            _context.ConsoleUsers.Remove(user);
            await _context.SaveChangesAsync();

            SuccessMessage = $"Ο χρήστης '{user.Username}' διαγράφηκε με επιτυχία.";
            return RedirectToPage();
        }
    }
}
