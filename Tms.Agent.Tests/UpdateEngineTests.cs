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
    }
}
