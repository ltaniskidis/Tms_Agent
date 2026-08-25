using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Xml;

namespace Tms.Agent.Core.Models
{
    public class LocalProfile
    {
        public string ProfileId { get; set; } = Guid.NewGuid().ToString();
        public string ProfileName { get; set; } = string.Empty;
        public string Afm { get; set; } = string.Empty;
        public string TargetFolder { get; set; } = string.Empty;
        public string TargetExeName { get; set; } = "TIMOLOGISI.exe";
        
        // Connection string settings
        public string ConnectionString { get; set; } = string.Empty;
        public string ConnectionStringType { get; set; } = "Direct"; // Direct, Builder, ConfigFile
        public string DbServer { get; set; } = string.Empty;
        public string DbName { get; set; } = string.Empty;
        public string DbUser { get; set; } = string.Empty;
        public string DbPassword { get; set; } = string.Empty;
        public bool DbUseWindowsAuth { get; set; } = false;
        public string ConfigFilePath { get; set; } = string.Empty;

        public string CurrentVersion { get; set; } = "1.0.0";

        private string? _currentProgramVersion;
        public string CurrentProgramVersion
        {
            get => !string.IsNullOrEmpty(_currentProgramVersion) ? _currentProgramVersion : CurrentVersion;
            set => _currentProgramVersion = value;
        }

        private string? _currentDbVersion;
        public string CurrentDbVersion
        {
            get => !string.IsNullOrEmpty(_currentDbVersion) ? _currentDbVersion : CurrentVersion;
            set => _currentDbVersion = value;
        }

        public string SerialNumber { get; set; } = string.Empty;
        public int ActiveUsersCount { get; set; } = 0;

        // Resolved connection string helper
        [JsonIgnore]
        public string ResolvedConnectionString => GetResolvedConnectionString();

