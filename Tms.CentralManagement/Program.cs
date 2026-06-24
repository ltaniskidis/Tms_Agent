using Microsoft.EntityFrameworkCore;
using Tms.CentralManagement.Data;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Prevent cycle reference issues in JSON serialization
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

builder.Services.AddRazorPages(options =>
{
    options.Conventions.AuthorizeFolder("/");
    options.Conventions.AllowAnonymousToPage("/Login");
});

builder.Services.AddAuthentication(Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Login";
        options.ExpireTimeSpan = TimeSpan.FromHours(2);
    });

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Register DbContext with SQLite
builder.Services.AddDbContext<CentralDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=central.db"));

var app = builder.Build();

// Auto-migration & Seeding
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<CentralDbContext>();
    context.Database.EnsureCreated();

    // Check and add missing columns to ClientProfiles table for SQLite
    try
    {
        using (var connection = context.Database.GetDbConnection())
        {
            connection.Open();
            
            // 1. ClientProfiles columns check
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(ClientProfiles);";
                var columns = new List<string>();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        columns.Add(reader["name"].ToString() ?? "");
                    }
                }

                var columnsToAdd = new Dictionary<string, string>
                {
                    { "ConnectionStringType", "TEXT NULL" },
                    { "DbServer", "TEXT NULL" },
                    { "DbName", "TEXT NULL" },
                    { "DbUser", "TEXT NULL" },
                    { "DbPassword", "TEXT NULL" },
                    { "DbUseWindowsAuth", "INTEGER NOT NULL DEFAULT 0" },
                    { "ConfigFilePath", "TEXT NULL" },
                    { "Emails", "TEXT NULL" }
                };

                foreach (var col in columnsToAdd)
                {
                    if (!columns.Contains(col.Key, StringComparer.OrdinalIgnoreCase))
                    {
                        using (var alterCommand = connection.CreateCommand())
                        {
                            alterCommand.CommandText = $"ALTER TABLE ClientProfiles ADD COLUMN {col.Key} {col.Value};";
                            alterCommand.ExecuteNonQuery();
                        }
                    }
                }
            }

            // 2. Versions columns check
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(Versions);";
                var columns = new List<string>();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        columns.Add(reader["name"].ToString() ?? "");
                    }
                }

                if (!columns.Contains("AffectedSystemComponent", StringComparer.OrdinalIgnoreCase))
                {
                    using (var alterCommand = connection.CreateCommand())
                    {
                        alterCommand.CommandText = "ALTER TABLE Versions ADD COLUMN AffectedSystemComponent TEXT NULL;";
                        alterCommand.ExecuteNonQuery();
                    }
                }

                if (!columns.Contains("IsCurrent", StringComparer.OrdinalIgnoreCase))
                {
                    using (var alterCommand = connection.CreateCommand())
                    {
                        alterCommand.CommandText = "ALTER TABLE Versions ADD COLUMN IsCurrent INTEGER NOT NULL DEFAULT 0;";
                        alterCommand.ExecuteNonQuery();
                    }
                }

                // Initialize NULL values for the new columns in database
                using (var updateCommand = connection.CreateCommand())
                {
                    updateCommand.CommandText = "UPDATE Versions SET AffectedSystemComponent = 'Both' WHERE AffectedSystemComponent IS NULL;";
                    updateCommand.ExecuteNonQuery();
                }
            }

            // 3. ConsoleUsers columns check
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(ConsoleUsers);";
                var columns = new List<string>();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        columns.Add(reader["name"].ToString() ?? "");
                    }
                }

                if (!columns.Contains("Scope", StringComparer.OrdinalIgnoreCase))
                {
                    using (var alterCommand = connection.CreateCommand())
                    {
                        alterCommand.CommandText = "ALTER TABLE ConsoleUsers ADD COLUMN Scope TEXT NOT NULL DEFAULT 'Console';";
                        alterCommand.ExecuteNonQuery();
                    }

                    // Update existing owner to Both
                    using (var updateCommand = connection.CreateCommand())
                    {
                        updateCommand.CommandText = "UPDATE ConsoleUsers SET Scope = 'Both' WHERE lower(Username) = 'owner';";
                        updateCommand.ExecuteNonQuery();
                    }
                }
            }

            // 4. Clients table columns check
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(Clients);";
                var columns = new List<string>();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        columns.Add(reader["name"].ToString() ?? "");
                    }
                }

                if (!columns.Contains("StartWithWindows", StringComparer.OrdinalIgnoreCase))
                {
                    using (var alterCommand = connection.CreateCommand())
                    {
                        alterCommand.CommandText = "ALTER TABLE Clients ADD COLUMN StartWithWindows INTEGER NOT NULL DEFAULT 0;";
                        alterCommand.ExecuteNonQuery();
                    }
                }
            }

            // Check if BroadcastMessages table exists and create it if missing
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='BroadcastMessages';";
                var tableExists = command.ExecuteScalar() != null;
                if (!tableExists)
                {
                    using (var createTableCommand = connection.CreateCommand())
                    {
                        createTableCommand.CommandText = @"
                            CREATE TABLE BroadcastMessages (
                                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                                Title TEXT NOT NULL,
                                Content TEXT NOT NULL,
                                CreatedDate TEXT NOT NULL,
                                IsActive INTEGER NOT NULL,
                                TargetClientApiKey TEXT NULL
                            );";
                        createTableCommand.ExecuteNonQuery();
                    }
                }
            }

            // Check if SupportTickets table exists and create it if missing
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='SupportTickets';";
                var tableExists = command.ExecuteScalar() != null;
                if (!tableExists)
                {
                    using (var createTableCommand = connection.CreateCommand())
                    {
                        createTableCommand.CommandText = @"
                            CREATE TABLE SupportTickets (
                                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                                ClientGuid TEXT NOT NULL,
                                MachineName TEXT NOT NULL,
                                ApiKey TEXT NOT NULL,
                                Subject TEXT NOT NULL,
                                Body TEXT NOT NULL,
                                CreatedDate TEXT NOT NULL,
                                AttachmentFileName TEXT NULL,
                                Status TEXT NOT NULL,
                                AdminResponse TEXT NULL,
                                ResponseDate TEXT NULL
                            );";
                        createTableCommand.ExecuteNonQuery();
                    }
                }
            }

            // Check if SmtpSettings table exists and create it if missing
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='SmtpSettings';";
                var tableExists = command.ExecuteScalar() != null;
                if (!tableExists)
                {
                    using (var createTableCommand = connection.CreateCommand())
                    {
                        createTableCommand.CommandText = @"
                            CREATE TABLE SmtpSettings (
                                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                                Server TEXT NOT NULL,
                                Port INTEGER NOT NULL,
                                Username TEXT NOT NULL,
                                Password TEXT NOT NULL,
                                EnableSsl INTEGER NOT NULL DEFAULT 1,
                                Sender TEXT NOT NULL
                            );";
                        createTableCommand.ExecuteNonQuery();
                    }
                }
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error checking/adding SQLite columns: {ex.Message}");
    }

            // Update existing version target types to System if they belong to Console/Agent
            var systemVersionsList = new[] { "1.0.0", "1.1.0", "1.1.1", "1.1.2", "1.1.3", "1.1.4", "1.4.1", "1.4.2", "1.5.0" };
            var allDbVersions = context.Versions.ToList();
            foreach (var ver in allDbVersions)
            {
                if (systemVersionsList.Contains(ver.VersionNumber))
                {
                    ver.TargetType = "System";
                    if (string.IsNullOrEmpty(ver.AffectedSystemComponent))
                    {
                        ver.AffectedSystemComponent = "Both";
                    }
                }
            }
            context.SaveChanges();

            // Ensure the latest system version in the database is marked as Current
            var latestSystemVersion = context.Versions
                .Where(v => v.TargetType == "System")
                .AsEnumerable()
                .OrderByDescending(v => Version.TryParse(v.VersionNumber, out var ver) ? ver : new Version(0, 0, 0))
                .FirstOrDefault();

            if (latestSystemVersion != null && !latestSystemVersion.IsCurrent)
            {
                var allSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
                foreach (var v in allSystemVersions)
                {
                    v.IsCurrent = (v.Id == latestSystemVersion.Id);
                }
                context.SaveChanges();
            }

            // Update 1.5.10 description to reflect that it targets both Server & Client
            var v10 = context.Versions.FirstOrDefault(v => v.VersionNumber == "1.5.10");
            if (v10 != null && v10.Description.Contains("Αφορά: Server -"))
            {
                v10.Description = "Αφορά: Server & Client - Διαχείριση, Προβολή & Απάντηση σε Αιτήματα Support";
                context.SaveChanges();
            }

            // Seed versions incrementally if they don't exist
            bool hasChanges = false;

            if (!context.Versions.Any(v => v.VersionNumber == "1.0.0"))
            {
                var initialVersion = new VersionInfo
                {
                    VersionNumber = "1.0.0",
                    ReleaseDate = DateTime.UtcNow.AddDays(-10),
                    Description = "Αρχική έκδοση συστήματος",
                    BinaryFileUrl = "http://localhost:5000/packages/app_1.0.0.zip",
                    TargetType = "System",
                    AffectedSystemComponent = "Both",
                    IsCurrent = false,
                    IsActive = true
                };
                initialVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αρχική εγκατάσταση της desktop εφαρμογής." });
                context.Versions.Add(initialVersion);
                hasChanges = true;
            }

            if (!context.Versions.Any(v => v.VersionNumber == "1.1.0"))
            {
                var updateVersion = new VersionInfo
                {
                    VersionNumber = "1.1.0",
                    ReleaseDate = DateTime.UtcNow.AddDays(-5),
                    Description = "Ενημέρωση 1.1.0 - Προσθήκη πεδίων πελατών",
                    BinaryFileUrl = "http://localhost:5000/packages/app_1.1.0.zip",
                    TargetType = "System",
                    AffectedSystemComponent = "Both",
                    IsCurrent = false,
                    IsActive = true
                };
                updateVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "1. Προσθήκη πεδίου AFM στη βάση δεδομένων." });
                updateVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "2. Διόρθωση σφάλματος κατά την εκκίνηση της εφαρμογής." });
                
                updateVersion.Scripts.Add(new SqlScript
                {
                    ScriptName = "01_Add_AFM_Column.sql",
                    ScriptContent = @"
-- Check if column exists, if not add it
IF NOT EXISTS (
    SELECT * FROM sys.columns 
    WHERE object_id = OBJECT_ID(N'[dbo].[Customers]') 
    AND name = N'AFM'
)
BEGIN
    ALTER TABLE [dbo].[Customers] ADD [AFM] NVARCHAR(10) NULL;
END
GO
",
                    SequenceOrder = 1
                });
                updateVersion.Scripts.Add(new SqlScript
                {
                    ScriptName = "02_Insert_Sample_Data.sql",
                    ScriptContent = @"
-- Update dummy status
UPDATE [dbo].[Customers] SET [AFM] = '000000000' WHERE [AFM] IS NULL;
GO
",
                    SequenceOrder = 2
                });

                context.Versions.Add(updateVersion);
                hasChanges = true;
            }

    if (!context.Versions.Any(v => v.VersionNumber == "1.1.1"))
    {
        var bulkScriptVersion = new VersionInfo
        {
            VersionNumber = "1.1.1",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Υποστήριξη μαζικού αρχείου σεναρίων SQL (.txt)",
            BinaryFileUrl = "http://localhost:5000/packages/app_1.1.1.zip",
            IsActive = true,
            TargetType = "System"
        };
        bulkScriptVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Δυνατότητα μεταφόρτωσης μαζικού αρχείου .txt με SQL scripts." });
        bulkScriptVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Έλεγχος ύπαρξης πίνακα SQL_HISTORY_UPDATE_SCRIPTS στη βάση πελάτη." });
        bulkScriptVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Ανάλυση αρχείου .txt, εύρεση του LAST_SCRIPT_NUMBER και εκτέλεση των επόμενων σεναρίων." });
        bulkScriptVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Εγγραφή του νέου LAST_SCRIPT_NUMBER στον πίνακα ιστορικού μετά από κάθε επιτυχή εκτέλεση." });

        context.Versions.Add(bulkScriptVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.1.2"))
    {
        var discoveryVersion = new VersionInfo
        {
            VersionNumber = "1.1.2",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Αυτόματη ανίχνευση SQL instances και βάσεων δεδομένων",
            BinaryFileUrl = "http://localhost:5000/packages/app_1.1.2.zip",
            IsActive = true,
            TargetType = "System"
        };
        discoveryVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Πίνακας ClientDatabases και αποθήκευση ανιχνευμένων βάσεων δεδομένων." });
        discoveryVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Διαχείριση ενεργοποίησης/απενεργοποίησης (Monitored/Ignored) βάσεων στο Dashboard." });
        discoveryVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Ανίχνευση SQL Server Instances μέσω Windows Registry (mssqlserver και named instances)." });
        discoveryVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Ανάκτηση ενεργών βάσεων δεδομένων μέσω sys.databases (παράλειψη system/offline dbs)." });
        discoveryVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Φιλτράρισμα και εκτέλεση SQL scripts μόνο για τις εγκεκριμένες (monitored) βάσεις." });

        context.Versions.Add(discoveryVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.1.3"))
    {
        var securityVersion = new VersionInfo
        {
            VersionNumber = "1.1.3",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Προσθήκη κωδικού έγκρισης αναβάθμισης",
            BinaryFileUrl = "http://localhost:5000/packages/app_1.1.3.zip",
            SecurityCode = "tms123",
            IsActive = true,
            TargetType = "System"
        };
        securityVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Δυνατότητα ορισμού κωδικού ασφαλείας κατά την έκδοση." });
        securityVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Απαίτηση εισαγωγής κωδικού από τον χρήστη πριν την εκτέλεση της αναβάθμισης." });
        securityVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Εκτέλεση του Agent πάντα με δικαιώματα Administrator." });

        context.Versions.Add(securityVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.1.4"))
    {
        var finalReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.1.4",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Διαχείριση κωδικού, δικαιώματα admin, παράκαμψη UAC και λογότυπο Clever Data",
            BinaryFileUrl = "/packages/app_1.1.4.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            TargetType = "System"
        };
        finalReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server & Client - Διαχείριση κωδικού έγκρισης αναβάθμισης (καταχώρηση και επιβεβαίωση)." });
        finalReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Αυτόματη εγγραφή στο Task Scheduler για εκκίνηση με τα Windows με Highest privileges (παράκαμψη UAC)." });
        finalReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Υποστήριξη παραμέτρου --startup για silent εκκίνηση στο system tray." });
        finalReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Ενσωμάτωση λογοτύπου Clever Data στο Window και στο Tray icon." });

        context.Versions.Add(finalReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.4.1"))
    {
        var securityReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.4.1",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Ασφάλεια Κονσόλας, API Keys, Χρήστες & Δικαιώματα Agent",
            BinaryFileUrl = "/packages/app_1.4.1.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            TargetType = "System"
        };
        securityReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server & Client - Πιστοποίηση των Agents μέσω API Key και έλεγχος δικαιώματος λήψης αναβαθμίσεων." });
        securityReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Υλοποίηση Cookie Authentication στην Κεντρική Κονσόλα και διαχείριση πελατών/χρηστών." });
        securityReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Νέα οθόνη Login κατά την εκκίνηση και τοπική προσωρινή αποθήκευση χρηστών (offline caching)." });
        securityReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Κλείδωμα τοπικών ρυθμίσεων (Server URL, API Key, προφίλ) μόνο για τον Owner." });
        securityReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Εμφάνιση μηνύματος επικοινωνίας με τον προμηθευτή εάν οι αναβαθμίσεις έχουν απενεργοποιηθεί κεντρικά." });

        context.Versions.Add(securityReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.4.2"))
    {
        var builderReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.4.2",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Κατασκευαστής Connection String & Αναζήτηση Φακέλου με Windows Explorer",
            BinaryFileUrl = "/packages/app_1.4.2.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            TargetType = "System"
        };
        builderReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server & Client - Αυτόματη κατασκευή του connection string βάσει SQL Server Host/Instance, DB Name, Credentials ή Windows Auth." });
        builderReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server & Client - Υποστήριξη αυτόματης ανάγνωσης connection string από τοπικό αρχείο ρυθμίσεων (.config, .json, .xml)." });
        builderReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server & Client - Κουμπί 'Αναζήτηση...' που ανοίγει τον Windows Explorer για την εύρεση φακέλων/αρχείων." });

        context.Versions.Add(builderReleaseVersion);
        hasChanges = true;
    }
 
    if (!context.Versions.Any(v => v.VersionNumber == "1.5.0"))
    {
        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.0",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Έξυπνες Προτάσεις Διαδρομών, Λίστα Βάσεων Δεδομένων & Διορθώσεις Δυναμικών Φορμών",
            BinaryFileUrl = "/packages/app_1.5.0.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Έξυπνο σύστημα προτεινόμενων τοπικών διαδρομών (Smart Folder Suggestions) για την παράκαμψη των περιορισμών ασφαλείας του browser." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Αναλυτική εμφάνιση των συνδεδεμένων βάσεων εταιρειών και ανιχνευμένων βάσεων Agent στον κεντρικό πίνακα πελατών." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Ενσωμάτωση Anti-Forgery Tokens στις δυναμικές φόρμες της JavaScript για την αποφυγή σφαλμάτων 400 Bad Request κατά τη διαγραφή." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Άμεση απόκρυψη των εταιρειών που έχουν μαρκαριστεί για διαγραφή από όλες τις λίστες της κονσόλας." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.5"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.5",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Προσθήκη Διαχείρισης Χρηστών Κονσόλας και Agents",
            BinaryFileUrl = "/packages/app_1.5.5.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Διεπαφή διαχείρισης χρηστών κονσόλας και αυτόματη αρχική τροφοδοσία του owner." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server & Client - Δυνατότητα αλλαγής κωδικού του owner από την κονσόλα και αυτόματης διάδοσης σε όλους τους Agents." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Διεπαφή τοπικής διαχείρισης χρηστών (Admin/Operator) στον Agent και αυτόματου συγχρονισμού στην κεντρική κονσόλα." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Αναβάθμιση ασφάλειας σύνδεσης με προτεραιότητα στη βάση τοπικών χρηστών." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.6"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.6",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Προσθήκη Στήλης/Πεδίου Εφαρμογής (Scope) στους Χρήστες",
            BinaryFileUrl = "/packages/app_1.5.6.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server & Client - Προσθήκη στήλης/πεδίου στην κονσόλα και στον Agent που δείχνει αν ο χρήστης εφαρμόζεται σε Κονσόλα, Agent ή και στα δύο." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.7"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.7",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Δυναμική Επιλογή Εφαρμογής (Scope) στους Χρήστες",
            BinaryFileUrl = "/packages/app_1.5.7.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Δυνατότητα ορισμού και επεξεργασίας της εμβέλειας (Scope) του χρήστη (Κονσόλα, Agent ή και τα 2) κατά τη δημιουργία/επεξεργασία στην κονσόλα." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server & Client - Αυτόματος συγχρονισμός και αποστολή όλων των χρηστών της κονσόλας με scope Agent ή Both στους Agents." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.8"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.8",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Client - Απόκρυψη του χρήστη owner από τη διεπαφή του Agent",
            BinaryFileUrl = "/packages/app_1.5.8.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Πλήρης απόκρυψη του global owner χρήστη από τη λίστα διαχείρισης χρηστών στον Agent για λόγους ασφαλείας, διατηρώντας τον μόνο στο τοπικό αρχείο users.json για την ταυτοποίηση." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.9"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.9",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Αποστολή Support Emails & Διαφημιστικά/Ενημερώσεις",
            BinaryFileUrl = "/packages/app_1.5.9.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server & Client - Δυνατότητα αποστολής emails στο support (support@cleverdata.gr, l.taniskidis@cleverdata.gr, e.kordouli@cleverdata.gr) με θέμα, περιεχόμενο και screenshot/attachment από τον Agent." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Διεπαφή διαχείρισης και αποστολής διαφημιστικών/ενημερώσεων (Broadcasts) στους Agents." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Δυνατότητα καταχώρησης πολλαπλών emails ανά εταιρεία/προφίλ (ClientProfile) για επικοινωνία." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Νέα Tabs επικοινωνίας και ενημερώσεων με ένδειξη (🔴) για νέα μη αναγνωσμένα μηνύματα στον Agent." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.10"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.10",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Διαχείριση, Προβολή & Απάντηση σε Αιτήματα Support",
            BinaryFileUrl = "/packages/app_1.5.10.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Νέα σελίδα «Αιτήματα Support» στην κονσόλα διαχείρισης για προβολή, λήψη screenshot και απάντηση στα tickets." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Αυτόματη αποστολή email απάντησης (με SMTP ή fallback txt αρχείο) προς όλα τα δηλωμένα emails της εταιρείας κατά την επίλυση του ticket." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.11"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.11",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Ρυθμίσεις SMTP στην κονσόλα & Popups ενημερώσεων στον Agent",
            BinaryFileUrl = "/packages/app_1.5.11.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Νέα σελίδα «Ρυθμίσεις SMTP» στην κονσόλα για εύκολη διαχείριση της αλληλογραφίας μέσω βάσης." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Άμεση popup ειδοποίηση (MessageBox) στον χρήστη όταν λαμβάνει νέα γενική ενημέρωση κατά το check-in." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.12"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.12",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Ειδοποιήσεις Tray (Balloon Tips) στον Agent για ανακοινώσεις",
            BinaryFileUrl = "/packages/app_1.5.12.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Εμφάνιση ειδοποίησης (Balloon Tip) από το system tray όταν υπάρχει νέα ανακοίνωση." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Αυτόματη εστίαση και άνοιγμα της καρτέλας «Ενημερώσεις & Νέα» κατά το κλικ στην ειδοποίηση του tray." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.13"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.13",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server - Όνομα Αποστολέα (Display Name) & Στοιχεία Επιχείρησης στα support emails",
            BinaryFileUrl = "/packages/app_1.5.13.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Ορισμός του 'Clever_Support' ως Display Name αποστολέα στα emails υποστήριξης και απαντήσεων." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Συμπερίληψη των στοιχείων/ονομάτων των επιχειρήσεων (Client Profiles) στα emails υποστήριξης." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.14"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.14",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Διόρθωση JS επιλογής support tickets στην κονσόλα",
            BinaryFileUrl = "/packages/app_1.5.14.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Διόρθωση JS σφάλματος κατά την επιλογή support ticket στην κονσόλα διαχείρισης λόγω Casing." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.15"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.15",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server - Δυνατότητα αλλαγής κατάστασης στα αιτήματα support",
            BinaryFileUrl = "/packages/app_1.5.15.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Προσθήκη επιλογής κατάστασης (Ανοιχτό, Παραλήφθηκε, Ανατέθηκε, Σε έλεγχο, Επιλύθηκε) στις λεπτομέρειες των αιτημάτων support." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Αυτόματη αποστολή email ειδοποίησης επίλυσης στον πελάτη όταν ένα αίτημα τίθεται ως Επιλύθηκε." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.16"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.16",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Ιστορικό αιτημάτων support, ειδοποιήσεις, αποθήκευση σύνδεσης & tray actions",
            BinaryFileUrl = "/packages/app_1.5.16.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server & Client - Προσθήκη API και UI για την εμφάνιση ιστορικού αιτημάτων υποστήριξης στον Agent." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Λήψη Balloon Tip ειδοποιήσεων στο system tray όταν αλλάζει η κατάσταση ενός αιτήματος." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Αποθήκευση στοιχείων σύνδεσης (Username & Password) ανά PC με την επιλογή 'Να με θυμάσαι'." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Προσθήκη επιλογών 'Γράψτε ένα αίτημα support' και 'Εκδόσεις Desktop Προγράμματος' στο tray context menu." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.17"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.17",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Επιλογή αυτόματης εκκίνησης με τα Windows (ως service / boot)",
            BinaryFileUrl = "/packages/app_1.5.17.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server & Client - Προσθήκη επιλογής 'Εκκίνηση με τα Windows' στις ρυθμίσεις του Agent και στην κεντρική κονσόλα διαχείρισης." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Υλοποίηση διπλής λειτουργίας εκτέλεσης (ως Windows Service και ως interactive EXE με Tray icon)." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Μεταφορά και αυτόματη μετακίνηση των αρχείων ρυθμίσεων στον κοινό φάκελο C:\\ProgramData\\TmsAgent για κοινή χρήση από το Service και το GUI." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.ConsoleUsers.Any())
    {
        context.ConsoleUsers.Add(new ConsoleUser
        {
            Username = "admin",
            PasswordHash = "clever2026",
            Role = "SuperAdmin",
            Scope = "Console"
        });
        hasChanges = true;
    }

    if (!context.ConsoleUsers.Any(u => u.Username.ToLower() == "owner"))
    {
        context.ConsoleUsers.Add(new ConsoleUser
        {
            Username = "owner",
            PasswordHash = "clever2026owner",
            Role = "SuperAdmin",
            Scope = "Both"
        });
        hasChanges = true;
    }

    if (hasChanges)
    {
        context.SaveChanges();
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseStaticFiles();

// Serve update packages directly from PublishAndSetup directory
var publishAndSetupDir = Tms.CentralManagement.Helpers.PathHelper.GetPublishAndSetupPath();
if (!Directory.Exists(publishAndSetupDir))
{
    Directory.CreateDirectory(publishAndSetupDir);
}
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(publishAndSetupDir),
    RequestPath = "/packages"
});

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapRazorPages();

app.Run();

