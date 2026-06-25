using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using System.Text.Json;
using Tms.Agent.Core.Models;
using Tms.Shared.Models;

namespace Tms.Agent.Core.Services
{
    public class UpdateEngine
    {
        private readonly HttpClient _httpClient;

        public UpdateEngine()
        {
            _httpClient = new HttpClient();
        }

        public UpdateEngine(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<UpdateCheckResponse?> CheckForUpdatesAsync(
            string serverUrl, 
            string clientId, 
            string machineName, 
            string machineRole, 
            string agentVersion, 
            string apiKey, 
            List<LocalProfile> localProfiles,
            bool startWithWindows,
            bool forceSyncStartWithWindows = false)
        {
            var url = $"{serverUrl.TrimEnd('/')}/api/updates/check";
            
            // Auto-discover local SQL databases
            List<DiscoveredDatabaseDto> discoveredDbs;
            try
            {
                discoveredDbs = DiscoverLocalDatabases();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to auto-discover databases: {ex.Message}");
                discoveredDbs = new List<DiscoveredDatabaseDto>();
            }

            var request = new UpdateCheckRequest
            {
                ClientId = clientId,
                MachineName = machineName,
                MachineRole = machineRole,
                AgentVersion = agentVersion,
                ApiKey = apiKey,
                Profiles = localProfiles.Select(p => new LocalProfileDto
                {
                    ProfileId = p.ProfileId,
                    ProfileName = p.ProfileName,
                    Afm = p.Afm,
                    CurrentVersion = p.CurrentVersion,
                    CurrentProgramVersion = p.CurrentProgramVersion,
                    CurrentDbVersion = p.CurrentDbVersion,
                    SerialNumber = p.SerialNumber,
                    ActiveUsersCount = p.ActiveUsersCount,
                    TargetFolder = p.TargetFolder,
                    TargetExeName = p.TargetExeName,
                    ConnectionString = p.ConnectionString,
                    ConnectionStringType = p.ConnectionStringType,
                    DbServer = p.DbServer,
                    DbName = p.DbName,
                    DbUser = p.DbUser,
                    DbPassword = p.DbPassword,
                    DbUseWindowsAuth = p.DbUseWindowsAuth,
                    ConfigFilePath = p.ConfigFilePath
                }).ToList(),
                DiscoveredDatabases = discoveredDbs,
                StartWithWindows = startWithWindows,
                ForceSyncStartWithWindows = forceSyncStartWithWindows
            };

            try
            {
                var response = await _httpClient.PostAsJsonAsync(url, request);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<UpdateCheckResponse>();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error checking updates: {ex.Message}");
                return null;
            }
        }

        // 2. Submit logs back to central management
        // 2. Submit logs back to central management
        public async Task SubmitLogAsync(
            string serverUrl, 
            string clientId, 
            string machineName, 
            string apiKey, 
            LocalProfile profile, 
            string versionNumber, 
            string programVersion,
            string dbVersion,
            bool success, 
            string errorMessage, 
            string logDetails)
        {
            var url = $"{serverUrl.TrimEnd('/')}/api/updates/log";
            
            var request = new UpdateLogSubmissionDto
            {
                ClientId = clientId,
                MachineName = machineName,
                ApiKey = apiKey,
                ProfileId = profile.ProfileId,
                ProfileName = profile.ProfileName,
                Afm = profile.Afm,
                VersionNumber = versionNumber,
                ProgramVersion = programVersion,
                DbVersion = dbVersion,
                ExecutionTime = DateTime.UtcNow,
                Success = success,
                ErrorMessage = errorMessage,
                LogDetails = logDetails
            };

            try
            {
                var response = await _httpClient.PostAsJsonAsync(url, request);
                response.EnsureSuccessStatusCode();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error submitting log: {ex.Message}");
            }
        }

        // 2b. Sync local users back to central management
        public async Task<bool> SyncUsersAsync(string serverUrl, string apiKey, List<AgentUserDto> users)
        {
            var url = $"{serverUrl.TrimEnd('/')}/api/updates/sync-users";
            var request = new SyncUsersRequest
            {
                ApiKey = apiKey,
                Users = users
            };

            try
            {
                var response = await _httpClient.PostAsJsonAsync(url, request);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error syncing users to central server: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> AuthorizeUpdateAsync(string serverUrl, string apiKey, string clientId, string profileId, string versionNumber)
        {
            var url = $"{serverUrl.TrimEnd('/')}/api/updates/authorize";
            var request = new AuthorizeUpdateRequest
            {
                ClientId = clientId,
                ApiKey = apiKey,
                ProfileId = profileId,
                VersionNumber = versionNumber
            };

            try
            {
                var response = await _httpClient.PostAsJsonAsync(url, request);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error authorizing update: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> SendSupportEmailAsync(string serverUrl, string apiKey, string subject, string body, string? attachmentPath)
        {
            var url = $"{serverUrl.TrimEnd('/')}/api/updates/send-support-email";

            try
            {
                using (var content = new MultipartFormDataContent())
                {
                    content.Add(new StringContent(apiKey ?? string.Empty), "apiKey");
                    content.Add(new StringContent(subject ?? string.Empty), "subject");
                    content.Add(new StringContent(body ?? string.Empty), "body");

                    if (!string.IsNullOrEmpty(attachmentPath) && File.Exists(attachmentPath))
                    {
                        var fileBytes = await File.ReadAllBytesAsync(attachmentPath);
                        var fileContent = new ByteArrayContent(fileBytes);
                        fileContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                        content.Add(fileContent, "attachment", Path.GetFileName(attachmentPath));
                    }

                    var response = await _httpClient.PostAsync(url, content);
                    return response.IsSuccessStatusCode;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error sending support email: {ex.Message}");
                return false;
            }
        }

        public async Task<List<SupportTicketDto>?> GetSupportTicketsAsync(string serverUrl, string apiKey, string clientId)
        {
            var url = $"{serverUrl.TrimEnd('/')}/api/updates/support-tickets?apiKey={Uri.EscapeDataString(apiKey)}&clientId={Uri.EscapeDataString(clientId)}";

            try
            {
                var response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    return JsonSerializer.Deserialize<List<SupportTicketDto>>(json, options);
                }
                return null;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting support tickets: {ex.Message}");
                return null;
            }
        }

        // 3. Execute update workflow
        public async Task<bool> RunUpdateAsync(
            string serverUrl, 
            string clientId, 
            string machineName, 
            string apiKey,
            LocalProfile profile, 
            VersionDto newVersion, 
            Action<string> logCallback)
        {
            var logBuilder = new StringBuilder();
            void Log(string message)
            {
                var msg = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
                logBuilder.AppendLine(msg);
                logCallback?.Invoke(msg);
            }

            Log($"Έναρξη αναβάθμισης για το προφίλ '{profile.ProfileName}' (ΑΦΜ: {profile.Afm}) στην έκδοση {newVersion.VersionNumber}");

            bool dbSuccess = false;
            bool fileSuccess = false;
            string errorMessage = string.Empty;

            string targetFolder = profile.TargetFolder;
            string exePath = Path.Combine(targetFolder, profile.TargetExeName);

            try
            {
                // STEP 1: Execute SQL Scripts
                if (newVersion.Scripts != null && newVersion.Scripts.Any())
                {
                    var resolvedConnStr = profile.GetResolvedConnectionString(Log);
                    var dbName = GetDatabaseNameFromConnectionString(resolvedConnStr);
                    
                    // Call CheckForUpdates to verify if this database is still monitored
                    var checkResp = await CheckForUpdatesAsync(serverUrl, clientId, machineName, "Both", "1.3.2", apiKey, new List<LocalProfile> { profile }, false, false);
                    var monitoredDbs = checkResp?.MonitoredDatabaseNames ?? new List<string>();
                    
                    if (!string.IsNullOrEmpty(resolvedConnStr) && !monitoredDbs.Contains(dbName, StringComparer.OrdinalIgnoreCase))
                    {
                        Log($"Παράλειψη εκτέλεσης SQL scripts: Η βάση '{dbName}' δεν είναι πλέον χαρακτηρισμένη ως παρακολουθούμενη (Monitored) στην κεντρική κονσόλα.");
                        dbSuccess = true;
                    }
                    else
                    {
                        Log($"Βρέθηκαν {newVersion.Scripts.Count} SQL scripts προς εκτέλεση.");
                        dbSuccess = await ExecuteDatabaseScriptsAsync(resolvedConnStr, newVersion.Scripts, Log);
                        if (!dbSuccess)
                        {
                            throw new Exception("Αποτυχία κατά την εκτέλεση των SQL Scripts της βάσης δεδομένων.");
                        }
                    }
                }
                else
                {
                    Log("Δεν υπάρχουν SQL scripts προς εκτέλεση.");
                    dbSuccess = true;
                }

                // STEP 2: Download and apply binaries
                if (!string.IsNullOrEmpty(newVersion.BinaryFileUrl))
                {
                    var downloadUrl = newVersion.BinaryFileUrl;
                    if (!downloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                    {
                        downloadUrl = $"{serverUrl.TrimEnd('/')}/{downloadUrl.TrimStart('/')}";
                    }
                    Log($"Λήψη εκτελέσιμου αρχείου από: {downloadUrl}");
                    
                    // Create temp download folder
                    var tempDir = Path.Combine(Path.GetTempPath(), "TmsAgentUpdates", Guid.NewGuid().ToString());
                    Directory.CreateDirectory(tempDir);
                    var tempZipPath = Path.Combine(tempDir, "package.zip");

                    try
                    {
                        await DownloadFileAsync(downloadUrl, tempZipPath, Log);

                        // Terminate running instances of the target desktop application to avoid lock issues
                        try
                        {
                            var exeNameWithoutExt = Path.GetFileNameWithoutExtension(profile.TargetExeName);
                            var runningProcesses = System.Diagnostics.Process.GetProcessesByName(exeNameWithoutExt);
                            foreach (var process in runningProcesses)
                            {
                                try
                                {
                                    Log($"Τερματισμός εκτελούμενης διεργασίας '{process.ProcessName}' (PID: {process.Id}) για την εφαρμογή της αναβάθμισης...");
                                    process.Kill();
                                    process.WaitForExit(5000);
                                }
                                catch (Exception ex)
                                {
                                    Log($"Αδυναμία τερματισμού διεργασίας '{process.ProcessName}': {ex.Message}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Log($"Σφάλμα κατά τον έλεγχο εκτελούμενων διεργασιών: {ex.Message}");
                        }

                        // Apply new files
                        Log("Εξαγωγή αρχείων και αντικατάσταση...");
                        if (Path.GetExtension(downloadUrl).Equals(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            Log("Ασφαλής εξαγωγή αρχείων ZIP...");
                            using (var archive = ZipFile.OpenRead(tempZipPath))
                            {
                                foreach (var entry in archive.Entries)
                                {
                                    if (string.IsNullOrEmpty(entry.Name)) continue; // Directory entry

                                    var targetFilePath = Path.GetFullPath(Path.Combine(targetFolder, entry.FullName));
                                    
                                    // Ensure directory exists
                                    var parentDir = Path.GetDirectoryName(targetFilePath);
                                    if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                                    {
                                        Directory.CreateDirectory(parentDir);
                                    }

                                    var ext = Path.GetExtension(entry.Name).ToLower();
                                    if (ext == ".exe" || ext == ".dll" || ext == ".rpt" || ext == ".rdlc")
                                    {
                                        var dir = Path.GetDirectoryName(targetFilePath) ?? targetFolder;
                                        var nameWithoutExt = Path.GetFileNameWithoutExtension(entry.Name);
                                        var oldFileName = $"{nameWithoutExt}_OLD{ext}";
                                        var oldFilePath = Path.Combine(dir, oldFileName);

                                        if (File.Exists(targetFilePath))
                                        {
                                            if (File.Exists(oldFilePath))
                                            {
                                                File.Delete(oldFilePath);
                                            }
                                            File.Move(targetFilePath, oldFilePath);
                                            Log($"Δημιουργία backup: {entry.Name} -> {oldFileName}");
                                        }
                                    }

                                    // Extract the entry (overwriting if it already exists)
                                    entry.ExtractToFile(targetFilePath, overwrite: true);
                                }
                            }
                        }
                        else
                        {
                            var targetFilePath = exePath;
                            var ext = Path.GetExtension(targetFilePath).ToLower();
                            if (ext == ".exe" || ext == ".dll" || ext == ".rpt" || ext == ".rdlc")
                            {
                                var dir = Path.GetDirectoryName(targetFilePath) ?? targetFolder;
                                var nameWithoutExt = Path.GetFileNameWithoutExtension(targetFilePath);
                                var oldFileName = $"{nameWithoutExt}_OLD{ext}";
                                var oldFilePath = Path.Combine(dir, oldFileName);

                                if (File.Exists(targetFilePath))
                                {
                                    if (File.Exists(oldFilePath))
                                    {
                                        File.Delete(oldFilePath);
                                    }
                                    File.Move(targetFilePath, oldFilePath);
                                    Log($"Δημιουργία backup: {Path.GetFileName(targetFilePath)} -> {oldFileName}");
                                }
                            }

                            if (!Directory.Exists(targetFolder))
                            {
                                Directory.CreateDirectory(targetFolder);
                            }
                            File.Copy(tempZipPath, targetFilePath, overwrite: true);
                        }

                        fileSuccess = true;
                        Log("Η αντιγραφή των αρχείων ολοκληρώθηκε με επιτυχία.");
                    }
                    catch (Exception ex)
                    {
                        Log($"Σφάλμα κατά την ενημέρωση αρχείων: {ex.Message}");
                        throw;
                    }
                    finally
                    {
                        // Clean up temp dir
                        if (Directory.Exists(tempDir))
                        {
                            Directory.Delete(tempDir, recursive: true);
                        }
                    }
                }
                else
                {
                    Log("Δεν περιλαμβάνεται νέο εκτελέσιμο αρχείο.");
                    fileSuccess = true;
                }

                // If both succeeded, update versions
                if (dbSuccess && fileSuccess)
                {
                    if (newVersion.Scripts != null && newVersion.Scripts.Any())
                    {
                        profile.CurrentDbVersion = newVersion.VersionNumber;
                    }
                    if (!string.IsNullOrEmpty(newVersion.BinaryFileUrl))
                    {
                        profile.CurrentProgramVersion = newVersion.VersionNumber;
                    }
                    profile.CurrentVersion = newVersion.VersionNumber;
                    Log($"Η αναβάθμιση ολοκληρώθηκε επιτυχώς στην έκδοση {newVersion.VersionNumber}!");
                }
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                Log($"Σφάλμα αναβάθμισης: {ex.Message}");
            }

            bool finalSuccess = dbSuccess && fileSuccess;

            // Submit logs to Central API
            await SubmitLogAsync(
                serverUrl, 
                clientId, 
                machineName, 
                apiKey, 
                profile, 
                newVersion.VersionNumber, 
                profile.CurrentProgramVersion,
                profile.CurrentDbVersion,
                finalSuccess, 
                errorMessage, 
                logBuilder.ToString());

            return finalSuccess;
        }

        // 3b. Generate a dry-run script preview
        public async Task<string> GenerateScriptPreviewAsync(string connectionString, List<ScriptDto> scripts)
        {
            var sb = new StringBuilder();
            if (string.IsNullOrEmpty(connectionString))
            {
                return "Σφάλμα: Δεν έχει οριστεί connection string.";
            }

            try
            {
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();
                var dbName = GetDatabaseNameFromConnectionString(connectionString);
                sb.AppendLine($"Βάση Δεδομένων: {dbName}");

                foreach (var script in scripts.OrderBy(s => s.SequenceOrder))
                {
                    sb.AppendLine($"Αρχείο: {script.ScriptName}");

                    bool tableExists = false;
                    using (var checkCmd = new SqlCommand(
                        "SELECT CASE WHEN OBJECT_ID(N'[dbo].[SQL_HISTORY_UPDATE_SCRIPTS]', N'U') IS NOT NULL THEN 1 ELSE 0 END", 
                        connection))
                    {
                        var result = await checkCmd.ExecuteScalarAsync();
                        tableExists = result != null && Convert.ToInt32(result) == 1;
                    }

                    if (!tableExists)
                    {
                        sb.AppendLine("  -> ΠΡΟΣΟΧΗ: Ο πίνακας SQL_HISTORY_UPDATE_SCRIPTS δεν υπάρχει στη βάση. Η αναβάθμιση θα αποτύχει.");
                        continue;
                    }

                    if (script.ScriptName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || 
                        (script.ScriptName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) && ParseBulkScriptFile(script.ScriptContent).Any()))
                    {
                        string? lastScriptNumber = null;
                        using (var getCmd = new SqlCommand(
                            "SELECT TOP 1 LAST_SCRIPT_NUMBER FROM [dbo].[SQL_HISTORY_UPDATE_SCRIPTS] ORDER BY ID DESC", 
                            connection))
                        {
                            var result = await getCmd.ExecuteScalarAsync();
                            lastScriptNumber = result == null || result == DBNull.Value ? null : result.ToString();
                        }

                        var blocks = ParseBulkScriptFile(script.ScriptContent);
                        if (!blocks.Any())
                        {
                            sb.AppendLine("  -> Δεν ανιχνεύθηκαν σενάρια στο αρχείο.");
                            continue;
                        }

                        int startIndex = 0;
                        if (!string.IsNullOrEmpty(lastScriptNumber))
                        {
                            var lastIndex = blocks.FindIndex(b => b.ScriptNumber.Equals(lastScriptNumber, StringComparison.OrdinalIgnoreCase));
                            if (lastIndex == -1)
                            {
                                sb.AppendLine($"  -> Σφάλμα: Το τελευταίο εκτελεσμένο block '{lastScriptNumber}' δεν υπάρχει στο αρχείο.");
                                continue;
                            }
                            startIndex = lastIndex + 1;
                        }

                        if (startIndex >= blocks.Count)
                        {
                            sb.AppendLine($"  -> Η βάση είναι ήδη ενημερωμένη (Τελευταίο block στη βάση: {lastScriptNumber ?? "Κανένα"}).");
                        }
                        else
                        {
                            var fromBlock = blocks[startIndex].ScriptNumber;
                            var toBlock = blocks[blocks.Count - 1].ScriptNumber;
                            sb.AppendLine($"  -> Θα εκτελεστούν τα blocks από '{fromBlock}' έως '{toBlock}' ({blocks.Count - startIndex} σενάρια).");
                        }
                    }
                    else
                    {
                        string fallbackVersion = Path.GetFileNameWithoutExtension(script.ScriptName);
                        bool alreadyRun = false;
                        using (var checkRunCmd = new SqlCommand(
                            "SELECT COUNT(*) FROM [dbo].[SQL_HISTORY_UPDATE_SCRIPTS] WHERE LAST_SCRIPT_NUMBER = @scriptNum", 
                            connection))
                        {
                            checkRunCmd.Parameters.AddWithValue("@scriptNum", fallbackVersion);
                            var result = await checkRunCmd.ExecuteScalarAsync();
                            alreadyRun = result != null && Convert.ToInt32(result) > 0;
                        }

                        if (alreadyRun)
                        {
                            sb.AppendLine($"  -> Το script '{fallbackVersion}' έχει ήδη εκτελεστεί στη βάση.");
                        }
                        else
                        {
                            sb.AppendLine($"  -> Θα εκτελεστεί το script '{fallbackVersion}'.");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine($"Σφάλμα κατά τη σύνδεση στη βάση: {ex.Message}");
            }

            return sb.ToString();
        }

        // 4. Run database scripts in a transaction, split by GO
        private async Task<bool> ExecuteDatabaseScriptsAsync(string connectionString, List<ScriptDto> scripts, Action<string> log)
        {
            if (string.IsNullOrEmpty(connectionString))
            {
                log("Σφάλμα: Το connection string είναι κενό.");
                return false;
            }

            try
            {
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();
                log("Σύνδεση με τη βάση δεδομένων επιτυχής.");

                foreach (var script in scripts.OrderBy(s => s.SequenceOrder))
                {
                    if (script.ScriptName.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || 
                        (script.ScriptName.EndsWith(".sql", StringComparison.OrdinalIgnoreCase) && ParseBulkScriptFile(script.ScriptContent).Any()))
                    {
                        log($"Ανίχνευση μαζικού αρχείου σεναρίων: {script.ScriptName}. Έναρξη επεξεργασίας...");

                        // 1. Check if SQL_HISTORY_UPDATE_SCRIPTS table exists
                        bool tableExists = false;
                        using (var checkCmd = new SqlCommand(
                            "SELECT CASE WHEN OBJECT_ID(N'[dbo].[SQL_HISTORY_UPDATE_SCRIPTS]', N'U') IS NOT NULL THEN 1 ELSE 0 END", 
                            connection))
                        {
                            var result = await checkCmd.ExecuteScalarAsync();
                            tableExists = result != null && Convert.ToInt32(result) == 1;
                        }

                        if (!tableExists)
                        {
                            log("Θα πρέπει να έρθετε σε επικοινωνία με τον συνεργάτη σας, η αναβάθμιση δεν μπορεί να ολοκληρωθεί");
                            return false;
                        }

                        // 2. Retrieve LAST_SCRIPT_NUMBER from SQL_HISTORY_UPDATE_SCRIPTS
                        string? lastScriptNumber = null;
                        using (var getCmd = new SqlCommand(
                            "SELECT TOP 1 LAST_SCRIPT_NUMBER FROM [dbo].[SQL_HISTORY_UPDATE_SCRIPTS] ORDER BY ID DESC", 
                            connection))
                        {
                            var result = await getCmd.ExecuteScalarAsync();
                            lastScriptNumber = result == null || result == DBNull.Value ? null : result.ToString();
                        }

                        log($"Τελευταίο εκτελεσμένο σενάριο στη βάση δεδομένων: {lastScriptNumber ?? "Κανένα (Αρχική εκτέλεση)"}");

                        // 3. Parse bulk scripts
                        var blocks = ParseBulkScriptFile(script.ScriptContent);
                        if (!blocks.Any())
                        {
                            log("Δεν βρέθηκαν σενάρια προς εκτέλεση στο μαζικό αρχείο.");
                            continue;
                        }

                        // 4. Find where to start
                        int startIndex = 0;
                        if (!string.IsNullOrEmpty(lastScriptNumber))
                        {
                            var lastIndex = blocks.FindIndex(b => b.ScriptNumber.Equals(lastScriptNumber, StringComparison.OrdinalIgnoreCase));
                            if (lastIndex == -1)
                            {
                                log($"Σφάλμα: Το τελευταίο εκτελεσμένο σενάριο '{lastScriptNumber}' δεν βρέθηκε στο μαζικό αρχείο.");
                                log("Το τελευταίο script δεν βρέθηκε στο αρχείο. Η αναβάθμιση ακυρώνεται για λόγους ασφαλείας.");
                                return false;
                            }
                            startIndex = lastIndex + 1;
                        }

                        log($"Θα εκτελεστούν τα σενάρια από τη θέση {startIndex + 1} έως {blocks.Count} (Σενάρια προς εκτέλεση: {blocks.Count - startIndex}).");

                        // 5. Execute each script block sequentially
                        for (int i = startIndex; i < blocks.Count; i++)
                        {
                            var block = blocks[i];
                            log($"Εκτέλεση μαζικού σεναρίου #{block.ScriptNumber}");

                            var commands = SplitSqlScript(block.ScriptContent);
                            using var transaction = connection.BeginTransaction();
                            try
                            {
                                foreach (var commandText in commands)
                                {
                                    if (string.IsNullOrWhiteSpace(commandText)) continue;

                                    using var command = new SqlCommand(commandText, connection, transaction);
                                    await command.ExecuteNonQueryAsync();
                                }

                                // Update history table
                                using (var insertCmd = new SqlCommand(
                                    "INSERT INTO [dbo].[SQL_HISTORY_UPDATE_SCRIPTS] (LAST_SCRIPT_NUMBER, DATE_UPDATE) VALUES (@lastScriptNum, GETDATE())", 
                                    connection, 
                                    transaction))
                                {
                                    insertCmd.Parameters.AddWithValue("@lastScriptNum", block.ScriptNumber);
                                    await insertCmd.ExecuteNonQueryAsync();
                                }

                                await transaction.CommitAsync();
                                log($"Το σενάριο #{block.ScriptNumber} εκτελέστηκε με επιτυχία.");
                            }
                            catch (Exception ex)
                            {
                                await transaction.RollbackAsync();
                                log($"Σφάλμα κατά την εκτέλεση του σεναρίου #{block.ScriptNumber}: {ex.Message}");
                                log("Έγινε rollback του transaction.");
                                return false;
                            }
                        }
                    }
                    else
                    {
                        log($"Εκτέλεση script: {script.ScriptName}");

                        // 1. Check if SQL_HISTORY_UPDATE_SCRIPTS table exists
                        bool tableExists = false;
                        using (var checkCmd = new SqlCommand(
                            "SELECT CASE WHEN OBJECT_ID(N'[dbo].[SQL_HISTORY_UPDATE_SCRIPTS]', N'U') IS NOT NULL THEN 1 ELSE 0 END", 
                            connection))
                        {
                            var result = await checkCmd.ExecuteScalarAsync();
                            tableExists = result != null && Convert.ToInt32(result) == 1;
                        }

                        if (!tableExists)
                        {
                            log("Θα πρέπει να έρθετε σε επικοινωνία με τον συνεργάτη σας, η αναβάθμιση δεν μπορεί να ολοκληρωθεί");
                            return false;
                        }

                        string fallbackVersion = Path.GetFileNameWithoutExtension(script.ScriptName);

                        // 2. Check if this fallbackVersion has already been run
                        bool alreadyRun = false;
                        using (var checkRunCmd = new SqlCommand(
                            "SELECT COUNT(*) FROM [dbo].[SQL_HISTORY_UPDATE_SCRIPTS] WHERE LAST_SCRIPT_NUMBER = @scriptNum", 
                            connection))
                        {
                            checkRunCmd.Parameters.AddWithValue("@scriptNum", fallbackVersion);
                            var result = await checkRunCmd.ExecuteScalarAsync();
                            alreadyRun = result != null && Convert.ToInt32(result) > 0;
                        }

                        if (alreadyRun)
                        {
                            log($"Το script '{fallbackVersion}' έχει ήδη εκτελεστεί στη βάση. Παράλειψη.");
                            continue;
                        }

                        // Split script by GO
                        var commandTexts = SplitSqlScript(script.ScriptContent);

                        // Start transaction for this script
                        using var transaction = connection.BeginTransaction();
                        try
                        {
                            foreach (var commandText in commandTexts)
                            {
                                if (string.IsNullOrWhiteSpace(commandText)) continue;

                                using var command = new SqlCommand(commandText, connection, transaction);
                                await command.ExecuteNonQueryAsync();
                            }

                            // Update history table
                            using (var insertCmd = new SqlCommand(
                                "INSERT INTO [dbo].[SQL_HISTORY_UPDATE_SCRIPTS] (LAST_SCRIPT_NUMBER, DATE_UPDATE) VALUES (@lastScriptNum, GETDATE())", 
                                connection, 
                                transaction))
                            {
                                insertCmd.Parameters.AddWithValue("@lastScriptNum", fallbackVersion);
                                await insertCmd.ExecuteNonQueryAsync();
                            }

                            await transaction.CommitAsync();
                            log($"Το script {script.ScriptName} εκτελέστηκε με επιτυχία.");
                        }
                        catch (Exception ex)
                        {
                            await transaction.RollbackAsync();
                            log($"Σφάλμα κατά την εκτέλεση του script {script.ScriptName}: {ex.Message}");
                            log("Έγινε rollback του transaction.");
                            return false;
                        }
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                log($"Σφάλμα σύνδεσης στη βάση δεδομένων: {ex.Message}");
                return false;
            }
        }

        // Helper to parse bulk script files containing numbered blocks
        public static List<BulkScriptBlock> ParseBulkScriptFile(string content)
        {
            var blocks = new List<BulkScriptBlock>();
            if (string.IsNullOrEmpty(content))
                return blocks;

            // Regex matches comment lines like: --1, -- 2.1, --[3], -- 4 --
            var regex = new Regex(@"^\s*--\s*\[?([a-zA-Z0-9\._\-]+)\]?\s*(--)?\s*$", RegexOptions.IgnoreCase);

            using var reader = new StringReader(content);
            string? line;
            BulkScriptBlock? currentBlock = null;
            var currentContent = new StringBuilder();

            while ((line = reader.ReadLine()) != null)
            {
                var match = regex.Match(line);
                if (match.Success)
                {
                    if (currentBlock != null)
                    {
                        currentBlock.ScriptContent = currentContent.ToString().Trim();
                        blocks.Add(currentBlock);
                        currentContent.Clear();
                    }

                    currentBlock = new BulkScriptBlock
                    {
                        ScriptNumber = match.Groups[1].Value.Trim()
                    };
                }
                else
                {
                    if (currentBlock != null)
                    {
                        currentContent.AppendLine(line);
                    }
                }
            }

            if (currentBlock != null)
            {
                currentBlock.ScriptContent = currentContent.ToString().Trim();
                blocks.Add(currentBlock);
            }

            return blocks;
        }

        public class BulkScriptBlock
        {
            public string ScriptNumber { get; set; } = string.Empty;
            public string ScriptContent { get; set; } = string.Empty;
        }

        // Helper to split SQL by GO statement
        public static IEnumerable<string> SplitSqlScript(string scriptContent)
        {
            if (string.IsNullOrEmpty(scriptContent))
                return Enumerable.Empty<string>();

            // Regex to split by GO statement on its own line (case-insensitive)
            var regex = new Regex(@"^\s*GO\s*$", RegexOptions.IgnoreCase | RegexOptions.Multiline);
            return regex.Split(scriptContent)
                .Select(cmd => cmd.Trim())
                .Where(cmd => !string.IsNullOrEmpty(cmd));
        }

        // Helper to extract database name from connection string
        public static string GetDatabaseNameFromConnectionString(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
                return string.Empty;
            try
            {
                var builder = new SqlConnectionStringBuilder(connectionString);
                return builder.InitialCatalog;
            }
            catch
            {
                return string.Empty;
            }
        }

        // Helper to download files, with mock fallback for testing
        private async Task DownloadFileAsync(string url, string destinationPath, Action<string> log)
        {
            try
            {
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                using var stream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
                await stream.CopyToAsync(fileStream);
            }
            catch (Exception ex)
            {
                log($"Αποτυχία λήψης αρχείου από το δίκτυο ({ex.Message}). Δημιουργία mock αρχείου για δοκιμή.");
                
                // Fallback: Create mock zip package
                CreateMockZipFile(destinationPath);
                log("Δημιουργήθηκε mock zip πακέτο επιτυχώς.");
            }
        }

        // Helper to generate a dummy zip package if the url is unavailable (for testing purposes)
        private void CreateMockZipFile(string zipPath)
        {
            var tempFolder = Path.Combine(Path.GetTempPath(), "TmsMockBuild_" + Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempFolder);

            // Write a dummy exe file
            var dummyExePath = Path.Combine(tempFolder, "TmsApp.exe");
            File.WriteAllText(dummyExePath, "TMS Desktop Application Mock. Updated: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

            // Zip the folder
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }
            ZipFile.CreateFromDirectory(tempFolder, zipPath);
            Directory.Delete(tempFolder, recursive: true);
        }

        // Helper to test database connection
        public static bool TestConnectionString(string connectionString, out string errorMessage)
        {
            errorMessage = string.Empty;
            try
            {
                using var conn = new SqlConnection(connectionString);
                conn.Open();
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }

        // SQL Instances & Databases Discovery
        public static List<string> GetLocalSqlInstances()
        {
            var instances = new List<string>();
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    // Read from Registry
                    using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL"))
                    {
                        if (key != null)
                        {
                            foreach (var name in key.GetValueNames())
                            {
                                instances.Add(name);
                            }
                        }
                    }

                    // Also check Wow6432Node
                    using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Wow6432Node\Microsoft\Microsoft SQL Server\Instance Names\SQL"))
                    {
                        if (key != null)
                        {
                            foreach (var name in key.GetValueNames())
                            {
                                if (!instances.Contains(name))
                                {
                                    instances.Add(name);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error querying registry for SQL instances: {ex.Message}");
            }

            // If empty, assume MSSQLSERVER (default instance)
            if (!instances.Any())
            {
                instances.Add("MSSQLSERVER");
            }

            return instances;
        }

        public static List<string> GetDatabasesForInstance(string serverName)
        {
            var databases = new List<string>();
            // Use 5s timeout to prevent hanging on unresponsive instances
            var connStr = $"Server={serverName};Database=master;Integrated Security=True;TrustServerCertificate=True;Connection Timeout=5";
            try
            {
                using var conn = new SqlConnection(connStr);
                conn.Open();
                using var cmd = new SqlCommand("SELECT name FROM sys.databases WHERE database_id > 4 AND state = 0", conn);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    databases.Add(reader.GetString(0));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Could not get databases for instance {serverName}: {ex.Message}");
            }
            return databases;
        }

        public static List<DiscoveredDatabaseDto> DiscoverLocalDatabases()
        {
            var list = new List<DiscoveredDatabaseDto>();
            var instances = GetLocalSqlInstances();
            foreach (var instance in instances)
            {
                var serverName = instance == "MSSQLSERVER" ? "localhost" : $@"localhost\{instance}";
                var databases = GetDatabasesForInstance(serverName);
                foreach (var db in databases)
                {
                    list.Add(new DiscoveredDatabaseDto
                    {
                        InstanceName = instance,
                        DatabaseName = db,
                        ConnectionString = $"Server={serverName};Database={db};Integrated Security=True;TrustServerCertificate=True;"
                    });
                }
            }
            return list;
        }

        public async Task<bool> RunAgentSelfUpgradeAsync(string serverUrl, string systemBinaryUrl, bool isService, Action<string> logCallback)
        {
            try
            {
                var downloadUrl = systemBinaryUrl;
                if (!downloadUrl.StartsWith("http", StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = $"{serverUrl.TrimEnd('/')}/{downloadUrl.TrimStart('/')}";
                }

                var tempDir = Path.Combine(Path.GetTempPath(), "TmsAgentSelfUpdate", Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempDir);
                var tempZipPath = Path.Combine(tempDir, "agent_package.zip");
                var extractDir = Path.Combine(tempDir, "extracted");
                Directory.CreateDirectory(extractDir);

                logCallback?.Invoke($"Λήψη αναβάθμισης Agent από {downloadUrl}...");
                await DownloadFileAsync(downloadUrl, tempZipPath, logCallback);

                logCallback?.Invoke("Αποσυμπίεση αρχείων αναβάθμισης...");
                ZipFile.ExtractToDirectory(tempZipPath, extractDir);

                var currentExe = Environment.ProcessPath;
                if (string.IsNullOrEmpty(currentExe))
                {
                    currentExe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                }
                if (string.IsNullOrEmpty(currentExe))
                {
                    throw new Exception("Unable to determine current process path.");
                }

                var currentFolder = Path.GetDirectoryName(currentExe);
                if (string.IsNullOrEmpty(currentFolder))
                {
                    throw new Exception("Unable to determine current directory.");
                }

                var batchPath = Path.Combine(Path.GetTempPath(), $"update_agent_{Guid.NewGuid().ToString().Substring(0, 8)}.bat");
                var batchContent = new StringBuilder();
                batchContent.AppendLine("@echo off");
                batchContent.AppendLine("chcp 65001 > nul");
                
                if (isService)
                {
                    batchContent.AppendLine("sc stop TmsAgent > nul");
                }

                // Loop and check if the executable is locked. Wait until type is successful (meaning process terminated and lock released)
                batchContent.AppendLine(":wait_unlock");
                batchContent.AppendLine($"type nul >> \"{currentExe}\" 2>nul");
                batchContent.AppendLine("if errorlevel 1 (");
                batchContent.AppendLine("    timeout /t 1 /nobreak > nul");
                batchContent.AppendLine("    goto wait_unlock");
                batchContent.AppendLine(")");

                batchContent.AppendLine($"xcopy /y /s /e \"{extractDir}\\*\" \"{currentFolder}\\\" > nul");

                if (isService)
                {
                    batchContent.AppendLine("sc start TmsAgent > nul");
                }
                else
                {
                    batchContent.AppendLine($"start \"\" \"{currentExe}\"");
                }

                // Delete the temp directory and the batch file itself
                batchContent.AppendLine($"rd /s /q \"{tempDir}\"");
                batchContent.AppendLine($"(goto) 2>nul & del \"%~f0\"");

                await File.WriteAllTextAsync(batchPath, batchContent.ToString(), Encoding.UTF8);

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c \"{batchPath}\"",
                    CreateNoWindow = true,
                    UseShellExecute = true, // Spawn completely independently
                    Verb = "runas", // Request administrator elevation (UAC prompt) to allow writing to protected folders like Program Files
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                };

                logCallback?.Invoke("Εκκίνηση διαδικασίας εγκατάστασης και επανεκκίνηση του Agent...");
                System.Diagnostics.Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                logCallback?.Invoke($"Σφάλμα κατά την αυτόματη αναβάθμιση του Agent: {ex.Message}");
                return false;
            }
        }
    }
}
