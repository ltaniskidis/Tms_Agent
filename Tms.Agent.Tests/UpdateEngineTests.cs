using System;
using System.IO;
using System.Linq;
using Tms.Agent.Core.Models;
using Tms.Agent.Core.Services;
using Xunit;

namespace Tms.Agent.Tests
{
    public class UpdateEngineTests
    {
        [Fact]
        public void InspectFlessasClients()
        {
            var dbPath = @"C:\Users\Administrator\OneDrive - CLEVER DATA\sources\repos\Tms_Agent\Tms.CentralManagement\central.db";
            using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT Id, ApiKey, MachineName, AgentVersion, MachineRole, ClientGuid, RegistrationDate FROM Clients WHERE ApiKey = 'TMS-KEY-5078EAC505274E73'";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var info = $"Id: {reader.GetValue(0)} | ApiKey: {reader.GetValue(1)} | Name: {reader.GetValue(2)} | Ver: {reader.GetValue(3)} | Role: {reader.GetValue(4)} | Guid: {reader.GetValue(5)} | Date: {reader.GetValue(6)}";
                            Console.WriteLine($"[FLESSAS_CLIENT_INSPECT] {info}");
                        }
                    }
                }
            }
        }

        [Fact]
        public void InspectUpdateLogs()
        {
            var dbPath = @"C:\Users\Administrator\OneDrive - CLEVER DATA\sources\repos\Tms_Agent\Tms.CentralManagement\central.db";
            using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}"))
            {
                conn.Open();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT ClientProfileId, VersionNumber, ProgramVersion, DbVersion, Success, ErrorMessage, ExecutionTime FROM UpdateLogs ORDER BY ExecutionTime DESC LIMIT 20";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var info = $"ProfileId: {reader.GetValue(0)} | Ver: {reader.GetValue(1)} | ProgVer: {reader.GetValue(2)} | DbVer: {reader.GetValue(3)} | Success: {reader.GetValue(4)} | Err: {reader.GetValue(5)} | Time: {reader.GetValue(6)}";
                            Console.WriteLine($"[LOG_INSPECT] {info}");
                        }
                    }
                }
            }
        }

        [Fact]
        public void SplitSqlScript_SplitsCorrectlyByGo()
        {
            // Arrange
            var script = @"
-- Create Table
CREATE TABLE [dbo].[Test] (
    [Id] INT IDENTITY(1,1) NOT NULL
)
GO
-- Insert Data
INSERT INTO [dbo].[Test] DEFAULT VALUES
GO
";

            // Act
            var commands = UpdateEngine.SplitSqlScript(script).ToList();

            // Assert
            Assert.Equal(2, commands.Count);
            Assert.Contains("CREATE TABLE", commands[0]);
            Assert.Contains("INSERT INTO", commands[1]);
        }

        [Fact]
        public void SplitSqlScript_HandlesGoCaseInsensitivelyAndSpacing()
        {
            // Arrange
            var script = "SELECT 1\n  go  \nSELECT 2\n\tGO\nSELECT 3";

            // Act
            var commands = UpdateEngine.SplitSqlScript(script).ToList();

            // Assert
            Assert.Equal(3, commands.Count);
            Assert.Equal("SELECT 1", commands[0]);
            Assert.Equal("SELECT 2", commands[1]);
            Assert.Equal("SELECT 3", commands[2]);
        }

        [Fact]
        public void SplitSqlScript_HandlesEmptyOrNull()
        {
            // Act & Assert
            Assert.Empty(UpdateEngine.SplitSqlScript(null!));
            Assert.Empty(UpdateEngine.SplitSqlScript(string.Empty));
            Assert.Empty(UpdateEngine.SplitSqlScript("   "));
        }

        [Fact]
        public void ProfileManager_LoadsAndSavesProfiles()
        {
            // Arrange
            var tempFile = Path.Combine(Path.GetTempPath(), "TmsAgentTests_" + Guid.NewGuid() + ".json");
            var manager = new ProfileManager(tempFile);

            var originalProfiles = new System.Collections.Generic.List<LocalProfile>
            {
                new LocalProfile
                {
                    ProfileId = "test-id-1",
                    ProfileName = "Company A",
                    Afm = "123456789",
                    TargetFolder = "C:\\TargetA",
                    TargetExeName = "App.exe",
                    ConnectionString = "Server=localhost;Database=DbA;Integrated Security=True",
                    CurrentVersion = "1.0.0"
                },
                new LocalProfile
                {
                    ProfileId = "test-id-2",
                    ProfileName = "Company B",
                    Afm = "987654321",
                    TargetFolder = "C:\\TargetB",
                    TargetExeName = "App.exe",
                    ConnectionString = "Server=localhost;Database=DbB;Integrated Security=True",
                    CurrentVersion = "2.0.0"
                }
            };

            try
            {
                // Act
                manager.SaveProfiles(originalProfiles);
                var loadedProfiles = manager.LoadProfiles();

                // Assert
                Assert.Equal(2, loadedProfiles.Count);
                Assert.Equal("Company A", loadedProfiles[0].ProfileName);
                Assert.Equal("123456789", loadedProfiles[0].Afm);
                Assert.Equal("Company B", loadedProfiles[1].ProfileName);
                Assert.Equal("987654321", loadedProfiles[1].Afm);
            }
            finally
            {
                if (File.Exists(tempFile))
                {
                    File.Delete(tempFile);
                }
            }
        }

        [Fact]
        public void ParseBulkScriptFile_ParsesMultipleBlocksCorrectly()
        {
            // Arrange
            var content = @"
-- Some preamble to ignore
SELECT 'ignore';

--1
SELECT 1;
GO
SELECT 1.1;

-- 2
SELECT 2;
GO

--[3]
SELECT 3;
GO

-- 4 --
SELECT 4;
";

            // Act
            var blocks = UpdateEngine.ParseBulkScriptFile(content);

            // Assert
            Assert.Equal(4, blocks.Count);
            
            Assert.Equal("1", blocks[0].ScriptNumber);
            Assert.Contains("SELECT 1;", blocks[0].ScriptContent);
            Assert.Contains("SELECT 1.1;", blocks[0].ScriptContent);

            Assert.Equal("2", blocks[1].ScriptNumber);
            Assert.Equal("SELECT 2;\r\nGO", blocks[1].ScriptContent.Replace("\n", "\r\n").Replace("\r\r\n", "\r\n"));

            Assert.Equal("3", blocks[2].ScriptNumber);
            Assert.Equal("SELECT 3;\r\nGO", blocks[2].ScriptContent.Replace("\n", "\r\n").Replace("\r\r\n", "\r\n"));

            Assert.Equal("4", blocks[3].ScriptNumber);
            Assert.Equal("SELECT 4;", blocks[3].ScriptContent);
        }

        [Fact]
        public void ParseBulkScriptFile_HandlesNullOrEmpty()
        {
            // Act & Assert
            Assert.Empty(UpdateEngine.ParseBulkScriptFile(null!));
            Assert.Empty(UpdateEngine.ParseBulkScriptFile(string.Empty));
            Assert.Empty(UpdateEngine.ParseBulkScriptFile("   "));
        }

        [Fact]
        public void GetDatabaseNameFromConnectionString_ExtractsCorrectly()
        {
            // Arrange
            var connStr1 = "Server=localhost\\SQLEXPRESS;Database=CompanyDb;Integrated Security=True;";
            var connStr2 = "Server=localhost;Initial Catalog=AnotherDb;User Id=sa;Password=secret;";

            // Act
            var db1 = UpdateEngine.GetDatabaseNameFromConnectionString(connStr1);
            var db2 = UpdateEngine.GetDatabaseNameFromConnectionString(connStr2);

            // Assert
            Assert.Equal("CompanyDb", db1);
            Assert.Equal("AnotherDb", db2);
        }

        [Fact]
        public void GetDatabaseNameFromConnectionString_HandlesNullOrInvalid()
        {
            // Act & Assert
            Assert.Equal(string.Empty, UpdateEngine.GetDatabaseNameFromConnectionString(null!));
            Assert.Equal(string.Empty, UpdateEngine.GetDatabaseNameFromConnectionString("Invalid Connection String"));
            Assert.Equal(string.Empty, UpdateEngine.GetDatabaseNameFromConnectionString("Server=localhost;Integrated Security=True;"));
        }

        [Fact]
        public async Task Test_SendSupportEmail_And_CheckBroadcasts()
        {
            var serverUrl = "http://localhost:5007";
            var apiKey = "TMS-KEY-409B441F2E7B4954";
            var engine = new UpdateEngine();

            // Create a dummy attachment file
            var dummyFile = Path.Combine(Path.GetTempPath(), "dummy_screenshot.png");
            File.WriteAllText(dummyFile, "Fake image bytes");

            try
            {
                bool emailResult = await engine.SendSupportEmailAsync(
                    serverUrl,
                    apiKey,
                    "Δοκιμαστικό Θέμα",
                    "Αυτό είναι ένα δοκιμαστικό email από το test runner.",
                    dummyFile
                );

                Assert.True(emailResult, "Αποστολή email απέτυχε.");

                // Verify that the email file was created locally on the server (under SentEmails/)
                var sentEmailsDir = Path.Combine("..", "..", "..", "..", "Tms.CentralManagement", "SentEmails");
                // Also check relative to workspace root directory just in case
                var sentEmailsWorkspaceDir = Path.Combine("..", "..", "..", "Tms.CentralManagement", "SentEmails");
                
                bool foundFile = false;
                if (Directory.Exists(sentEmailsDir) && Directory.GetFiles(sentEmailsDir, "SupportEmail_*.txt").Length > 0)
                {
                    foundFile = true;
                }
                else if (Directory.Exists(sentEmailsWorkspaceDir) && Directory.GetFiles(sentEmailsWorkspaceDir, "SupportEmail_*.txt").Length > 0)
                {
                    foundFile = true;
                }
                
                Assert.True(foundFile, "Δεν βρέθηκε το αποθηκευμένο αρχείο email στον φάκελο SentEmails/");

                // Check for updates / broadcasts
                var checkResponse = await engine.CheckForUpdatesAsync(
                    serverUrl,
                    "test-client",
                    "test-machine",
                    "Both",
                    "1.5.8",
                    apiKey,
                    new System.Collections.Generic.List<LocalProfile>(),
                    false,
                    false
                );

                Assert.NotNull(checkResponse);
                Assert.NotNull(checkResponse.Broadcasts);
            }
            finally
            {
                if (File.Exists(dummyFile))
                {
                    File.Delete(dummyFile);
                }
            }
        }

        [Fact]
        public async Task Test_GetSupportTicketsAsync()
        {
            var serverUrl = "http://localhost:5007";
            var apiKey = "TMS-KEY-409B441F2E7B4954";
            var clientId = "test-client-history";
            var engine = new UpdateEngine();

            // First check-in to associate the client ID
            await engine.CheckForUpdatesAsync(
                serverUrl,
                clientId,
                "test-machine",
                "Both",
                "1.5.16",
                apiKey,
                new System.Collections.Generic.List<LocalProfile>(),
                false,
                false
            );

            // Submit a unique test ticket
            var subject = "Test Ticket " + Guid.NewGuid().ToString();
            bool sendResult = await engine.SendSupportEmailAsync(
                serverUrl,
                apiKey,
                subject,
                "Testing support ticket history retrieval.",
                null
            );
            Assert.True(sendResult);

            // Retrieve tickets using the same clientId
            var tickets = await engine.GetSupportTicketsAsync(serverUrl, apiKey, clientId);
            Assert.NotNull(tickets);
            Assert.NotEmpty(tickets);
            Assert.Contains(tickets, t => t.Subject == subject);
        }

        [Fact]
        public void GetActualProgramVersion_ReturnsCorrectVersionForExistingExe()
        {
            // Arrange
            var currentExe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(currentExe))
            {
                currentExe = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
            }
            Assert.NotNull(currentExe);
            
            var folder = Path.GetDirectoryName(currentExe)!;
            var exeName = Path.GetFileName(currentExe)!;
            var engine = new UpdateEngine();

            // Act
            var version = engine.GetActualProgramVersion(folder, exeName);

            // Assert
            Assert.NotNull(version);
            Assert.Matches(@"^\d+\.\d+\.\d+", version);
        }

        [Fact]
        public async Task GetActualDatabaseVersionAsync_ReturnsNullOnInvalidConnectionString()
        {
            // Arrange
            var engine = new UpdateEngine();
            var invalidConnStr = "Server=invalid_server_name;Database=InvalidDb;Integrated Security=True;Connection Timeout=1;";

            // Act
            var version = await engine.GetActualDatabaseVersionAsync(invalidConnStr);

            // Assert
            Assert.Null(version);
        }
    }
}
