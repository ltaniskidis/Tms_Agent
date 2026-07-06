using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
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
            _httpClient.Timeout = TimeSpan.FromMinutes(10);
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
        public async Task<UpdateResult> RunUpdateAsync(
            string serverUrl, 
            string clientId, 
            string machineName, 
            string machineRole,
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
                try
                {
                    var logFolder = PathHelper.GetAgentDataFolder();
                    var logFile = Path.Combine(logFolder, "agent.log");
                    File.AppendAllText(logFile, msg + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to write to local log file: {ex.Message}");
                }
            }

            try
            {
                var logFolder = PathHelper.GetAgentDataFolder();
                var logFile = Path.Combine(logFolder, "agent.log");
                File.AppendAllText(logFile, $"{Environment.NewLine}{Environment.NewLine}=================== NEW UPDATE RUN: {DateTime.Now} ==================={Environment.NewLine}");
            }
            catch {}

            Log($"Έναρξη αναβάθμισης για το προφίλ '{profile.ProfileName}' (ΑΦΜ: {profile.Afm}) στην έκδοση {newVersion.VersionNumber}");

            bool dbSuccess = false;
            bool fileSuccess = false;
            string errorMessage = string.Empty;

            string targetFolder = profile.TargetFolder;

            using var cts = new CancellationTokenSource();
            string exeNameWithoutExt = Path.GetFileNameWithoutExtension(profile.TargetExeName);
            var guardTask = Task.Run(async () =>
            {
                if (string.IsNullOrEmpty(exeNameWithoutExt)) return;
                while (!cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var runningProcesses = System.Diagnostics.Process.GetProcessesByName(exeNameWithoutExt);
                        foreach (var process in runningProcesses)
                        {
                            if (process.Id == System.Diagnostics.Process.GetCurrentProcess().Id) continue;
                            try
                            {
                                process.Kill();
                                Log("Παρακαλώ περιμένετε, πραγματοποιείται αναβάθμιση στην εφαρμογή");
                            }
                            catch { }
                        }
                    }
                    catch { }
                    try
                    {
                        await Task.Delay(150, cts.Token);
                    }
                    catch { }
                }
            });

            try
            {
                if (!Directory.Exists(targetFolder))
                {
                    Log($"Παράλειψη αναβάθμισης αρχείων: Ο δηλωμένος φάκελος '{targetFolder}' δεν υπάρχει σε αυτόν τον υπολογιστή.");
                    return new UpdateResult
                    {
                        Success = true,
                        ErrorMessage = string.Empty
                    };
                }

                // STEP 1: Execute SQL Scripts
                if (newVersion.Scripts != null && newVersion.Scripts.Any())
                {
                    if (machineRole == "Client")
                    {
                        Log("Παράλειψη εκτέλεσης SQL scripts: Ο υπολογιστής έχει δηλωθεί ως Client (workstation).");
                        dbSuccess = true;
                    }
                    else
                    {
                        var resolvedConnStr = profile.GetResolvedConnectionString(Log);
                        var dbName = GetDatabaseNameFromConnectionString(resolvedConnStr);
                        
                        // Call CheckForUpdates to verify if this database is still monitored
                        var checkResp = await CheckForUpdatesAsync(serverUrl, clientId, machineName, machineRole, "1.3.2", apiKey, new List<LocalProfile> { profile }, false, false);
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
                                // Check if table exists error occurred. The helper logs it.
                                errorMessage = "Αποτυχία κατά την εκτέλεση των SQL Scripts της βάσης δεδομένων.";
                                
                                // Let's check if SQL_HISTORY_UPDATE_SCRIPTS table check failed
                                bool tableExists = false;
                                try
                                {
                                    using var connection = new SqlConnection(resolvedConnStr);
                                    await connection.OpenAsync();
                                    using var checkCmd = new SqlCommand(
                                        "SELECT CASE WHEN OBJECT_ID(N'[dbo].[SQL_HISTORY_UPDATE_SCRIPTS]', N'U') IS NOT NULL THEN 1 ELSE 0 END", 
                                        connection);
                                    var result = await checkCmd.ExecuteScalarAsync();
                                    tableExists = result != null && Convert.ToInt32(result) == 1;
                                }
                                catch { }

                                if (!tableExists)
                                {
                                    errorMessage = "Παρακαλώ θα πρέπει να έρθετε σε επικοινωνία με τον συνεργάτη σας!!!";
                                }
                                throw new Exception(errorMessage);
                            }
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
                    if (!Directory.Exists(targetFolder))
                    {
                        Log($"Παράλειψη αναβάθμισης αρχείων: Ο δηλωμένος φάκελος '{targetFolder}' δεν υπάρχει σε αυτόν τον υπολογιστή.");
                        fileSuccess = true;
                    }
                    else
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

                            // Find the active productive EXE
                            string activeExeName = FindActiveProductiveExe(targetFolder, profile.TargetExeName, Log);
                            string activeExePath = Path.Combine(targetFolder, activeExeName);

                            // Terminate running instances of the active desktop application to avoid lock issues
                            try
                            {
                                var innerExeNameWithoutExt = Path.GetFileNameWithoutExtension(activeExeName);
                                var runningProcesses = System.Diagnostics.Process.GetProcessesByName(innerExeNameWithoutExt);
                                foreach (var process in runningProcesses)
                                {
                                    if (process.Id == System.Diagnostics.Process.GetCurrentProcess().Id)
                                    {
                                        continue;
                                    }

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

                            // Backup and Delete Logic for activeExeName
                            string activeExeWithoutExt = Path.GetFileNameWithoutExtension(activeExeName);
                            string oldExePath1 = Path.Combine(targetFolder, $"{activeExeWithoutExt}_OLD.exe");
                            string oldExePath2 = Path.Combine(targetFolder, $"{activeExeWithoutExt}_ολδ.exe");

                            if (File.Exists(oldExePath1))
                            {
                                try
                                {
                                    File.Delete(oldExePath1);
                                    Log($"Διαγραφή παλαιού backup: {Path.GetFileName(oldExePath1)}");
                                }
                                catch (Exception ex)
                                {
                                    Log($"Αδυναμία διαγραφής παλαιού backup {Path.GetFileName(oldExePath1)}: {ex.Message}");
                                }
                            }
                            if (File.Exists(oldExePath2))
                            {
                                try
                                {
                                    File.Delete(oldExePath2);
                                    Log($"Διαγραφή παλαιού backup: {Path.GetFileName(oldExePath2)}");
                                }
                                catch (Exception ex)
                                {
                                    Log($"Αδυναμία διαγραφής παλαιού backup {Path.GetFileName(oldExePath2)}: {ex.Message}");
                                }
                            }

                            if (File.Exists(activeExePath))
                            {
                                try
                                {
                                    File.Move(activeExePath, oldExePath1);
                                    Log($"Μετονομασία παραγωγικού αρχείου σε backup: {activeExeName} -> {Path.GetFileName(oldExePath1)}");
                                }
                                catch (Exception ex)
                                {
                                    Log($"Αδυναμία μετονομασίας παραγωγικού αρχείου {activeExeName} σε backup: {ex.Message}");
                                    throw;
                                }
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

                                        string targetFilePath;
                                        
                                        // If this is the main EXE inside the zip, rename it to activeExeName when extracting
                                        string zipEntryName = entry.Name;
                                        string defaultBaseName = "TIMOLOGISI";
                                        string configBaseName = Path.GetFileNameWithoutExtension(profile.TargetExeName ?? "TIMOLOGISI.exe");
                                        string cleanConfigBaseName = System.Text.RegularExpressions.Regex.Replace(configBaseName, @"\d+$", "");
                                        string cleanZipEntryBaseName = System.Text.RegularExpressions.Regex.Replace(Path.GetFileNameWithoutExtension(zipEntryName), @"\d+$", "");

                                        bool isMainExe = string.Equals(zipEntryName, profile.TargetExeName, StringComparison.OrdinalIgnoreCase) ||
                                                         (Path.GetExtension(zipEntryName).Equals(".exe", StringComparison.OrdinalIgnoreCase) && 
                                                          (string.Equals(zipEntryName, "TIMOLOGISI.exe", StringComparison.OrdinalIgnoreCase) ||
                                                           string.Equals(cleanZipEntryBaseName, cleanConfigBaseName, StringComparison.OrdinalIgnoreCase) ||
                                                           string.Equals(cleanZipEntryBaseName, defaultBaseName, StringComparison.OrdinalIgnoreCase)));

                                        if (isMainExe)
                                        {
                                            targetFilePath = Path.GetFullPath(Path.Combine(targetFolder, activeExeName));
                                            Log($"Εξαγωγή νέου εκτελέσιμου αρχείου ως {activeExeName} (από {entry.Name} στο ZIP)...");
                                        }
                                        else
                                        {
                                            targetFilePath = Path.GetFullPath(Path.Combine(targetFolder, entry.FullName));
                                        }
                                        
                                        // Ensure directory exists
                                        var parentDir = Path.GetDirectoryName(targetFilePath);
                                        if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                                        {
                                            Directory.CreateDirectory(parentDir);
                                        }

                                        // Extract the entry (overwriting if it already exists, other files have no renaming/backup)
                                        entry.ExtractToFile(targetFilePath, overwrite: true);
                                    }
                                }
                            }
                            else
                            {
                                var targetFilePath = Path.Combine(targetFolder, activeExeName);
                                if (!Directory.Exists(targetFolder))
                                {
                                    Directory.CreateDirectory(targetFolder);
                                }
                                File.Copy(tempZipPath, targetFilePath, overwrite: true);
                                Log($"Αντιγραφή νέου εκτελέσιμου αρχείου ως {activeExeName}...");
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
                }
                else
                {
                    Log("Δεν περιλαμβάνεται νέο εκτελέσιμο αρχείο.");
                    fileSuccess = true;
                }

                // If both succeeded, update versions
                if (dbSuccess && fileSuccess)
                {
                    if (newVersion.Scripts != null && newVersion.Scripts.Any() && machineRole != "Client")
                    {
                        // Query the database for the actual last executed script to set as CurrentDbVersion
                        string? actualDbVersion = null;
                        try
                        {
                            var resolvedConnStr = profile.GetResolvedConnectionString(Log);
                            using var connection = new SqlConnection(resolvedConnStr);
                            await connection.OpenAsync();
                            using var cmd = new SqlCommand("SELECT TOP 1 LAST_SCRIPT_NUMBER FROM [dbo].[SQL_HISTORY_UPDATE_SCRIPTS] ORDER BY ID DESC", connection);
                            var result = await cmd.ExecuteScalarAsync();
                            actualDbVersion = result == null || result == DBNull.Value ? null : result.ToString();
                        }
                        catch (Exception ex)
                        {
                            Log($"Σφάλμα κατά την ανάκτηση της έκδοσης της βάσης: {ex.Message}");
                        }

                        if (!string.IsNullOrEmpty(actualDbVersion))
                        {
                            profile.CurrentDbVersion = actualDbVersion;
                            Log($"Έκδοση βάσης ενημερώθηκε σε: {actualDbVersion}");
                        }
                        else
                        {
                            profile.CurrentDbVersion = newVersion.VersionNumber;
                        }
                    }
                    profile.CurrentProgramVersion = newVersion.VersionNumber;
                    profile.CurrentVersion = newVersion.VersionNumber;

                    // Save version file to target folder to survive resets and bypass bad assembly metadata
                    if (!string.IsNullOrEmpty(targetFolder) && Directory.Exists(targetFolder))
                    {
                        try
                        {
                            var versionFilePath = Path.Combine(targetFolder, "tms_version.txt");
                            File.WriteAllText(versionFilePath, newVersion.VersionNumber);
                            Log($"Αποθήκευση τοπικής έκδοσης: {versionFilePath} -> {newVersion.VersionNumber}");
                        }
                        catch (Exception ex)
                        {
                            Log($"Αδυναμία αποθήκευσης τοπικού αρχείου έκδοσης tms_version.txt: {ex.Message}");
                        }
                    }

                    Log($"Η αναβάθμιση ολοκληρώθηκε επιτυχώς στην έκδοση {newVersion.VersionNumber}!");
                }
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                Log($"Σφάλμα αναβάθμισης: {ex.Message}");
            }
            finally
            {
                cts.Cancel();
                try
                {
                    guardTask.Wait(1000);
                }
                catch { }
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

            return new UpdateResult { Success = finalSuccess, ErrorMessage = errorMessage };
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

                        sb.AppendLine($"  -> Τελευταίο Εκτελεσμένο Block: {lastScriptNumber ?? "Κανένα"}");

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
                                int dbVal = 0;
                                int maxPkgVal = 0;
                                bool isDbValNumeric = int.TryParse(lastScriptNumber, out dbVal);
                                foreach (var b in blocks)
                                {
                                    if (int.TryParse(b.ScriptNumber, out int v) && v > maxPkgVal)
                                    {
                                        maxPkgVal = v;
                                    }
                                }

                                if ((isDbValNumeric && dbVal >= maxPkgVal) || 
                                    string.Equals(lastScriptNumber, blocks.Last().ScriptNumber, StringComparison.OrdinalIgnoreCase))
                                {
                                    sb.AppendLine($"  -> Η βάση είναι ήδη ενημερωμένη (Τελευταίο block στη βάση: {lastScriptNumber}, Μέγιστο πακέτου: {maxPkgVal}).");
                                    continue;
                                }

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
                            log("Παρακαλώ θα πρέπει να έρθετε σε επικοινωνία με τον συνεργάτη σας!!!");
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
                                int dbVal = 0;
                                int maxPkgVal = 0;
                                bool isDbValNumeric = int.TryParse(lastScriptNumber, out dbVal);
                                foreach (var b in blocks)
                                {
                                    if (int.TryParse(b.ScriptNumber, out int v) && v > maxPkgVal)
                                    {
                                        maxPkgVal = v;
                                    }
                                }

                                if ((isDbValNumeric && dbVal >= maxPkgVal) || 
                                    string.Equals(lastScriptNumber, blocks.Last().ScriptNumber, StringComparison.OrdinalIgnoreCase))
                                {
                                    log($"Η βάση δεδομένων είναι ήδη ενημερωμένη (Τρέχουσα βάσης: {lastScriptNumber}, Μέγιστη πακέτου: {maxPkgVal}). Προχωράμε στην αναβάθμιση του .exe...");
                                    continue;
                                }

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

                                // Update history table (if not already inserted by the script commands)
                                bool alreadyInserted = false;
                                using (var checkExistCmd = new SqlCommand(
                                    "SELECT COUNT(*) FROM [dbo].[SQL_HISTORY_UPDATE_SCRIPTS] WHERE LAST_SCRIPT_NUMBER = @lastScriptNum", 
                                    connection, 
                                    transaction))
                                {
                                    checkExistCmd.Parameters.AddWithValue("@lastScriptNum", block.ScriptNumber);
                                    var existsResult = await checkExistCmd.ExecuteScalarAsync();
                                    alreadyInserted = existsResult != null && Convert.ToInt32(existsResult) > 0;
                                }

                                if (!alreadyInserted)
                                {
                                    using (var insertCmd = new SqlCommand(
                                        "INSERT INTO [dbo].[SQL_HISTORY_UPDATE_SCRIPTS] (LAST_SCRIPT_NUMBER, DATE_UPDATE) VALUES (@lastScriptNum, GETDATE())", 
                                        connection, 
                                        transaction))
                                    {
                                        insertCmd.Parameters.AddWithValue("@lastScriptNum", block.ScriptNumber);
                                        await insertCmd.ExecuteNonQueryAsync();
                                    }
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
                            log("Παρακαλώ θα πρέπει να έρθετε σε επικοινωνία με τον συνεργάτη σας!!!");
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

            // Regex matches comment lines like: --1, -- 2.1, --[3], -- 4 --, and ---NEW SCRIPT 2341
            // Requires the block identifier to start with an alphanumeric character to prevent matching divider lines like '------------------'
            var regex = new Regex(@"^\s*-{2,}\s*(?:NEW\s+SCRIPT\s+|SCRIPT\s+)?\[?([a-zA-Z0-9][a-zA-Z0-9\._\-]*)\]?\s*(?:-{2,})?\s*$", RegexOptions.IgnoreCase);

            using var reader = new StringReader(content);
            string? line;
            BulkScriptBlock? currentBlock = null;
            var currentContent = new StringBuilder();

            while ((line = reader.ReadLine()) != null)
            {
                var match = regex.Match(line);
                bool isValidBlockMatch = false;
                string blockNumber = "";

                if (match.Success)
                {
                    blockNumber = match.Groups[1].Value.Trim();
                    bool startsWithDigit = char.IsDigit(blockNumber[0]);
                    bool hasNewScript = line.Contains("NEW SCRIPT", StringComparison.OrdinalIgnoreCase) || line.Contains("SCRIPT ", StringComparison.OrdinalIgnoreCase);

                    if (startsWithDigit || hasNewScript)
                    {
                        isValidBlockMatch = true;
                    }
                }

                if (isValidBlockMatch)
                {
                    if (currentBlock != null)
                    {
                        currentBlock.ScriptContent = currentContent.ToString().Trim();
                        if (HasSqlStatements(currentBlock.ScriptContent))
                        {
                            blocks.Add(currentBlock);
                        }
                        currentContent.Clear();
                    }

                    currentBlock = new BulkScriptBlock
                    {
                        ScriptNumber = blockNumber
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
                if (HasSqlStatements(currentBlock.ScriptContent))
                {
                    blocks.Add(currentBlock);
                }
            }

            return blocks;
        }

        public static string StripSqlComments(string sql)
        {
            if (string.IsNullOrEmpty(sql))
                return string.Empty;

            var sb = new StringBuilder();
            bool inSingleLineComment = false;
            bool inMultiLineComment = false;
            bool inString = false;
            char stringChar = '\0';

            for (int i = 0; i < sql.Length; i++)
            {
                char c = sql[i];
                char next = i + 1 < sql.Length ? sql[i + 1] : '\0';

                if (inSingleLineComment)
                {
                    if (c == '\r' || c == '\n')
                    {
                        inSingleLineComment = false;
                        sb.Append(c);
                    }
                    continue;
                }

                if (inMultiLineComment)
                {
                    if (c == '*' && next == '/')
                    {
                        inMultiLineComment = false;
                        i++; // skip '/'
                    }
                    continue;
                }

                if (inString)
                {
                    sb.Append(c);
                    if (c == '\'' && stringChar == '\'')
                    {
                        if (next == '\'')
                        {
                            sb.Append(next);
                            i++;
                        }
                        else
                        {
                            inString = false;
                        }
                    }
                    continue;
                }

                if (c == '-' && next == '-')
                {
                    inSingleLineComment = true;
                    i++;
                    continue;
                }
                if (c == '/' && next == '*')
                {
                    inMultiLineComment = true;
                    i++;
                    continue;
                }
                if (c == '\'')
                {
                    inString = true;
                    stringChar = '\'';
                    sb.Append(c);
                    continue;
                }

                sb.Append(c);
            }

            return sb.ToString();
        }

        public static bool HasSqlStatements(string content)
        {
            string stripped = StripSqlComments(content);
            stripped = Regex.Replace(stripped, @"\bGO\b", "", RegexOptions.IgnoreCase);
            stripped = stripped.Replace(";", "").Trim();
            return Regex.IsMatch(stripped, "[a-zA-Z0-9]");
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
        private async Task DownloadFileAsync(string url, string destinationPath, Action<string>? log, Action<double>? progressCallback = null)
        {
            try
            {
                using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                response.EnsureSuccessStatusCode();

                var totalBytes = response.Content.Headers.ContentLength ?? -1L;
                using var stream = await response.Content.ReadAsStreamAsync();
                using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
                
                var buffer = new byte[81920];
                var bytesRead = 0L;
                int read;
                while (true)
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    try
                    {
                        read = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        throw new TimeoutException("Η λήψη του αρχείου διακόπηκε λόγω αδράνειας του δικτύου (Network Read Timeout).");
                    }

                    if (read <= 0) break;

                    await fileStream.WriteAsync(buffer, 0, read);
                    bytesRead += read;
                    
                    if (progressCallback != null && totalBytes > 0)
                    {
                        var percentage = (double)bytesRead / totalBytes * 100.0;
                        progressCallback.Invoke(percentage);
                    }
                }
            }
            catch (Exception ex)
            {
#if DEBUG
                log?.Invoke($"Αποτυχία λήψης αρχείου από το δίκτυο ({ex.Message}). Δημιουργία mock αρχείου για δοκιμή.");
                
                // Fallback: Create mock zip package
                CreateMockZipFile(destinationPath);
                log?.Invoke("Δημιουργήθηκε mock zip πακέτο επιτυχώς.");
#else
                log?.Invoke($"Αποτυχία λήψης αρχείου από το δίκτυο ({ex.Message}).");
                throw;
#endif
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

        public async Task<bool> RunAgentSelfUpgradeAsync(string serverUrl, string systemBinaryUrl, bool isService, Action<string>? logCallback, Action<double>? progressCallback = null)
        {
            void Log(string msg)
            {
                logCallback?.Invoke(msg);
                try
                {
                    var logFolder = PathHelper.GetAgentDataFolder();
                    var logFile = Path.Combine(logFolder, "agent.log");
                    File.AppendAllText(logFile, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [SelfUpgrade] {msg}{Environment.NewLine}");
                }
                catch { }
            }

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

                Log($"Έναρξη αυτόματης αναβάθμισης Agent. Λήψη πακέτου από {downloadUrl}...");
                await DownloadFileAsync(downloadUrl, tempZipPath, logCallback, progressCallback);

                Log("Αποσυμπίεση αρχείων αναβάθμισης...");
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

                var commandLineArgs = Environment.GetCommandLineArgs();
                bool wasStartup = false;
                if (commandLineArgs != null)
                {
                    wasStartup = commandLineArgs.Any(arg => string.Equals(arg, "--startup", StringComparison.OrdinalIgnoreCase));
                }
                
                // Track if the service was running so we can restore it
                batchContent.AppendLine("set wasServiceRunning=0");
                batchContent.AppendLine("sc query TmsAgent 2>nul | findstr RUNNING > nul");
                batchContent.AppendLine("if %errorlevel% equ 0 (");
                batchContent.AppendLine("    set wasServiceRunning=1");
                batchContent.AppendLine("    sc stop TmsAgent > nul");
                batchContent.AppendLine(")");
                batchContent.AppendLine("if %wasServiceRunning% equ 1 goto wait_service_stop");
                batchContent.AppendLine("goto service_stopped");
                batchContent.AppendLine(":wait_service_stop");
                batchContent.AppendLine("set stopRetry=0");
                batchContent.AppendLine(":wait_service_stop_loop");
                batchContent.AppendLine("sc query TmsAgent 2>nul | findstr /I \"RUNNING STOP_PENDING\" > nul");
                batchContent.AppendLine("if %errorlevel% equ 0 (");
                batchContent.AppendLine("    set /a stopRetry+=1");
                batchContent.AppendLine("    if %stopRetry% lss 10 (");
                batchContent.AppendLine("        timeout /t 1 /nobreak > nul");
                batchContent.AppendLine("        goto wait_service_stop_loop");
                batchContent.AppendLine("    )");
                batchContent.AppendLine(")");
                batchContent.AppendLine(":service_stopped");
                
                // Force kill remaining processes to release locks on files/DLLs
                batchContent.AppendLine("taskkill /f /im TmsAgentService.exe 2>nul");
                batchContent.AppendLine("taskkill /f /im Tms.Agent.Wpf.exe 2>nul");
                batchContent.AppendLine($"taskkill /f /im \"{Path.GetFileName(currentExe)}\" 2>nul");

                // Loop and check if the executable is locked. Wait until type is successful (meaning process terminated and lock released)
                batchContent.AppendLine("set unlockRetry=0");
                batchContent.AppendLine(":wait_unlock");
                batchContent.AppendLine($"type nul >> \"{currentExe}\" 2>nul");
                batchContent.AppendLine("if errorlevel 1 (");
                batchContent.AppendLine("    set /a unlockRetry+=1");
                batchContent.AppendLine("    if %unlockRetry% lss 10 (");
                batchContent.AppendLine("        timeout /t 1 /nobreak > nul");
                batchContent.AppendLine("        goto wait_unlock");
                batchContent.AppendLine("    )");
                batchContent.AppendLine(")");

                // Copy files with a retry loop in case other files (DLLs) are locked temporarily
                var logFolder = PathHelper.GetAgentDataFolder();
                var logFilePath = Path.Combine(logFolder, "agent_upgrade.log");
                batchContent.AppendLine("set copyRetry=0");
                batchContent.AppendLine(":do_copy");
                batchContent.AppendLine($"xcopy /y /s /e \"{extractDir}\\*\" \"{currentFolder}\" > \"{logFilePath}\" 2>&1");
                batchContent.AppendLine("if errorlevel 1 (");
                batchContent.AppendLine("    set /a copyRetry+=1");
                batchContent.AppendLine("    if %copyRetry% lss 10 (");
                batchContent.AppendLine("        taskkill /f /im TmsAgentService.exe 2>nul");
                batchContent.AppendLine("        taskkill /f /im Tms.Agent.Wpf.exe 2>nul");
                batchContent.AppendLine($"        taskkill /f /im \"{Path.GetFileName(currentExe)}\" 2>nul");
                batchContent.AppendLine("        timeout /t 2 /nobreak > nul");
                batchContent.AppendLine("        goto do_copy");
                batchContent.AppendLine("    )");
                batchContent.AppendLine(")");

                if (isService)
                {
                    batchContent.AppendLine("sc start TmsAgent > nul");
                    batchContent.AppendLine("schtasks /run /tn \"TmsAgent\" > nul 2>&1");
                }
                else
                {
                    string argsStr = " --startup"; // Always restart silently in tray after upgrade
                    batchContent.AppendLine($"start \"\" \"{currentExe}\"{argsStr}");
                    batchContent.AppendLine("if %wasServiceRunning% equ 1 (");
                    batchContent.AppendLine("    sc start TmsAgent > nul");
                    batchContent.AppendLine(")");
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
                    UseShellExecute = !isService, // Use shell execution only for desktop app to support runas elevation (Session 0 does not support shell)
                    WindowStyle = System.Diagnostics.ProcessWindowStyle.Hidden
                };

                if (!isService)
                {
                    psi.Verb = "runas"; // Request administrator elevation (UAC prompt) to allow writing to protected folders like Program Files
                }

                Log("Εκκίνηση διαδικασίας εγκατάστασης και επανεκκίνηση του Agent...");
                System.Diagnostics.Process.Start(psi);
                return true;
            }
            catch (Exception ex)
            {
                Log($"Σφάλμα κατά την αυτόματη αναβάθμιση του Agent: {ex.Message}");
                return false;
            }
        }

        #region Shortcut Resolution & Active EXE Finding Helpers

        public static string FindActiveProductiveExe(string targetFolder, string targetExeName, Action<string> log)
        {
            string baseName = Path.GetFileNameWithoutExtension(targetExeName);
            string extension = Path.GetExtension(targetExeName);
            
            // Try resolving via Desktop shortcuts first
            var activeExesFromShortcuts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                var commonDesktopPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
                
                var shortcutFiles = new List<string>();
                if (Directory.Exists(desktopPath))
                {
                    shortcutFiles.AddRange(Directory.GetFiles(desktopPath, "*.lnk"));
                }
                if (Directory.Exists(commonDesktopPath))
                {
                    shortcutFiles.AddRange(Directory.GetFiles(commonDesktopPath, "*.lnk"));
                }
                
                foreach (var shortcut in shortcutFiles)
                {
                    var target = ResolveShortcut(shortcut);
                    if (!string.IsNullOrEmpty(target) && File.Exists(target))
                    {
                        var targetDir = Path.GetDirectoryName(target);
                        var targetName = Path.GetFileName(target);
                        
                        if (targetDir != null && string.Equals(Path.GetFullPath(targetDir), Path.GetFullPath(targetFolder), StringComparison.OrdinalIgnoreCase))
                        {
                            var regex = new Regex($"^{Regex.Escape(baseName)}\\d*{Regex.Escape(extension)}$", RegexOptions.IgnoreCase);
                            if (regex.IsMatch(targetName))
                            {
                                activeExesFromShortcuts.Add(targetName);
                                log($"Βρέθηκε ενεργή συντόμευση επιφάνειας εργασίας: '{Path.GetFileName(shortcut)}' -> '{targetName}'");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                log($"Σφάλμα κατά την ανάγνωση συντομεύσεων επιφάνειας εργασίας: {ex.Message}");
            }
            
            if (activeExesFromShortcuts.Count == 1)
            {
                var resolvedExe = activeExesFromShortcuts.First();
                log($"Χρήση παραγωγικού εκτελέσιμου μέσω συντόμευσης: {resolvedExe}");
                return resolvedExe;
            }
            else if (activeExesFromShortcuts.Count > 1)
            {
                log($"Βρέθηκαν πολλαπλά εκτελέσιμα μέσω συντομεύσεων: {string.Join(", ", activeExesFromShortcuts)}. Θα γίνει επιλογή του πρώτου.");
                return activeExesFromShortcuts.First();
            }
            
            log("Δεν βρέθηκε συντόμευση στην επιφάνεια εργασίας. Έναρξη σάρωσης του φακέλου εφαρμογής...");
            if (Directory.Exists(targetFolder))
            {
                try
                {
                    var files = Directory.GetFiles(targetFolder, "*.exe");
                    var regex = new Regex($"^{Regex.Escape(baseName)}\\d*{Regex.Escape(extension)}$", RegexOptions.IgnoreCase);
                    var matchedFiles = files
                        .Select(Path.GetFileName)
                        .Where(f => !string.IsNullOrEmpty(f) && regex.IsMatch(f))
                        .ToList();
                    
                    if (matchedFiles.Any())
                    {
                        var sorted = matchedFiles.OrderByDescending(f => f).ToList();
                        log($"Βρέθηκαν εκτελέσιμα στο φάκελο: {string.Join(", ", sorted)}. Επιλογή: {sorted.First()}");
                        return sorted.First() ?? targetExeName;
                    }
                }
                catch (Exception ex)
                {
                    log($"Σφάλμα κατά τη σάρωση του φακέλου {targetFolder}: {ex.Message}");
                }
            }
            
            log($"Δεν βρέθηκε αντιστοιχία. Fallback στο προκαθορισμένο όνομα: {targetExeName}");
            return targetExeName;
        }

        public static string ResolveShortcut(string shortcutPath)
        {
            try
            {
                var link = (IShellLinkW)new ShellLink();
                var file = (IPersistFile)link;
                file.Load(shortcutPath, 0); // STGM_READ = 0
                var sb = new StringBuilder(260);
                link.GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
                return sb.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        [ComImport]
        [Guid("00021401-0000-0000-C000-000000000046")]
        internal class ShellLink
        {
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("000214F9-0000-0000-C000-000000000046")]
        internal interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, IntPtr pfd, int fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, int dwReserved);
            void Resolve(IntPtr hwnd, int fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        [Guid("0000010b-0000-0000-C000-000000000046")]
        internal interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            [PreserveSig]
            int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, int dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
        }

        public async Task<string?> GetActualDatabaseVersionAsync(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString)) return null;
            try
            {
                using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();
                
                // Check if SQL_HISTORY_UPDATE_SCRIPTS table exists
                bool tableExists = false;
                using (var checkCmd = new SqlCommand(
                    "SELECT CASE WHEN OBJECT_ID(N'[dbo].[SQL_HISTORY_UPDATE_SCRIPTS]', N'U') IS NOT NULL THEN 1 ELSE 0 END", 
                    connection))
                {
                    var result = await checkCmd.ExecuteScalarAsync();
                    tableExists = result != null && Convert.ToInt32(result) == 1;
                }

                if (!tableExists) return null;

                using var getCmd = new SqlCommand(
                    "SELECT TOP 1 LAST_SCRIPT_NUMBER FROM [dbo].[SQL_HISTORY_UPDATE_SCRIPTS] ORDER BY ID DESC", 
                    connection);
                var versionResult = await getCmd.ExecuteScalarAsync();
                return versionResult == null || versionResult == DBNull.Value ? null : versionResult.ToString();
            }
            catch
            {
                return null;
            }
        }

        public string? GetActualProgramVersion(string targetFolder, string targetExeName)
        {
            if (string.IsNullOrEmpty(targetFolder) || string.IsNullOrEmpty(targetExeName)) return null;
            try
            {
                // 1. Try reading tms_version.txt first
                var versionFilePath = Path.Combine(targetFolder, "tms_version.txt");
                if (File.Exists(versionFilePath))
                {
                    try
                    {
                        var fileContent = File.ReadAllText(versionFilePath).Trim();
                        if (!string.IsNullOrEmpty(fileContent) && Regex.IsMatch(fileContent, @"^\d+(\.\d+)*$"))
                        {
                            return fileContent;
                        }
                    }
                    catch
                    {
                        // Ignore and fallback
                    }
                }

                // 2. Fallback: Find the active executable (resolving shortcuts, etc.)
                string activeExeName = FindActiveProductiveExe(targetFolder, targetExeName, _ => { });
                string exePath = Path.Combine(targetFolder, activeExeName);
                if (!File.Exists(exePath)) return null;

                var versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(exePath);
                var versionStr = versionInfo.ProductVersion ?? versionInfo.FileVersion;
                if (!string.IsNullOrEmpty(versionStr))
                {
                    // Clean it to be 3-level (e.g., 1.5.0)
                    var match = Regex.Match(versionStr, @"^(\d+\.\d+\.\d+)");
                    if (match.Success)
                    {
                        return match.Groups[1].Value;
                    }
                }
            }
            catch
            {
                // Ignore
            }
            return null;
        }

        #endregion
    }
}

