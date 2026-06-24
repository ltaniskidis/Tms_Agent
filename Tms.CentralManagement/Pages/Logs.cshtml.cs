using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Tms.CentralManagement.Data;

namespace Tms.CentralManagement.Pages
{
    public class LogsModel : PageModel
    {
        private readonly CentralDbContext _context;

        public LogsModel(CentralDbContext context)
        {
            _context = context;
        }

        public IList<LogVm> Logs { get;set; } = default!;

        public async Task OnGetAsync()
        {
            Logs = await (from log in _context.UpdateLogs
                          join profile in _context.ClientProfiles on log.ClientProfileId equals profile.Id
                          join client in _context.Clients on profile.ClientMachineId equals client.Id
                          select new LogVm
                          {
                              Id = log.Id,
                              MachineName = client.MachineName,
                              ProfileName = profile.ProfileName,
                              Afm = profile.Afm,
                              VersionNumber = log.VersionNumber,
                              ExecutionTime = log.ExecutionTime,
                              Success = log.Success,
                              ErrorMessage = log.ErrorMessage,
                              LogDetails = log.LogDetails
                          })
                          .OrderByDescending(l => l.ExecutionTime)
                          .ToListAsync();
        }
    }

    public class LogVm
    {
        public int Id { get; set; }
        public string MachineName { get; set; } = string.Empty;
        public string ProfileName { get; set; } = string.Empty;
        public string Afm { get; set; } = string.Empty;
        public string VersionNumber { get; set; } = string.Empty;
        public DateTime ExecutionTime { get; set; }
        public bool Success { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;
        public string LogDetails { get; set; } = string.Empty;
    }
}
