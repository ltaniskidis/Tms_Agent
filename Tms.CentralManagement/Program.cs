using Microsoft.EntityFrameworkCore;
using Tms.CentralManagement.Data;

var builder = WebApplication.CreateBuilder(args);

// Configure Kestrel upload limits (500 MB)
builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 524288000;
});

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // Prevent cycle reference issues in JSON serialization
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });

// Configure multipart form upload limits (500 MB)
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 524288000;
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
{
    var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
    if (string.IsNullOrEmpty(connectionString))
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        // Resolve central.db path reliably (independent of Process Cwd and environment mode)
        var parentDir = System.IO.Directory.GetParent(baseDir)?.Parent?.Parent?.FullName;
        if (parentDir != null && System.IO.File.Exists(System.IO.Path.Combine(parentDir, "central.db")))
        {
            baseDir = parentDir;
        }
        else
        {
            var curDir = System.IO.Directory.GetCurrentDirectory();
            var subProjectDb = System.IO.Path.Combine(curDir, "Tms.CentralManagement");
            if (System.IO.File.Exists(System.IO.Path.Combine(subProjectDb, "central.db")))
            {
                baseDir = subProjectDb;
            }
            else if (System.IO.File.Exists(System.IO.Path.Combine(curDir, "central.db")))
            {
                baseDir = curDir;
            }
        }
        connectionString = $"Data Source={System.IO.Path.Combine(baseDir, "central.db")}";
    }
    Console.WriteLine($"[DATABASE CONNECTION] Using SQLite Connection String: {connectionString}");
    options.UseSqlite(connectionString);
});

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
                    { "Emails", "TEXT NULL" },
                    { "LastUpdatedProgramVersion", "TEXT NULL" },
                    { "LastUpdatedDbVersion", "TEXT NULL" }
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

                // Initialize NULL values for the new columns in database from LastUpdatedVersion
                using (var updateCommand = connection.CreateCommand())
                {
                    updateCommand.CommandText = "UPDATE ClientProfiles SET LastUpdatedProgramVersion = LastUpdatedVersion WHERE LastUpdatedProgramVersion IS NULL;";
                    updateCommand.ExecuteNonQuery();
                }
                using (var updateCommand = connection.CreateCommand())
                {
                    updateCommand.CommandText = "UPDATE ClientProfiles SET LastUpdatedDbVersion = LastUpdatedVersion WHERE LastUpdatedDbVersion IS NULL;";
                    updateCommand.ExecuteNonQuery();
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

            // 5. UpdateLogs table columns check
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "PRAGMA table_info(UpdateLogs);";
                var columns = new List<string>();
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        columns.Add(reader["name"].ToString() ?? "");
                    }
                }

                if (!columns.Contains("ProgramVersion", StringComparer.OrdinalIgnoreCase))
                {
                    using (var alterCommand = connection.CreateCommand())
                    {
                        alterCommand.CommandText = "ALTER TABLE UpdateLogs ADD COLUMN ProgramVersion TEXT NULL;";
                        alterCommand.ExecuteNonQuery();
                    }
                }

                if (!columns.Contains("DbVersion", StringComparer.OrdinalIgnoreCase))
                {
                    using (var alterCommand = connection.CreateCommand())
                    {
                        alterCommand.CommandText = "ALTER TABLE UpdateLogs ADD COLUMN DbVersion TEXT NULL;";
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

            // 6. Check if Customers table exists and create it if missing
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='Customers';";
                var tableExists = command.ExecuteScalar() != null;
                if (!tableExists)
                {
                    using (var createTableCommand = connection.CreateCommand())
                    {
                        createTableCommand.CommandText = @"
                            CREATE TABLE Customers (
                                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                                Name TEXT NOT NULL,
                                Notes TEXT NULL,
                                CreatedDate TEXT NOT NULL
                            );";
                        createTableCommand.ExecuteNonQuery();
                    }
                }
            }

            // 7. Check if CustomerId column exists in Clients table and add it
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

                if (!columns.Contains("CustomerId", StringComparer.OrdinalIgnoreCase))
                {
                    using (var alterCommand = connection.CreateCommand())
                    {
                        alterCommand.CommandText = "ALTER TABLE Clients ADD COLUMN CustomerId INTEGER NULL REFERENCES Customers(Id) ON DELETE SET NULL;";
                        alterCommand.ExecuteNonQuery();
                    }
                }

                if (!columns.Contains("Alias", StringComparer.OrdinalIgnoreCase))
                {
                    using (var alterCommand = connection.CreateCommand())
                    {
                        alterCommand.CommandText = "ALTER TABLE Clients ADD COLUMN Alias TEXT NULL;";
                        alterCommand.ExecuteNonQuery();
                    }
                }
            }
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error checking/adding SQLite columns: {ex.Message}");
    }

    // Auto-match existing clients to customers
    try
    {
        var clientsWithoutCustomer = context.Clients.Where(c => c.CustomerId == null).ToList();
        if (clientsWithoutCustomer.Any())
        {
            foreach (var client in clientsWithoutCustomer)
            {
                // Determine customer name from machine name
                string customerName = "DEFAULT";
                var parts = client.MachineName.Split(new[] { '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    customerName = parts[0].Trim().ToUpper();
                }
                else
                {
                    customerName = client.MachineName.Trim().ToUpper();
                }

                // Clean common generic names
                if (customerName == "DESKTOP" || customerName == "LAPTOP" || customerName == "WORKSTATION" || string.IsNullOrWhiteSpace(customerName))
                {
                    customerName = "OTHER";
                }

                // Find or create customer
                var customer = context.Customers.FirstOrDefault(c => c.Name.ToUpper() == customerName);
                if (customer == null)
                {
                    customer = new Tms.CentralManagement.Data.Customer
                    {
                        Name = customerName,
                        Notes = "Αυτόματη δημιουργία κατά τη μετάβαση συστήματος",
                        CreatedDate = DateTime.UtcNow
                    };
                    context.Customers.Add(customer);
                    context.SaveChanges();
                }

                client.CustomerId = customer.Id;
                if (string.IsNullOrEmpty(client.Alias))
                {
                    client.Alias = client.MachineName;
                }
            }
            context.SaveChanges();
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Error auto-matching clients to customers: {ex.Message}");
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

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.18"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.18",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Client - Προσθήκη Οδηγού Εγκατάστασης (Setup Wizard) κατά την πρώτη εκκίνηση",
            BinaryFileUrl = "/packages/app_1.5.18.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Προσθήκη διαδραστικού οδηγού εγκατάστασης Setup Wizard για εύκολη ρύθμιση της διεύθυνσης Server, API Key, ρόλου και αυτόματης εκκίνησης." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Αυτόματη ανίχνευση μη ρυθμισμένης εγκατάστασης κατά την εκκίνηση της εφαρμογής και προβολή του wizard." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.19"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.19",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Διαχωρισμός εκδόσεων, έλεγχος scripts βάσης, έγκριση αναβάθμισης, responsive UI & ασφαλές backup",
            BinaryFileUrl = "/packages/app_1.5.19.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server & Client - Υλοποίηση ξεχωριστής παρακολούθησης έκδοσης προγράμματος και βάσης δεδομένων." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server & Client - Υποστήριξη ελέγχου SQL scripts μέσω του πίνακα SQL_HISTORY_UPDATE_SCRIPTS και αποτροπή αν λείπει." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server & Client - Προσθήκη επιβεβαίωσης αναβάθμισης από Admin και ειδοποίηση Operators για κλείσιμο εφαρμογής." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Βελτιστοποίηση της κονσόλας διαχείρισης ώστε να είναι πλήρως responsive από κινητά τηλέφωνα." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Ασφαλής αντικατάσταση αρχείων με αυτόματη δημιουργία αντιγράφων ασφαλείας με επίθεμα _OLD." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.20"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.20",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Δοκιμαστική έκδοση για αυτόματη ενημέρωση συστήματος",
            BinaryFileUrl = "/packages/app_1.5.20.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = false,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server & Client - Δοκιμαστικός έλεγχος και επιβεβαίωση της λειτουργίας αυτόματης ενημέρωσης του Agent." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.21"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.21",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server - Διορθώσεις σφαλμάτων διαχείρισης εταιρείας και βελτιστοποιήσεις SQLite",
            BinaryFileUrl = "/packages/app_1.5.21.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = false,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Θωράκιση των POST handlers με try-catch blocks για την αποφυγή κρασαρισμάτων του Kestrel." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Βελτιστοποίηση SQLite σύνδεσης με busy timeout 5 δευτερολέπτων για αποφυγή σφαλμάτων locked." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.22"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.22",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Προκαθορισμένα στοιχεία εταιρείας, οπτική ένδειξη κωδικού & διόρθωση λίστας πελατών",
            BinaryFileUrl = "/packages/app_1.5.22.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = false,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server & Client - Αλλαγή προτεινόμενου EXE σε TIMOLOGISI.exe και προ-συμπλήρωση των default emails επικοινωνίας cleverdata." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Προσθήκη κουμπιού (ματάκι) για εμφάνιση/απόκρυψη του κωδικού χρήστη της βάσης." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Διασφάλιση σύνδεσης με την SQLite βάση central.db ανεξαρτήτως του Process Working Directory (Cwd) για αποτροπή εμφάνισης κενής λίστας." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.23"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.23",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Διορθώσεις αξιοπιστίας στην αυτόματη αναβάθμιση του Agent",
            BinaryFileUrl = "/packages/app_1.5.23.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Υλοποίηση βρόχου αναμονής στο batch script ώστε να περιμένει την απελευθέρωση του κλειδώματος αρχείου (file lock) του τρέχοντος exe πριν την αντιγραφή." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Προσθήκη αιτήματος admin elevation (UAC prompt via runas Verb) κατά την εκτέλεση της αναβάθμισης για αποφυγή σφαλμάτων Access Denied σε προστατευμένους φακέλους." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.24"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.24",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Client - Διόρθωση συγχρονισμού ρυθμίσεων SQL Server της εταιρείας",
            BinaryFileUrl = "/packages/app_1.5.24.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Διόρθωση σφάλματος κατά το οποίο οι παράμετροι σύνδεσης SQL Server της εταιρείας (ConnectionStringType, DbServer, DbName κλπ.) δεν αποθηκεύονταν τοπικά στον Agent κατά τον συγχρονισμό." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.25"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.25",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server - Διασφάλιση σύνδεσης με την SQLite βάση central.db ανεξαρτήτως του Process Working Directory (Cwd) για αποτροπή εμφάνισης κενής λίστας.",
            BinaryFileUrl = "/packages/app_1.5.25.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = false,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Διασφάλιση σύνδεσης με την SQLite βάση central.db ανεξαρτήτως του Process Working Directory (Cwd) για αποτροπή εμφάνισης κενής λίστας." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.26"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.26",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Διορθώσεις στη διαχείριση αναβαθμίσεων του Agent (progress bar) και της αυτόματης παρακολούθησης βάσεων.",
            BinaryFileUrl = "/packages/app_1.5.26.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server & Client - Διορθώσεις στη διαχείριση αναβαθμίσεων του Agent (progress bar) και της αυτόματης παρακολούθησης βάσεων." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Αυτόματη παρακολούθηση των νέων βάσεων δεδομένων κατά την προσθήκη τους (IsMonitored = true)." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Προσθήκη μπάρας προόδου (progress bar) κατά τη λήψη της νέας έκδοσης του Agent." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Διόρθωση του ατέρμονου βρόχου επανεκκίνησης (infinite restart loop) σε περίπτωση αποτυχίας λήψης του πακέτου αναβάθμισης." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Διόρθωση αρχικοποίησης φόρμας προσθήκης εταιρείας για πελάτες χωρίς υφιστάμενα προφίλ (resetProfileForm)." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.27"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.27",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Διορθώσεις στη διαχείριση αναβαθμίσεων του Agent (progress bar) και της αυτόματης παρακολούθησης βάσεων (IsMonitored=true).",
            BinaryFileUrl = "/packages/app_1.5.27.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server & Client - Διορθώσεις στη διαχείριση αναβαθμίσεων του Agent (progress bar) και της αυτόματης παρακολούθησης βάσεων (IsMonitored=true)." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Αυτόματη παρακολούθηση των νέων βάσεων δεδομένων κατά την προσθήκη τους (IsMonitored = true)." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Προσθήκη μπάρας προόδου (progress bar) κατά τη λήψη της νέας έκδοσης του Agent." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Διόρθωση του ατέρμονου βρόχου επανεκκίνησης (infinite restart loop) σε περίπτωση αποτυχίας λήψης του πακέτου αναβάθμισης." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;

        // One-time cleanup for v1.5.27: set IsMonitored = false for all databases NOT used by a profile.
        // This will clean up databases that previously defaulted to true.
        var tempDbs = context.ClientDatabases.ToList();
        var tempProfiles = context.ClientProfiles.ToList();
        foreach (var db in tempDbs)
        {
            bool isUsed = tempProfiles.Any(p => 
                string.Equals(p.DbName, db.DatabaseName, StringComparison.OrdinalIgnoreCase) && 
                p.ClientMachineId == db.ClientMachineId);
            if (!isUsed && db.IsMonitored)
            {
                db.IsMonitored = false;
            }
        }
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.28"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.28",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Προσθήκη ελέγχου ύπαρξης φακέλου, ανθεκτικότητα συντομεύσεων επιφάνειας εργασίας και διαχωρισμός αναβαθμίσεων Server/Client.",
            BinaryFileUrl = "/packages/app_1.5.28.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server & Client - Προσθήκη ελέγχου ύπαρξης φακέλου, ανθεκτικότητα συντομεύσεων επιφάνειας εργασίας και διαχωρισμός αναβαθμίσεων Server/Client." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Παράλειψη αναβάθμισης (SQL & αρχεία) εάν ο δηλωμένος φάκελος της εταιρείας δεν υπάρχει στον υπολογιστή." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Παράκαμψη SQL scripts για workstations (Client ρόλος) και εκτέλεσή τους μόνο από Server/Both." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Ανθεκτικότητα κατά τον εντοπισμό παραγωγικού exe όταν απουσιάζουν συντομεύσεις από την επιφάνεια εργασίας." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Προσθήκη Linear Gradient μπάρας προόδου topmost κατά την εγκατάσταση." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.29"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.29",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Διορθώσεις ορίων μεγέθους uploads στον Server και ενσωμάτωση ελέγχων φακέλων/συντομεύσεων στον Agent.",
            BinaryFileUrl = "/packages/app_1.5.29.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server & Client - Διορθώσεις ορίων μεγέθους uploads στον Server και ενσωμάτωση ελέγχων φακέλων/συντομεύσεων στον Agent." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Αύξηση ορίου μεγέθους multipart request body στα 500MB για την υποστήριξη μεγάλων αρχείων ZIP." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Παράλειψη αναβάθμισης (SQL & αρχεία) εάν ο δηλωμένος φάκελος της εταιρείας δεν υπάρχει στον υπολογιστή." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Παράκαμψη SQL scripts για workstations (Client ρόλος) και εκτέλεσή τους μόνο από Server/Both." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Ανθεκτικότητα κατά τον εντοπισμό παραγωγικού exe όταν απουσιάζουν συντομεύσεις από την επιφάνεια εργασίας." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Προσθήκη Linear Gradient μπάρας προόδου topmost κατά την εγκατάσταση." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.30"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.30",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Διορθώσεις Regex για την ανάλυση SQL scripts με το νέο μοτίβο '---NEW SCRIPT'.",
            BinaryFileUrl = "/packages/app_1.5.30.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server & Client - Διορθώσεις Regex για την ανάλυση SQL scripts με το νέο μοτίβο '---NEW SCRIPT'." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.31"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.31",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Βελτιώσεις στο φιλτράρισμα διαχωριστικών σχολίων (divider lines) και προσθήκη προεπισκόπησης του τελευταίου εκτελεσμένου block.",
            BinaryFileUrl = "/packages/app_1.5.31.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server & Client - Βελτιώσεις στο Regex ώστε να αγνοούνται διακοσμητικές γραμμές σχολίων." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Εμφάνιση του τελευταίου εκτελεσμένου block από τη βάση δεδομένων στην προεπισκόπηση αναβάθμισης." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.32"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.32",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Φιλτράρισμα κενών blocks και βελτίωση ανίχνευσης πραγματικών SQL σεναρίων.",
            BinaryFileUrl = "/packages/app_1.5.32.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server & Client - Φιλτράρισμα κενών blocks χωρίς πραγματικές SQL εντολές." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server & Client - Διόρθωση regex ώστε να μην ανιχνεύονται απλά σχόλια ως blocks." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.33"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.33",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Αποτροπή διπλής εγγραφής block, έλεγχος κωδικού admin και προτεραιότητα αναβάθμισης Agent.",
            BinaryFileUrl = "/packages/app_1.5.33.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Έλεγχος κωδικού έγκρισης βάσει των τοπικών χρηστών Admin/Owner." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Απενεργοποίηση updates εφαρμογής αν εκκρεμεί αναβάθμιση του Agent." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Αποτροπή διπλοεγγραφών στο ιστορικό εκτέλεσης σεναρίων." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.34"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.34",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Client - Διόρθωση σφάλματος κλειδώματος αρχείων και αυτόματης επανεκκίνησης κατά την αναβάθμιση του Agent.",
            BinaryFileUrl = "/packages/app_1.5.34.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Αυτόματος τερματισμός της υπηρεσίας TmsAgent κατά την αναβάθμιση από το GUI για αποφυγή locks." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Αυτόματη επανεκκίνηση της υπηρεσίας TmsAgent και του GUI μετά την ολοκλήρωση της αναβάθμισης." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Αποτροπή UAC hangs στην υπηρεσία TmsAgent στο Session 0." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.35"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.35",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Client - Προτεραιότητα αναβάθμισης του Agent έναντι των ενημερώσεων εφαρμογών.",
            BinaryFileUrl = "/packages/app_1.5.35.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Απόκρυψη/απενεργοποίηση διαθεσιμότητας ενημερώσεων εφαρμογών στο UI όσο εκκρεμεί αναβάθμιση του Agent." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Εμφάνιση κατάστασης 'Αναμονή αναβάθμισης Agent...' στις εταιρείες όταν εκκρεμεί η αναβάθμιση." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.36"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.36",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Διορθώσεις εγκατάστασης, σύνδεσης και συγχρονισμού χρηστών.",
            BinaryFileUrl = "/packages/app_1.5.36.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Διόρθωση σφάλματος NOT NULL constraint κατά την καταχώρηση νέων βάσεων δεδομένων." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Αντιγραφή όλων των υποστηρικτικών αρχείων (native DLLs) κατά την εγκατάσταση στο Program Files." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Αυτόματος συγχρονισμός και αποθήκευση χρηστών (users.json) κατά την ολοκλήρωση του Setup." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.37"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.37",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Client - Διόρθωση συγχρονισμού επιλογής εκκίνησης με τα Windows.",
            BinaryFileUrl = "/packages/app_1.5.37.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Άμεση αποστολή της επιλογής 'Εκκίνηση με τα Windows' (force sync) στη βάση δεδομένων κατά την εγκατάσταση." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.41"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.41",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Διορθώσεις αναβαθμίσεων, φιλτράρισμα βάσει ρόλου (Client) και αποτροπή υποβάθμισης έκδοσης στη βάση.",
            BinaryFileUrl = "/packages/app_1.5.41.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Δυναμικός έλεγχος πραγματικής έκδοσης βάσης (SQL_HISTORY_UPDATE_SCRIPTS) και αρχείου εκτέλεσης." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Ενημέρωση έκδοσης προγράμματος κατά την ολοκλήρωση της αναβάθμισης, ακόμη και σε database-only πακέτα." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Περιορισμός ειδοποιήσεων αναβάθμισης για σταθμούς εργασίας (Clients) μόνο όταν υπάρχουν νέα αρχεία προγράμματος." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Συσσώρευση όλων των ενδιάμεσων SQL scripts σε μια ενιαία, σειριακή λίστα εκτέλεσης." });
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Αποτροπή υποβάθμισης (downgrade) της έκδοσης της βάσης δεδομένων στα προφίλ του Server κατά τον έλεγχο από Clients με παλαιότερο configuration." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.42"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.42",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Client - Αντικατάσταση του MessageBox κατά το κλείσιμο με ειδοποίηση στο System Tray (Balloon Tip) για καλύτερη εμπειρία χρήστη.",
            BinaryFileUrl = "/packages/app_1.5.42.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Αντικατάσταση του MessageBox με Balloon Tip Pop-up κατά το κλείσιμο." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.43"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.43",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server - Διόρθωση σφάλματος NOT NULL constraint κατά την καταχώρηση νέων εταιρειών (profiles) από την κονσόλα.",
            BinaryFileUrl = "/packages/app_1.5.43.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server - Προσθήκη default τιμών στις ιδιότητες ProfileName, Afm, TargetFolder, ConnectionString, SerialNumber, LastUpdatedVersion και LastUpdateStatus του ClientProfile για την αποτροπή σφαλμάτων NOT NULL constraint στην SQLite." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.44"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.44",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Διασφάλιση σωστής μετονομασίας custom εκτελέσιμων (π.χ. TIMOLOGISI1.exe) κατά την εξαγωγή ZIP.",
            BinaryFileUrl = "/packages/app_1.5.44.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Βελτίωση της λογικής αναγνώρισης του κύριου εκτελέσιμου αρχείου (EXE) μέσα στο ZIP ώστε να υποστηρίζονται custom ονόματα (π.χ. TIMOLOGISI1.exe)." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.45"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.45",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Επίλυση του βρόχου (loop) συνεχών ειδοποιήσεων αναβάθμισης όταν η βάση έχει ήδη ενημερωθεί.",
            BinaryFileUrl = "/packages/app_1.5.45.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server & Client - Φιλτράρισμα των SQL scripts που έχουν ήδη εκτελεστεί και παράκαμψη της ειδοποίησης αν η έκδοση έχει ήδη εγκατασταθεί επιτυχώς." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.46"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.46",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Υποστήριξη τοπικής αποθήκευσης έκδοσης προγράμματος (tms_version.txt) για την αποφυγή εσφαλμένης ανίχνευσης από un-bumped EXE.",
            BinaryFileUrl = "/packages/app_1.5.46.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server & Client - Αποθήκευση και ανάγνωση της έκδοσης από το αρχείο tms_version.txt και αυτόματη ενημέρωσή του κατά τον συγχρονισμό ρυθμίσεων." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.47"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.47",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Διόρθωση συγχρονισμού έκδοσης προγράμματος (.exe) σε operators (Client) ώστε να διατηρούν την τοπική τους έκδοση και να ενημερώνονται σωστά.",
            BinaryFileUrl = "/packages/app_1.5.47.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server & Client - Παράκαμψη αντικατάστασης του CurrentProgramVersion και του αρχείου tms_version.txt στους operators (Client) κατά τον συγχρονισμό ρυθμίσεων." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.48"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.48",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Client - Υποστήριξη ελαχιστοποίησης (minimize) του παραθύρου προόδου αναβάθμισης και εμφάνιση των διαλόγων επιτυχίας/σφάλματος στο προσκήνιο (foreground).",
            BinaryFileUrl = "/packages/app_1.5.48.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Προσθήκη κουμπιού ελαχιστοποίησης, απενεργοποίηση topmost και ορισμός ιδιοκτήτη (owner) για τα MessageBox των αποτελεσμάτων." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.49"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.49",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Δυναμικός συγχρονισμός δικαιωμάτων χειριστών (operators) από τον διακομιστή και αφαίρεση των χρονικών ορίων (timeouts) κατά τη λήψη πακέτων αναβάθμισης.",
            BinaryFileUrl = "/packages/app_1.5.49.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server & Client - Συγχρονισμός δικαιωμάτων CanOperatorRunUpdates και CanOperatorViewLogs, αφαίρεση ορίων λήξης λήψης (timeouts) και αυτόματη έγκριση αναβαθμίσεων στους operators όταν η βάση είναι ενημερωμένη." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.50"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.50",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Client - Ενεργοποίηση του κουμπιού αναβάθμισης στους χειριστές (operators) για εγκεκριμένες ενημερώσεις και εμφάνιση του οδηγού προόδου.",
            BinaryFileUrl = "/packages/app_1.5.50.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Διασφάλιση ενεργοποίησης του κουμπιού 'Αναβάθμιση Τώρα' για τους operators και καθολική εμφάνιση του παραθύρου προόδου αναβάθμισης." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.51"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.51",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Client - Σύνδεση όλων των MessageBox.Show με το κεντρικό παράθυρο ως ιδιοκτήτη (Owner) για αποτροπή κλειδώματος της οθόνης.",
            BinaryFileUrl = "/packages/app_1.5.51.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Σύνδεση των MessageBox με το MainWindow για αποφυγή απόκρυψης διαλόγων πίσω από το κεντρικό παράθυρο." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.52"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.52",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Client - Διόρθωση επανάληψης ειδοποιήσεων ανακοινώσεων και προσθήκη δυνατότητας προσωρινής απόκρυψης.",
            BinaryFileUrl = "/packages/app_1.5.52.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Επίλυση του προβλήματος επαναλαμβανόμενης εμφάνισης των ανακοινώσεων και δυνατότητα απόκρυψής τους μέχρι την επανεκκίνηση του Agent." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.53"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.53",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Client - Αποτροπή εσφαλμένης ενημέρωσης της έκδοσης εφαρμογής κατά τον συγχρονισμό ρυθμίσεων (config commands) με τον Server.",
            BinaryFileUrl = "/packages/app_1.5.53.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Διορθώθηκε το σφάλμα όπου ο Agent θεωρούσε ότι η εφαρμογή είναι ενημερωμένη επειδή κατά τον συγχρονισμό ρυθμίσεων γραφόταν λανθασμένα η έκδοση του Server στο tms_version.txt." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.54"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.54",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Client - Υποστήριξη αυτόματης εκτέλεσης αναβαθμίσεων κατά την επανεκκίνηση (Windows boot/restart) με ειδοποιήσεις Balloon στο System Tray.",
            BinaryFileUrl = "/packages/app_1.5.54.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Κατά την εκκίνηση του υπολογιστή (με την παράμετρο --startup), ο Agent πλέον ελέγχει άμεσα και εκτελεί αυτόματα τις εγκεκριμένες αναβαθμίσεις του Agent, της εφαρμογής και της βάσης, εμφανίζοντας την εξέλιξη μέσω Balloon ειδοποιήσεων στο Tray." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.55"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.55",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Client - Βελτίωση αξιοπιστίας αυτόματης αναβάθμισης Agent με βίαιο τερματισμό (taskkill) κλειδωμένων διεργασιών.",
            BinaryFileUrl = "/packages/app_1.5.55.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Ενσωμάτωση εντολών taskkill στο σενάριο αναβάθμισης του Agent για την αποτροπή process locks σε DLLs ή στο Service που προκαλούσαν πάγωμα της εγκατάστασης." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.56"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.56",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Client - Αποθήκευση της απόρριψης ανακοινώσεων (dismissal) τοπικά, ώστε να μην εμφανίζονται ξανά μετά από επανεκκίνηση.",
            BinaryFileUrl = "/packages/app_1.5.56.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Πλέον όταν ο χρήστης κλείνει ή βλέπει μια ειδοποίηση ανακοίνωσης (Broadcast Notification), αυτή αποθηκεύεται μόνιμα στο τοπικό αρχείο seen_broadcasts.json ώστε να μην εμφανίζεται ξανά μετά από επανεκκίνηση του υπολογιστή." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.57"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.57",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Client - Παράκαμψη τοπικού ελέγχου έκδοσης βάσης δεδομένων για Client Agents προκειμένου να αποφευχθούν timeouts, και συγχρονισμός της έκδοσης βάσης από τον Server.",
            BinaryFileUrl = "/packages/app_1.5.57.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Οι Client-role Agents πλέον παρακάμπτουν πλήρως τον τοπικό έλεγχο έκδοσης βάσης (ώστε να αποφεύγονται καθυστερήσεις/hangs όταν η SQL Server θύρα είναι κλειστή) και λαμβάνουν την έκδοση βάσης απευθείας από τον Server." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.58"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.58",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Client - Διόρθωση συντακτικού σφάλματος στη δημιουργία του batch σεναρίου αναβάθμισης του Agent όταν η υπηρεσία Service εκτελείται.",
            BinaryFileUrl = "/packages/app_1.5.58.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Διορθώθηκε κρίσιμο συντακτικό σφάλμα της cmd.exe (nested label/goto loop) κατά τον αυτόματο τερματισμό και επανεκκίνηση της υπηρεσίας TmsAgent στο batch script της αναβάθμισης." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.59"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.59",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Server & Client - Οργάνωση μηχανημάτων ανά πελάτη (Customer Grouping), διαχείριση API Keys/Aliases, collapsible Tree-View Dashboard και επεξεργασία στοιχείων πελάτη.",
            BinaryFileUrl = "/packages/app_1.5.59.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Server & Client - Προσθήκη οργάνωσης μηχανημάτων ανά Πελάτη, υποστήριξη Alias θέσης εργασίας, collapsible Tree-View στην κονσόλα με Expand/Collapse All, και δυνατότητα επεξεργασίας στοιχείων πελάτη." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.60"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.60",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Client - Βελτίωση της αξιοπιστίας και αποφυγή deadlocks κατά την αυτόματη αναβάθμιση του Agent.",
            BinaryFileUrl = "/packages/app_1.5.60.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Προσθήκη ορίου αναμονής (timeout) για τον τερματισμό της υπηρεσίας, αυτόματο Force Kill αν κολλήσει σε STOP_PENDING και μέγιστο όριο 10 επαναλήψεων αντιγραφής για αποφυγή ατέρμονων βρόχων." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.61"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.61",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Client - Παράκαμψη ελέγχου monitored databases για Client-role τερματικά, ώστε να μην εμφανίζονται ως 'Δεν παρακολουθείται'.",
            BinaryFileUrl = "/packages/app_1.5.61.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Διόρθωση ελέγχου monitored databases για σταθμούς εργασίας (Client role), επιτρέποντας την κανονική λήψη αναβαθμίσεων της εφαρμογής." });

        context.Versions.Add(systemReleaseVersion);
        hasChanges = true;
    }

    if (!context.Versions.Any(v => v.VersionNumber == "1.5.62"))
    {
        // Deactivate other system versions
        var oldSystemVersions = context.Versions.Where(v => v.TargetType == "System").ToList();
        foreach (var oldV in oldSystemVersions)
        {
            oldV.IsCurrent = false;
        }

        var systemReleaseVersion = new VersionInfo
        {
            VersionNumber = "1.5.62",
            ReleaseDate = DateTime.UtcNow,
            Description = "Αφορά: Client - Προσθήκη πεδίου αναζήτησης/φιλτραρίσματος στο Dashboard του Agent Panel.",
            BinaryFileUrl = "/packages/app_1.5.62.zip",
            SecurityCode = "clever2026",
            IsActive = true,
            IsCurrent = true,
            TargetType = "System"
        };
        systemReleaseVersion.ReleaseNotes.Add(new ReleaseNote { NotesContent = "Αφορά: Client - Προσθήκη δυνατότητας αναζήτησης εταιρείας, ΑΦΜ, βάσης δεδομένων και κατάστασης στο Dashboard του Agent." });

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

    // Force monitoring ONLY for databases that are currently associated with a declared profile.
    // The rest (other databases) are optional and their monitored status is controlled by the user via the console.
    var allDbs = context.ClientDatabases.ToList();
    var allProfiles = context.ClientProfiles.ToList();
    foreach (var db in allDbs)
    {
        bool isUsedByProfile = allProfiles.Any(p => 
            string.Equals(p.DbName, db.DatabaseName, StringComparison.OrdinalIgnoreCase) && 
            p.ClientMachineId == db.ClientMachineId);

        if (isUsedByProfile && !db.IsMonitored)
        {
            db.IsMonitored = true;
            hasChanges = true;
        }
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

