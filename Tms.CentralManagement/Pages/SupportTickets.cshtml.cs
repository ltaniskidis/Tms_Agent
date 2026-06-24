using System;
using System.Collections.Generic;
using System.IO;
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
    public class SupportTicketsModel : PageModel
    {
        private readonly CentralDbContext _context;
        private readonly IConfiguration _configuration;

        public SupportTicketsModel(CentralDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        public List<SupportTicket> OpenTickets { get; set; } = new();
        public List<SupportTicket> ResolvedTickets { get; set; } = new();

        [BindProperty]
        public string AdminReplyText { get; set; } = string.Empty;

        [TempData]
        public string SuccessMessage { get; set; } = string.Empty;

        [TempData]
        public string ErrorMessage { get; set; } = string.Empty;

        public string GetStatusLabel(string status)
        {
            return status switch
            {
                "Open" => "Ανοιχτό",
                "Received" => "Παραλήφθηκε",
                "Assigned" => "Ανατέθηκε",
                "UnderReview" => "Σε έλεγχο",
                "Resolved" => "Επιλύθηκε",
                _ => status
            };
        }

        public string GetStatusBadgeClass(string status)
        {
            return status switch
            {
                "Open" => "badge-danger",
                "Received" => "badge-info",
                "Assigned" => "badge-warning",
                "UnderReview" => "badge-purple",
                "Resolved" => "badge-success",
                _ => "badge-secondary"
            };
        }

        public async Task OnGetAsync()
        {
            var tickets = await _context.SupportTickets
                .OrderByDescending(t => t.CreatedDate)
                .ToListAsync();

            OpenTickets = tickets.Where(t => t.Status != "Resolved").ToList();
            ResolvedTickets = tickets.Where(t => t.Status == "Resolved").ToList();
        }

        public async Task<IActionResult> OnGetDownloadAttachmentAsync(int id)
        {
            var ticket = await _context.SupportTickets.FindAsync(id);
            if (ticket == null || string.IsNullOrEmpty(ticket.AttachmentFileName))
            {
                return NotFound("Το αρχείο δεν βρέθηκε.");
            }

            var filePath = Path.Combine(Directory.GetCurrentDirectory(), "SentEmails", ticket.AttachmentFileName);
            if (!System.IO.File.Exists(filePath))
            {
                return NotFound("Το αρχείο δεν υπάρχει στον διακομιστή.");
            }

            var contentType = "application/octet-stream";
            var ext = Path.GetExtension(ticket.AttachmentFileName).ToLower();
            if (ext == ".png") contentType = "image/png";
            else if (ext == ".jpg" || ext == ".jpeg") contentType = "image/jpeg";
            else if (ext == ".gif") contentType = "image/gif";
            else if (ext == ".pdf") contentType = "application/pdf";

            // Return physical file with clean original filename representation (removing the unique guid prefix)
            var originalName = ticket.AttachmentFileName;
            var underscoreIdx = ticket.AttachmentFileName.IndexOf('_');
            if (underscoreIdx > 0 && underscoreIdx < ticket.AttachmentFileName.Length - 1)
            {
                originalName = ticket.AttachmentFileName.Substring(underscoreIdx + 1);
            }

            return PhysicalFile(filePath, contentType, originalName);
        }

        private async Task<(bool Success, string Error, string RecipientEmails)> SendResolutionEmailAsync(SupportTicket ticket, string replyText)
        {
            // Find client profile emails to send response
            var client = await _context.Clients
                .Include(c => c.Profiles)
                .FirstOrDefaultAsync(c => c.ApiKey == ticket.ApiKey);

            var emailSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (client != null)
            {
                foreach (var p in client.Profiles)
                {
                    if (!string.IsNullOrWhiteSpace(p.Emails))
                    {
                        var parts = p.Emails.Split(',', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var part in parts)
                        {
                            var trimmed = part.Trim();
                            if (!string.IsNullOrEmpty(trimmed))
                            {
                                emailSet.Add(trimmed);
                            }
                        }
                    }
                }
            }

            string recipientEmails = string.Join(",", emailSet);

            bool emailSent = false;
            string smtpError = string.Empty;
            var toAddresses = string.IsNullOrWhiteSpace(recipientEmails) 
                ? Array.Empty<string>() 
                : recipientEmails.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(e => e.Trim()).ToArray();

            if (toAddresses.Length > 0)
            {
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
                            mail.Subject = $"[TMS Support Resolved] Απάντηση στο αίτημα: {ticket.Subject}";
                            var companyNames = client != null && client.Profiles != null && client.Profiles.Any()
                                ? string.Join(", ", client.Profiles.Select(p => p.ProfileName))
                                : "Δεν βρέθηκαν καταχωρημένα προφίλ";

                            mail.Body = $"Αγαπητέ πελάτη,\n\n" +
                                         $"Σχετικά με το αίτημα υποστήριξης που υποβάλατε με θέμα '{ticket.Subject}' στο μηχάνημα '{ticket.MachineName}' (Επιχείρηση: {companyNames}), σας ενημερώνουμε ότι έχει επιλυθεί.\n\n" +
                                         $"Απάντηση Τεχνικού:\n" +
                                         $"{replyText}\n\n" +
                                         $"Με εκτίμηση,\n" +
                                         $"Clever Data Support Team";

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
            }
            else
            {
                smtpError = "No contact emails configured for this client profile.";
            }

            if (!emailSent)
            {
                try
                {
                    var sentEmailsDir = Path.Combine(Directory.GetCurrentDirectory(), "SentEmails");
                    if (!Directory.Exists(sentEmailsDir))
                    {
                        Directory.CreateDirectory(sentEmailsDir);
                    }

                    var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    var textFile = Path.Combine(sentEmailsDir, $"ResponseEmail_{ticket.Id}_{timestamp}.txt");

                    var fileContent = $"SMTP Error / Warning: {smtpError}\n" +
                                      $"To Client Profile Emails: {recipientEmails}\n" +
                                      $"Ticket Subject: {ticket.Subject}\n" +
                                      $"Admin Response:\n{replyText}\n";

                    await System.IO.File.WriteAllTextAsync(textFile, fileContent, System.Text.Encoding.UTF8);
                }
                catch (Exception fileEx)
                {
                    Console.WriteLine($"Failed to write local response email file: {fileEx}");
                }
            }

            return (emailSent, smtpError, recipientEmails);
        }

        public async Task<IActionResult> OnPostReplyAsync(int id, string replyText)
        {
            if (string.IsNullOrWhiteSpace(replyText))
            {
                ErrorMessage = "Η απάντηση δεν μπορεί να είναι κενή.";
                return RedirectToPage();
            }

            var ticket = await _context.SupportTickets.FindAsync(id);
            if (ticket == null)
            {
                ErrorMessage = "Το αίτημα support δεν βρέθηκε.";
                return RedirectToPage();
            }

            ticket.Status = "Resolved";
            ticket.AdminResponse = replyText.Trim();
            ticket.ResponseDate = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            var (emailSent, smtpError, recipientEmails) = await SendResolutionEmailAsync(ticket, ticket.AdminResponse);

            string successMsg = $"Το αίτημα επισημάνθηκε ως Επιλυμένο.";
            if (emailSent)
            {
                successMsg += $" Στάλθηκε απαντητικό email στα: {recipientEmails}";
            }
            else
            {
                successMsg += $" Η απάντηση αποθηκεύτηκε τοπικά (σφάλμα/έλλειψη SMTP).";
                if (string.IsNullOrWhiteSpace(recipientEmails))
                {
                    successMsg += " (Δεν έχουν οριστεί emails επικοινωνίας στο προφίλ πελάτη!)";
                }
            }

            SuccessMessage = successMsg;
            return RedirectToPage();
        }

        public async Task<IActionResult> OnPostUpdateStatusAsync(int id, string status)
        {
            var ticket = await _context.SupportTickets.FindAsync(id);
            if (ticket == null)
            {
                ErrorMessage = "Το αίτημα support δεν βρέθηκε.";
                return RedirectToPage();
            }

            var oldStatus = ticket.Status;
            ticket.Status = status;

            bool sentEmail = false;
            string recipientEmails = "";

            if (status == "Resolved")
            {
                ticket.ResponseDate = DateTime.UtcNow;
                if (string.IsNullOrEmpty(ticket.AdminResponse))
                {
                    ticket.AdminResponse = "Το αίτημα επισημάνθηκε ως Επιλυμένο.";
                }

                var (emailSent, smtpError, emails) = await SendResolutionEmailAsync(ticket, ticket.AdminResponse);
                sentEmail = emailSent;
                recipientEmails = emails;
            }
            else
            {
                if (ticket.AdminResponse == "Το αίτημα επισημάνθηκε ως Επιλυμένο.")
                {
                    ticket.AdminResponse = null;
                }
            }

            await _context.SaveChangesAsync();

            string msg = $"Η κατάσταση του αιτήματος άλλαξε από '{GetStatusLabel(oldStatus)}' σε '{GetStatusLabel(status)}'.";
            if (status == "Resolved")
            {
                if (sentEmail)
                {
                    msg += $" Στάλθηκε απαντητικό email στα: {recipientEmails}";
                }
                else
                {
                    msg += $" Η απάντηση αποθηκεύτηκε τοπικά.";
                }
            }
            SuccessMessage = msg;

            return RedirectToPage();
        }
    }
}