        public string GetResolvedConnectionString(Action<string>? log = null)
        {
            if (string.Equals(ConnectionStringType, "Builder", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(DbServer))
                {
                    log?.Invoke("Σφάλμα: Ο SQL Server δεν έχει οριστεί.");
                    return string.Empty;
                }

                var parts = new List<string> { $"Server={DbServer}" };
                if (!string.IsNullOrWhiteSpace(DbName))
                {
                    parts.Add($"Database={DbName}");
                }

                if (DbUseWindowsAuth)
                {
                    parts.Add("Integrated Security=True");
                }
                else
                {
                    if (!string.IsNullOrWhiteSpace(DbUser)) parts.Add($"User Id={DbUser}");
                    if (!string.IsNullOrWhiteSpace(DbPassword)) parts.Add($"Password={DbPassword}");
                }

                parts.Add("TrustServerCertificate=True");
                var built = string.Join(";", parts) + ";";
                log?.Invoke($"Κατασκευή Connection String: {built.Replace(DbPassword, "******")}");
                return built;
            }
            else if (string.Equals(ConnectionStringType, "ConfigFile", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(ConfigFilePath))
                {
                    log?.Invoke("Προειδοποίηση: Η διαδρομή αρχείου ρυθμίσεων είναι κενή. Χρήση fallback Connection String.");
                    return ConnectionString;
                }

                if (!File.Exists(ConfigFilePath))
                {
                    log?.Invoke($"Προειδοποίηση: Το αρχείο ρυθμίσεων '{ConfigFilePath}' δεν βρέθηκε. Χρήση fallback Connection String.");
                    return ConnectionString;
                }

                try
                {
                    var content = File.ReadAllText(ConfigFilePath);
                    
                    // JSON Format (.json)
                    if (ConfigFilePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        try
                        {
                            using (var doc = JsonDocument.Parse(content))
                            {
                                var conn = FindConnectionStringInJson(doc.RootElement);
                                if (!string.IsNullOrEmpty(conn))
                                {
                                    log?.Invoke($"Ανάγνωση Connection String από JSON αρχείο: {ConfigFilePath}");
                                    return conn;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            log?.Invoke($"Σφάλμα ανάλυσης JSON: {ex.Message}");
                        }
                    }
                    else if (ConfigFilePath.EndsWith(".ini", StringComparison.OrdinalIgnoreCase) || content.Contains("[SQLSERVER]"))
                    {
                        try
                        {
                            var dict = new Dictionary<string, (string Value, bool IsExplicit)>();
                            var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                            foreach (var rawLine in lines)
                            {
                                var line = rawLine.Trim();
                                if (line.StartsWith("--") || line.StartsWith("#") || line.StartsWith(";"))
                                    continue;

                                if (line.Contains(":") || line.Contains("="))
                                {
                                    bool isExplicit = line.Contains(":=");
                                    string separator = isExplicit ? ":=" : ":";
                                    int sepIndex = line.IndexOf(separator);
                                    if (sepIndex > 0)
                                    {
                                        string key = line.Substring(0, sepIndex).Trim().TrimStart('%').ToUpper();
                                        string val = line.Substring(sepIndex + separator.Length).Trim();

                                        if (!dict.ContainsKey(key) || isExplicit || !dict[key].IsExplicit)
                                        {
                                            dict[key] = (val, isExplicit);
                                        }
                                    }
                                }
                            }

                            if (dict.ContainsKey("SERVER") && dict.ContainsKey("DATABASENAME"))
                            {
                                string server = dict["SERVER"].Value;
                                string db = dict["DATABASENAME"].Value;
                                string user = dict.ContainsKey("USER") ? dict["USER"].Value : "";
                                string pass = dict.ContainsKey("SAPASSWORD") ? dict["SAPASSWORD"].Value : "";
                                string instance = dict.ContainsKey("INSTANCE") ? dict["INSTANCE"].Value : "";
                                string ifInstance = dict.ContainsKey("IFINSTANCE") ? dict["IFINSTANCE"].Value : "";

                                // Build server address
                                string serverAddress = server;
                                if (!string.IsNullOrEmpty(instance) && 
                                    (string.Equals(ifInstance, "true", StringComparison.OrdinalIgnoreCase) || 
                                     !server.Contains("\\")))
                                {
                                    serverAddress = $"{server}\\{instance}";
                                }

                                var parts = new List<string> { $"Server={serverAddress}", $"Database={db}" };
                                if (!string.IsNullOrEmpty(user))
                                {
                                    parts.Add($"User Id={user}");
                                    if (!string.IsNullOrEmpty(pass))
                                    {
                                        parts.Add($"Password={pass}");
                                    }
                                }
                                else
                                {
                                    parts.Add("Integrated Security=True");
                                }
                                parts.Add("TrustServerCertificate=True");

                                var built = string.Join(";", parts) + ";";
                                log?.Invoke($"Ανάγνωση Connection String από INI αρχείο: {ConfigFilePath}");
                                return built;
                            }
                            else
                            {
                                log?.Invoke($"Προειδοποίηση: Το INI αρχείο '{ConfigFilePath}' δεν περιέχει SERVER ή DATABASENAME.");
                            }
                        }
                        catch (Exception ex)
                        {
                            log?.Invoke($"Σφάλμα ανάλυσης INI αρχείου: {ex.Message}");
                        }
                    }
                    else // XML / Config Format
                    {
                        try
                        {
                            var xml = new XmlDocument();
                            xml.LoadXml(content);

                            // Try connectionStrings section
                            var connNodes = xml.SelectNodes("//connectionStrings/add");
                            if (connNodes != null && connNodes.Count > 0)
                            {
                                foreach (XmlNode node in connNodes)
                                {
                                    var connAttr = node.Attributes?["connectionString"]?.Value;
                                    if (!string.IsNullOrEmpty(connAttr))
                                    {
                                        log?.Invoke($"Ανάγνωση Connection String από XML node <connectionStrings>: {ConfigFilePath}");
                                        return connAttr;
                                    }
                                }
                            }

                            // Try appSettings section
                            var appNodes = xml.SelectNodes("//appSettings/add");
                            if (appNodes != null && appNodes.Count > 0)
                            {
                                foreach (XmlNode node in appNodes)
                                {
                                    var keyAttr = node.Attributes?["key"]?.Value;
                                    var valAttr = node.Attributes?["value"]?.Value;
                                    if (keyAttr != null && keyAttr.Contains("ConnectionString", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(valAttr))
                                    {
                                        log?.Invoke($"Ανάγνωση Connection String από XML node <appSettings>: {ConfigFilePath}");
                                        return valAttr;
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            log?.Invoke($"Σφάλμα ανάλυσης XML: {ex.Message}");
                        }
                    }

                    // Fallback to regex
                    var regex = new Regex(
                        @"(?:Server|Data\s+Source)\s*=\s*[^;""']+(?:;\s*(?:Database|Initial\s+Catalog)\s*=\s*[^;""']+)+",
                        RegexOptions.IgnoreCase);
                    var match = regex.Match(content);
                    if (match.Success)
                    {
                        log?.Invoke($"Ανάγνωση Connection String με Regex από το αρχείο ρυθμίσεων: {ConfigFilePath}");
                        return match.Value;
                    }
                }
                catch (Exception ex)
                {
                    log?.Invoke($"Σφάλμα ανάγνωσης αρχείου ρυθμίσεων: {ex.Message}");
                }

                log?.Invoke($"Αποτυχία εύρεσης Connection String στο αρχείο. Χρήση fallback: {ConnectionString}");
                return ConnectionString;
            }

            // Direct fallback
            return ConnectionString;
        }

        private string? FindConnectionStringInJson(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in element.EnumerateObject())
                {
                    if (prop.Name.Equals("ConnectionString", StringComparison.OrdinalIgnoreCase) ||
                        prop.Name.Equals("DefaultConnection", StringComparison.OrdinalIgnoreCase) ||
                        prop.Name.Equals("ConnectionStrings", StringComparison.OrdinalIgnoreCase))
                    {
                        if (prop.Value.ValueKind == JsonValueKind.String)
                        {
                            return prop.Value.GetString();
                        }
                        else if (prop.Value.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var childProp in prop.Value.EnumerateObject())
                            {
                                if (childProp.Value.ValueKind == JsonValueKind.String)
                                {
                                    return childProp.Value.GetString();
                                }
                            }
                        }
                    }

                    var result = FindConnectionStringInJson(prop.Value);
                    if (result != null) return result;
                }
            }
            return null;
        }
    }
}
