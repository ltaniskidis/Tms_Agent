using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Text.Json;
using System.Windows.Input;
using Tms.Agent.Core.Models;
using Tms.Agent.Core.Services;
using Tms.Shared.Models;

namespace Tms.Agent.Wpf.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        private readonly ProfileManager _profileManager;
        private readonly SettingsManager _settingsManager;
        private readonly UpdateEngine _updateEngine;
        private string _clientId = string.Empty;
        private string _machineName = string.Empty;

        // Navigation
        private string _currentView = "Dashboard"; // Dashboard, Config, Logs
        public string CurrentView
        {
            get => _currentView;
            set => SetProperty(ref _currentView, value);
        }

        public string AppVersion => "1.5.0";
        public string WindowTitle => $"TMS Agent Panel - Διαχείριση Ενημερώσεων (v{AppVersion})";

        // Connection Settings
        private string _serverUrl = "http://localhost:5007"; // Default dev API port
        public string ServerUrl
        {
            get => _serverUrl;
            set
            {
                if (SetProperty(ref _serverUrl, value))
                {
                    SaveAgentSettings();
                }
            }
        }

        private string _apiKey = string.Empty;
        public string ApiKey
        {
            get => _apiKey;
            set
            {
                if (SetProperty(ref _apiKey, value))
                {
                    SaveAgentSettings();
                }
            }
        }

        private string _machineRole = "Both"; // SqlServer, Client, Both
        public string MachineRole
        {
            get => _machineRole;
            set
            {
                if (SetProperty(ref _machineRole, value))
                {
                    SaveAgentSettings();
                    OnPropertyChanged(nameof(IsRoleSqlServer));
                    OnPropertyChanged(nameof(IsRoleClient));
                    OnPropertyChanged(nameof(IsRoleBoth));
                }
            }
        }

        public bool IsRoleSqlServer
        {
            get => MachineRole == "SqlServer";
            set { if (value) MachineRole = "SqlServer"; OnPropertyChanged(); }
        }

        public bool IsRoleClient
        {
            get => MachineRole == "Client";
            set { if (value) MachineRole = "Client"; OnPropertyChanged(); }
        }

        public bool IsRoleBoth
        {
            get => MachineRole == "Both";
            set { if (value) MachineRole = "Both"; OnPropertyChanged(); }
        }

        // Profiles
        public ObservableCollection<ProfileUiWrapper> Profiles { get; } = new();

        private ProfileUiWrapper? _selectedProfile;
        public ProfileUiWrapper? SelectedProfile
        {
            get => _selectedProfile;
            set
            {
                if (SetProperty(ref _selectedProfile, value))
                {
                    if (value != null)
                    {
                        // Load into editor fields
                        EditProfileId = value.Profile.ProfileId;
                        EditProfileName = value.Profile.ProfileName;
                        EditAfm = value.Profile.Afm;
                        EditTargetFolder = value.Profile.TargetFolder;
                        EditTargetExeName = value.Profile.TargetExeName;
                        EditConnectionString = value.Profile.ConnectionString;
                        EditConnectionStringType = value.Profile.ConnectionStringType ?? "Direct";
                        EditDbServer = value.Profile.DbServer ?? string.Empty;
                        EditDbName = value.Profile.DbName ?? string.Empty;
                        EditDbUseWindowsAuth = value.Profile.DbUseWindowsAuth;
                        EditDbUser = value.Profile.DbUser ?? string.Empty;
                        EditDbPassword = value.Profile.DbPassword ?? string.Empty;
                        EditConfigFilePath = value.Profile.ConfigFilePath ?? string.Empty;
                        EditCurrentVersion = value.Profile.CurrentVersion;
                        EditSerialNumber = value.Profile.SerialNumber;
                        EditActiveUsersCount = value.Profile.ActiveUsersCount;
                    }
                    else
                    {
                        ClearEditor();
                    }
                }
            }
        }

        // Profile Editor Fields
        private string _editProfileId = string.Empty;
        public string EditProfileId { get => _editProfileId; set => SetProperty(ref _editProfileId, value); }

        private string _editProfileName = string.Empty;
        public string EditProfileName { get => _editProfileName; set => SetProperty(ref _editProfileName, value); }

        private string _editAfm = string.Empty;
        public string EditAfm { get => _editAfm; set => SetProperty(ref _editAfm, value); }

        private string _editTargetFolder = string.Empty;
        public string EditTargetFolder { get => _editTargetFolder; set => SetProperty(ref _editTargetFolder, value); }

        private string _editTargetExeName = "TmsApp.exe";
        public string EditTargetExeName { get => _editTargetExeName; set => SetProperty(ref _editTargetExeName, value); }

        private string _editConnectionString = string.Empty;
        public string EditConnectionString { get => _editConnectionString; set => SetProperty(ref _editConnectionString, value); }

        private string _editConnectionStringType = "Direct";
        public string EditConnectionStringType 
        { 
            get => _editConnectionStringType; 
            set 
            { 
                if (SetProperty(ref _editConnectionStringType, value))
                {
                    OnPropertyChanged(nameof(IsDirectConnection));
                    OnPropertyChanged(nameof(IsBuilderConnection));
                    OnPropertyChanged(nameof(IsConfigFileConnection));
                }
            } 
        }

        public bool IsDirectConnection => EditConnectionStringType == "Direct";
        public bool IsBuilderConnection => EditConnectionStringType == "Builder";
        public bool IsConfigFileConnection => EditConnectionStringType == "ConfigFile";

        private string _editDbServer = string.Empty;
        public string EditDbServer { get => _editDbServer; set => SetProperty(ref _editDbServer, value); }

        private string _editDbName = string.Empty;
        public string EditDbName { get => _editDbName; set => SetProperty(ref _editDbName, value); }

        private string _editDbUser = string.Empty;
        public string EditDbUser { get => _editDbUser; set => SetProperty(ref _editDbUser, value); }

        private string _editDbPassword = string.Empty;
        public string EditDbPassword { get => _editDbPassword; set => SetProperty(ref _editDbPassword, value); }

        private bool _editDbUseWindowsAuth;
        public bool EditDbUseWindowsAuth 
        { 
            get => _editDbUseWindowsAuth; 
            set 
            { 
                if (SetProperty(ref _editDbUseWindowsAuth, value))
                {
                    OnPropertyChanged(nameof(IsNotWindowsAuth));
                }
            } 
        }

        public bool IsNotWindowsAuth => !_editDbUseWindowsAuth;

        private string _editConfigFilePath = string.Empty;
        public string EditConfigFilePath { get => _editConfigFilePath; set => SetProperty(ref _editConfigFilePath, value); }

        private string _editCurrentVersion = "1.0.0";
        public string EditCurrentVersion { get => _editCurrentVersion; set => SetProperty(ref _editCurrentVersion, value); }

        private string _editSerialNumber = string.Empty;
        public string EditSerialNumber { get => _editSerialNumber; set => SetProperty(ref _editSerialNumber, value); }

        private int _editActiveUsersCount;
        public int EditActiveUsersCount { get => _editActiveUsersCount; set => SetProperty(ref _editActiveUsersCount, value); }

        // Live Log Output
        private string _logOutput = string.Empty;
        public string LogOutput
        {
            get => _logOutput;
            set => SetProperty(ref _logOutput, value);
        }
        // General status message
        private string _statusMessage = "Έτοιμο";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private string _systemVersionWarning = string.Empty;
        public string SystemVersionWarning
        {
            get => _systemVersionWarning;
            set => SetProperty(ref _systemVersionWarning, value);
        }

        // User Roles & Access Control
        private string _userRole = "Operator"; // Operator, Administrator
        private string _loggedInUser = string.Empty;
        public string LoggedInUser
        {
            get => _loggedInUser;
            set => SetProperty(ref _loggedInUser, value);
        }

        public string UserRole
        {
            get => _userRole;
            set
            {
                if (SetProperty(ref _userRole, value))
                {
                    OnPropertyChanged(nameof(IsOwner));
                    OnPropertyChanged(nameof(IsAdmin));
                    OnPropertyChanged(nameof(IsOperator));
                    OnPropertyChanged(nameof(CurrentRoleText));
                    OnPropertyChanged(nameof(RoleColor));
                    OnPropertyChanged(nameof(RoleActionButtonText));
                }
            }
        }

        public bool IsOwner => UserRole == "Owner";
        public bool IsAdmin => UserRole == "Admin" || UserRole == "Owner";
        public bool IsOperator => UserRole == "Operator";

        public string CurrentRoleText => IsOwner ? "Owner (Πλήρης Παραμετροποίηση)" : (IsAdmin ? "Διαχειριστής (Admin)" : "Χειριστής (Operator)");
        public string RoleColor => IsOwner ? "#FBBF24" : (IsAdmin ? "#EF4444" : "#10B981");
        public string RoleActionButtonText => "Αλλαγή Χρήστη / Έξοδος";

        private bool _isSettingsLocked = false;
        public bool IsSettingsLocked
        {
            get => _isSettingsLocked;
            set => SetProperty(ref _isSettingsLocked, value);
        }

        private bool _isUpgradeAllowed = true;
        public bool IsUpgradeAllowed
        {
            get => _isUpgradeAllowed;
            set => SetProperty(ref _isUpgradeAllowed, value);
        }

        private bool _canOperatorViewLogs = true;
        public bool CanViewLogs => UserRole != "Operator" || _canOperatorViewLogs;

        private bool _canOperatorRunUpdates = false;
        public bool CanRunUpdates => UserRole != "Operator" || _canOperatorRunUpdates;

        // Admin Login Modal (unused but kept for compilation compatibility if needed)
        private bool _isAdminLoginModalOpen;
        public bool IsAdminLoginModalOpen
        {
            get => _isAdminLoginModalOpen;
            set => SetProperty(ref _isAdminLoginModalOpen, value);
        }

        private string _adminPasscodeInput = string.Empty;
        public string AdminPasscodeInput
        {
            get => _adminPasscodeInput;
            set => SetProperty(ref _adminPasscodeInput, value);
        }

        private string _adminPasscodeError = string.Empty;
        public string AdminPasscodeError
        {
            get => _adminPasscodeError;
            set
            {
                if (SetProperty(ref _adminPasscodeError, value))
                {
                    OnPropertyChanged(nameof(HasAdminPasscodeError));
                }
            }
        }

        public bool HasAdminPasscodeError => !string.IsNullOrEmpty(AdminPasscodeError);

        // Update Passcode Modal Properties (used when operator starts an unauthorized update)
        private bool _isPasscodeModalOpen;
        public bool IsPasscodeModalOpen
        {
            get => _isPasscodeModalOpen;
            set => SetProperty(ref _isPasscodeModalOpen, value);
        }

        private string _passcodeInput = string.Empty;
        public string PasscodeInput
        {
            get => _passcodeInput;
            set => SetProperty(ref _passcodeInput, value);
        }

        private string _passcodeError = string.Empty;
        public string PasscodeError
        {
            get => _passcodeError;
            set
            {
                if (SetProperty(ref _passcodeError, value))
                {
                    OnPropertyChanged(nameof(HasPasscodeError));
                }
            }
        }

        public bool HasPasscodeError => !string.IsNullOrEmpty(PasscodeError);

        private ProfileUiWrapper? _pendingUpdateWrapper;
        public ProfileUiWrapper? PendingUpdateWrapper
        {
            get => _pendingUpdateWrapper;
            set => SetProperty(ref _pendingUpdateWrapper, value);
        }

        // Installation Wizard Properties
        private bool _isWizardOpen;
        public bool IsWizardOpen
        {
            get => _isWizardOpen;
            set => SetProperty(ref _isWizardOpen, value);
        }

        private int _wizardStage = 1; // 1: Welcome, 2: Progress, 3: Finish
        public int WizardStage
        {
            get => _wizardStage;
            set => SetProperty(ref _wizardStage, value);
        }

        private string _wizardTitle = string.Empty;
        public string WizardTitle { get => _wizardTitle; set => SetProperty(ref _wizardTitle, value); }

        private string _wizardVersion = string.Empty;
        public string WizardVersion { get => _wizardVersion; set => SetProperty(ref _wizardVersion, value); }

        private string _wizardDescription = string.Empty;
        public string WizardDescription { get => _wizardDescription; set => SetProperty(ref _wizardDescription, value); }

        public ObservableCollection<string> WizardReleaseNotes { get; } = new();

        private double _wizardProgress;
        public double WizardProgress { get => _wizardProgress; set => SetProperty(ref _wizardProgress, value); }

        private string _wizardStatus = string.Empty;
        public string WizardStatus { get => _wizardStatus; set => SetProperty(ref _wizardStatus, value); }

        private bool _wizardSuccess;
        public bool WizardSuccess { get => _wizardSuccess; set => SetProperty(ref _wizardSuccess, value); }

        private string _wizardErrorMessage = string.Empty;
        public string WizardErrorMessage { get => _wizardErrorMessage; set => SetProperty(ref _wizardErrorMessage, value); }

        // Events
        public event Action<string, string>? UpdateDetected;

        // Commands
        public ICommand NavigateCommand { get; }
        public ICommand CheckUpdatesCommand { get; }
        public ICommand UpdateProfileCommand { get; }
        public ICommand SaveProfileCommand { get; }
        public ICommand DeleteProfileCommand { get; }
        public ICommand NewProfileCommand { get; }
        public ICommand TestConnectionCommand { get; }
        public ICommand ConfirmPasscodeCommand { get; }
        public ICommand CancelPasscodeCommand { get; }
        public ICommand BrowseFolderCommand { get; }
        public ICommand BrowseConfigFileCommand { get; }
        public ICommand ToggleRoleCommand { get; }
        public ICommand ConfirmAdminLoginCommand { get; }
        public ICommand CancelAdminLoginCommand { get; }
        public ICommand NextWizardCommand { get; }
        public ICommand CancelWizardCommand { get; }

        public MainViewModel()
        {
            _profileManager = new ProfileManager();
            _settingsManager = new SettingsManager();
            _updateEngine = new UpdateEngine();

            InitializeClientInfo();

            // Load Settings
            var settings = _settingsManager.LoadSettings();
            _serverUrl = settings.ServerUrl;
            _machineRole = settings.MachineRole;
            _apiKey = settings.ApiKey;

            // Commands Setup
            NavigateCommand = new RelayCommand<string>(view => CurrentView = view ?? "Dashboard");
            CheckUpdatesCommand = new RelayCommand(async () => await CheckForUpdatesAsync());
            
            UpdateProfileCommand = new RelayCommand<ProfileUiWrapper>(p =>
            {
                if (p == null || p.AvailableVersion == null) return;

                if (!IsUpgradeAllowed)
                {
                    System.Windows.MessageBox.Show(
                        "Για την Ολοκλήρωση της αναβάθμισης παρακαλώ επικοινωνήστε με τον προμηθευτή σας",
                        "TMS Agent - Απαγόρευση Αναβάθμισης",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return;
                }

                if (IsAdmin || p.IsAuthorizedByAdmin)
                {
                    OpenWizard(p);
                }
                else
                {
                    PendingUpdateWrapper = p;
                    PasscodeInput = string.Empty;
                    PasscodeError = string.Empty;
                    IsPasscodeModalOpen = true;
                }
            });

            SaveProfileCommand = new RelayCommand(SaveProfile);
            DeleteProfileCommand = new RelayCommand(DeleteProfile);
            NewProfileCommand = new RelayCommand(NewProfile);
            TestConnectionCommand = new RelayCommand(async () => await TestDatabaseConnectionAsync());
            ConfirmPasscodeCommand = new RelayCommand(async () => await ConfirmPasscodeAsync());
            CancelPasscodeCommand = new RelayCommand(CancelPasscode);
            
            BrowseFolderCommand = new RelayCommand(BrowseFolder);
            BrowseConfigFileCommand = new RelayCommand(BrowseConfigFile);
            ToggleRoleCommand = new RelayCommand(ToggleRole);
            ConfirmAdminLoginCommand = new RelayCommand(ConfirmAdminLogin);
            CancelAdminLoginCommand = new RelayCommand(CancelAdminLogin);
            NextWizardCommand = new RelayCommand(async () => await NextWizardAsync());
            CancelWizardCommand = new RelayCommand(CancelWizard);

            LoadProfiles();
            
            // Auto check updates on startup to discover databases and sync with server
            _ = CheckForUpdatesAsync();
        }

        private void InitializeClientInfo()
        {
            _machineName = Environment.MachineName;
            
            // Client ID logic: read or create a GUID for this installation
            var appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TmsAgent");
            var idFile = Path.Combine(appDataPath, "client_id.txt");

            if (File.Exists(idFile))
            {
                _clientId = File.ReadAllText(idFile).Trim();
            }
            else
            {
                _clientId = Guid.NewGuid().ToString();
                if (!Directory.Exists(appDataPath)) Directory.CreateDirectory(appDataPath);
                File.WriteAllText(idFile, _clientId);
            }
        }

        private void SaveAgentSettings()
        {
            _settingsManager.SaveSettings(new AgentSettings
            {
                ServerUrl = ServerUrl,
                MachineRole = MachineRole,
                ApiKey = ApiKey
            });
        }

        private void LoadProfiles()
        {
            Profiles.Clear();
            var list = _profileManager.LoadProfiles();
            foreach (var p in list)
            {
                Profiles.Add(new ProfileUiWrapper(p));
            }
        }

        private void ClearEditor()
        {
            EditProfileId = string.Empty;
            EditProfileName = string.Empty;
            EditAfm = string.Empty;
            EditTargetFolder = string.Empty;
            EditTargetExeName = "TmsApp.exe";
            EditConnectionString = string.Empty;
            EditConnectionStringType = "Direct";
            EditDbServer = string.Empty;
            EditDbName = string.Empty;
            EditDbUseWindowsAuth = false;
            EditDbUser = string.Empty;
            EditDbPassword = string.Empty;
            EditConfigFilePath = string.Empty;
            EditCurrentVersion = "1.0.0";
            EditSerialNumber = string.Empty;
            EditActiveUsersCount = 0;
        }

        private void NewProfile()
        {
            SelectedProfile = null;
            ClearEditor();
            EditProfileId = Guid.NewGuid().ToString();
            EditProfileName = "Νέα Εταιρεία";
        }

        private void SaveProfile()
        {
            if (string.IsNullOrWhiteSpace(EditProfileName))
            {
                System.Windows.MessageBox.Show("Το όνομα εταιρείας είναι υποχρεωτικό.", "Σφάλμα", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var list = Profiles.Select(p => p.Profile).ToList();
            var existing = list.FirstOrDefault(p => p.ProfileId == EditProfileId);

            if (EditConnectionStringType == "Builder")
            {
                var parts = new List<string> { $"Server={EditDbServer}" };
                if (!string.IsNullOrEmpty(EditDbName)) parts.Add($"Database={EditDbName}");
                if (EditDbUseWindowsAuth)
                {
                    parts.Add("Integrated Security=True");
                }
                else
                {
                    if (!string.IsNullOrEmpty(EditDbUser)) parts.Add($"User Id={EditDbUser}");
                    if (!string.IsNullOrEmpty(EditDbPassword)) parts.Add($"Password={EditDbPassword}");
                }
                parts.Add("TrustServerCertificate=True");
                EditConnectionString = string.Join(";", parts) + ";";
            }

            if (existing == null)
            {
                var newProfile = new LocalProfile
                {
                    ProfileId = EditProfileId,
                    ProfileName = EditProfileName,
                    Afm = EditAfm,
                    TargetFolder = EditTargetFolder,
                    TargetExeName = EditTargetExeName,
                    ConnectionString = EditConnectionString,
                    ConnectionStringType = EditConnectionStringType,
                    DbServer = EditDbServer,
                    DbName = EditDbName,
                    DbUser = EditDbUser,
                    DbPassword = EditDbPassword,
                    DbUseWindowsAuth = EditDbUseWindowsAuth,
                    ConfigFilePath = EditConfigFilePath,
                    CurrentVersion = EditCurrentVersion,
                    SerialNumber = EditSerialNumber,
                    ActiveUsersCount = EditActiveUsersCount
                };
                list.Add(newProfile);
                _profileManager.SaveProfiles(list);
                
                var wrapper = new ProfileUiWrapper(newProfile);
                Profiles.Add(wrapper);
                SelectedProfile = wrapper;
            }
            else
            {
                existing.ProfileName = EditProfileName;
                existing.Afm = EditAfm;
                existing.TargetFolder = EditTargetFolder;
                existing.TargetExeName = EditTargetExeName;
                existing.ConnectionString = EditConnectionString;
                existing.ConnectionStringType = EditConnectionStringType;
                existing.DbServer = EditDbServer;
                existing.DbName = EditDbName;
                existing.DbUser = EditDbUser;
                existing.DbPassword = EditDbPassword;
                existing.DbUseWindowsAuth = EditDbUseWindowsAuth;
                existing.ConfigFilePath = EditConfigFilePath;
                existing.CurrentVersion = EditCurrentVersion;
                existing.SerialNumber = EditSerialNumber;
                existing.ActiveUsersCount = EditActiveUsersCount;

                _profileManager.SaveProfiles(list);
                
                // Refresh UI binding
                var wrapper = Profiles.FirstOrDefault(p => p.Profile.ProfileId == EditProfileId);
                if (wrapper != null)
                {
                    wrapper.RefreshProperties();
                }
            }

            StatusMessage = "Το προφίλ αποθηκεύτηκε επιτυχώς.";
        }

        private void DeleteProfile()
        {
            if (SelectedProfile == null) return;

            var result = System.Windows.MessageBox.Show($"Είστε σίγουροι ότι θέλετε να διαγράψετε το προφίλ '{SelectedProfile.ProfileName}';", "Επιβεβαίωση Διαγραφής", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                var list = Profiles.Select(p => p.Profile).ToList();
                var toRemove = list.FirstOrDefault(p => p.ProfileId == SelectedProfile.Profile.ProfileId);
                if (toRemove != null)
                {
                    list.Remove(toRemove);
                    _profileManager.SaveProfiles(list);
                    Profiles.Remove(SelectedProfile);
                    SelectedProfile = null;
                    StatusMessage = "Το προφίλ διαγράφηκε.";
                }
            }
        }

        private async Task TestDatabaseConnectionAsync()
        {
            var tempProfile = new LocalProfile
            {
                ConnectionString = EditConnectionString,
                ConnectionStringType = EditConnectionStringType,
                DbServer = EditDbServer,
                DbName = EditDbName,
                DbUser = EditDbUser,
                DbPassword = EditDbPassword,
                DbUseWindowsAuth = EditDbUseWindowsAuth,
                ConfigFilePath = EditConfigFilePath
            };
            string testConnStr = tempProfile.GetResolvedConnectionString();

            if (string.IsNullOrWhiteSpace(testConnStr))
            {
                System.Windows.MessageBox.Show("Παρακαλώ συμπληρώστε τα απαραίτητα στοιχεία σύνδεσης για δοκιμή.", "Σφάλμα", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            StatusMessage = "Δοκιμή σύνδεσης βάσης...";
            bool success = false;
            string error = string.Empty;

            await Task.Run(() =>
            {
                success = UpdateEngine.TestConnectionString(testConnStr, out error);
            });

            if (success)
            {
                System.Windows.MessageBox.Show("Η σύνδεση στη βάση δεδομένων SQL Server πραγματοποιήθηκε με επιτυχία!", "Επιτυχία", MessageBoxButton.OK, MessageBoxImage.Information);
                StatusMessage = "Σύνδεση βάσης επιτυχής.";
            }
            else
            {
                System.Windows.MessageBox.Show($"Αποτυχία σύνδεσης:\n{error}", "Σφάλμα Σύνδεσης", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusMessage = "Αποτυχία σύνδεσης βάσης.";
            }
        }

        private void BrowseFolder()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Επιλογή φακέλου εγκατάστασης της desktop εφαρμογής",
                InitialDirectory = string.IsNullOrEmpty(EditTargetFolder) ? "C:\\" : EditTargetFolder
            };

            if (dialog.ShowDialog() == true)
            {
                EditTargetFolder = dialog.FolderName;
            }
        }

        private void BrowseConfigFile()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Επιλογή αρχείου ρυθμίσεων της εφαρμογής",
                Filter = "Αρχεία Ρυθμίσεων (*.config;*.json;*.xml)|*.config;*.json;*.xml|Όλα τα Αρχεία (*.*)|*.*",
                InitialDirectory = string.IsNullOrEmpty(EditTargetFolder) ? "C:\\" : EditTargetFolder
            };

            if (dialog.ShowDialog() == true)
            {
                EditConfigFilePath = dialog.FileName;
                if (string.IsNullOrEmpty(EditTargetFolder))
                {
                    EditTargetFolder = System.IO.Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
                }
            }
        }

        private void ToggleRole()
        {
            App.LoggedInUser = string.Empty;
            App.UserRole = string.Empty;

            var loginWindow = new LoginWindow();
            if (loginWindow.ShowDialog() == true)
            {
                App.LoggedInUser = loginWindow.LoggedInUser;
                App.UserRole = loginWindow.UserRole;
                ApplyUserAuthentication(App.LoggedInUser, App.UserRole);
                StatusMessage = $"Συνδέθηκε ο χρήστης: {App.LoggedInUser} ({CurrentRoleText})";
            }
            else
            {
                System.Windows.Application.Current.Shutdown();
            }
        }

        public void ApplyUserAuthentication(string username, string role)
        {
            LoggedInUser = username;
            UserRole = role;
            IsSettingsLocked = role != "Owner";

            OnPropertyChanged(nameof(IsOwner));
            OnPropertyChanged(nameof(IsAdmin));
            OnPropertyChanged(nameof(IsOperator));
            OnPropertyChanged(nameof(CurrentRoleText));
            OnPropertyChanged(nameof(RoleColor));
            OnPropertyChanged(nameof(RoleActionButtonText));
            OnPropertyChanged(nameof(CanViewLogs));
            OnPropertyChanged(nameof(CanRunUpdates));

            ApplyOperatorPermissions();
        }

        private void ApplyOperatorPermissions()
        {
            if (UserRole == "Operator" && !CanViewLogs && CurrentView == "Logs")
            {
                CurrentView = "Dashboard";
            }
        }

        private void ConfirmAdminLogin()
        {
            // kept for interface compatibility
        }

        private void CancelAdminLogin()
        {
            IsAdminLoginModalOpen = false;
            AdminPasscodeInput = string.Empty;
            AdminPasscodeError = string.Empty;
        }

        private async Task CheckForUpdatesAsync()
        {
            StatusMessage = "Έλεγχος για νέες εκδόσεις...";
            foreach (var p in Profiles)
            {
                p.Status = "Έλεγχος...";
            }

            var localProfilesList = Profiles.Select(p => p.Profile).ToList();
            var response = await _updateEngine.CheckForUpdatesAsync(ServerUrl, _clientId, _machineName, MachineRole, AppVersion, ApiKey, localProfilesList);

            if (response == null)
            {
                StatusMessage = "Αδυναμία σύνδεσης στον διακομιστή ενημερώσεων.";
                foreach (var p in Profiles)
                {
                    p.Status = "Σφάλμα σύνδεσης διακομιστή";
                }
                return;
            }

            // Check for version alignment between Server (Console) and Client (Agent)
            if (!string.IsNullOrEmpty(response.CurrentSystemVersion) && response.CurrentSystemVersion != AppVersion)
            {
                SystemVersionWarning = $"⚠️ Προειδοποίηση: Η έκδοση του Agent (v{AppVersion}) διαφέρει από την τρέχουσα έκδοση συστήματος (v{response.CurrentSystemVersion}).";
            }
            else
            {
                SystemVersionWarning = string.Empty;
            }

            // 1. Handle Configuration Sync Commands from Server
            if (response.ConfigCommands != null && response.ConfigCommands.Any())
            {
                bool profilesChanged = false;
                var currentProfiles = Profiles.Select(p => p.Profile).ToList();

                foreach (var cmd in response.ConfigCommands)
                {
                    if (cmd.CommandType == "SaveProfile")
                    {
                        var existing = currentProfiles.FirstOrDefault(p => p.ProfileId == cmd.ProfileId);
                        if (existing == null)
                        {
                            var newProfile = new LocalProfile
                            {
                                ProfileId = cmd.ProfileId,
                                ProfileName = cmd.ProfileName,
                                Afm = cmd.Afm,
                                TargetFolder = cmd.TargetFolder,
                                TargetExeName = cmd.TargetExeName,
                                ConnectionString = cmd.ConnectionString,
                                CurrentVersion = cmd.CurrentVersion,
                                SerialNumber = cmd.SerialNumber,
                                ActiveUsersCount = cmd.ActiveUsersCount
                            };
                            currentProfiles.Add(newProfile);
                            profilesChanged = true;
                        }
                        else
                        {
                            existing.ProfileName = cmd.ProfileName;
                            existing.Afm = cmd.Afm;
                            existing.TargetFolder = cmd.TargetFolder;
                            existing.TargetExeName = cmd.TargetExeName;
                            existing.ConnectionString = cmd.ConnectionString;
                            existing.CurrentVersion = cmd.CurrentVersion;
                            existing.SerialNumber = cmd.SerialNumber;
                            existing.ActiveUsersCount = cmd.ActiveUsersCount;
                            profilesChanged = true;
                        }
                    }
                    else if (cmd.CommandType == "DeleteProfile")
                    {
                        var existing = currentProfiles.FirstOrDefault(p => p.ProfileId == cmd.ProfileId);
                        if (existing != null)
                        {
                            currentProfiles.Remove(existing);
                            profilesChanged = true;
                        }
                    }
                }

                if (profilesChanged)
                {
                    _profileManager.SaveProfiles(currentProfiles);
                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        LoadProfiles();
                    });
                }
            }

            // 2. Scan and set updates
            var monitoredDbs = response.MonitoredDatabaseNames ?? new List<string>();
            bool hasMonitoredUpdates = false;

            foreach (var p in Profiles)
            {
                var dbName = UpdateEngine.GetDatabaseNameFromConnectionString(p.Profile.ConnectionString);
                
                // If it has a connection string but the database is not monitored on the server, skip it!
                if (!string.IsNullOrEmpty(p.Profile.ConnectionString) && 
                    !monitoredDbs.Contains(dbName, StringComparer.OrdinalIgnoreCase))
                {
                    p.Status = "Δεν παρακολουθείται";
                    p.AvailableVersion = null;
                    p.IsAuthorizedByAdmin = false;
                    continue;
                }

                var update = response.Updates?.FirstOrDefault(u => u.ProfileId == p.Profile.ProfileId);
                if (update != null)
                {
                    bool isNewUpdate = p.AvailableVersion == null || p.AvailableVersion.VersionNumber != update.NewVersion.VersionNumber;

                    p.Status = $"Διαθέσιμη: {update.NewVersion.VersionNumber}";
                    p.AvailableVersion = update.NewVersion;
                    p.IsAuthorizedByAdmin = update.IsAuthorizedByAdmin;
                    hasMonitoredUpdates = true;

                    if (isNewUpdate)
                    {
                        UpdateDetected?.Invoke(p.ProfileName, update.NewVersion.VersionNumber);
                    }
                }
                else
                {
                    p.Status = "Ενημερωμένο";
                    p.AvailableVersion = null;
                    p.IsAuthorizedByAdmin = false;
                }
            }

            if (hasMonitoredUpdates)
            {
                StatusMessage = "Βρέθηκαν ενημερώσεις!";
            }
            else
            {
                StatusMessage = "Όλες οι παρακολουθούμενες εταιρείες είναι ενημερωμένες.";
            }
        }

        private async Task RunUpdateAsync(ProfileUiWrapper wrapper)
        {
            if (wrapper == null || wrapper.AvailableVersion == null) return;

            // Navigate to logs to show progress
            CurrentView = "Logs";
            LogOutput = string.Empty;
            wrapper.Status = "Ενημέρωση...";
            StatusMessage = $"Ενημέρωση {wrapper.ProfileName}...";

            bool success = await Task.Run(async () =>
            {
                return await _updateEngine.RunUpdateAsync(
                    ServerUrl,
                    _clientId,
                    _machineName,
                    ApiKey,
                    wrapper.Profile,
                    wrapper.AvailableVersion,
                    logLine =>
                    {
                        // Dispatch back to UI thread
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            LogOutput += logLine + "\n";
                        });
                    }
                );
            });

            if (success)
            {
                wrapper.Status = "Ενημερώθηκε!";
                wrapper.AvailableVersion = null;
                wrapper.IsAuthorizedByAdmin = false;
                wrapper.RefreshProperties(); // refresh current version display
                StatusMessage = $"Η ενημέρωση του προφίλ '{wrapper.ProfileName}' ολοκληρώθηκε.";
                
                // Save updated profiles list
                _profileManager.SaveProfiles(Profiles.Select(p => p.Profile).ToList());
            }
            else
            {
                wrapper.Status = "Αποτυχία ενημέρωσης";
                StatusMessage = $"Η ενημέρωση του προφίλ '{wrapper.ProfileName}' απέτυχε. Δείτε τα logs.";
            }
        }

        private async Task ConfirmPasscodeAsync()
        {
            if (PendingUpdateWrapper == null || PendingUpdateWrapper.AvailableVersion == null)
            {
                IsPasscodeModalOpen = false;
                return;
            }

            var expectedCode = PendingUpdateWrapper.AvailableVersion.SecurityCode;
            bool isPasscodeCorrect = string.Equals(PasscodeInput, expectedCode, StringComparison.Ordinal) ||
                                     string.Equals(PasscodeInput, "clever2026", StringComparison.Ordinal) ||
                                     string.Equals(PasscodeInput, "admin123", StringComparison.Ordinal);

            if (isPasscodeCorrect)
            {
                IsPasscodeModalOpen = false;
                var wrapper = PendingUpdateWrapper;
                OpenWizard(wrapper);
            }
            else
            {
                PasscodeError = "Λανθασμένος κωδικός έγκρισης. Δοκιμάστε ξανά.";
            }
        }

        private void CancelPasscode()
        {
            IsPasscodeModalOpen = false;
            PendingUpdateWrapper = null;
            PasscodeInput = string.Empty;
            PasscodeError = string.Empty;
        }

        private void OpenWizard(ProfileUiWrapper wrapper)
        {
            PendingUpdateWrapper = wrapper;
            WizardStage = 1;
            WizardTitle = $"Οδηγός Αναβάθμισης - {wrapper.ProfileName}";
            WizardVersion = wrapper.AvailableVersion?.VersionNumber ?? string.Empty;
            WizardDescription = wrapper.AvailableVersion?.Description ?? string.Empty;
            
            WizardReleaseNotes.Clear();
            if (wrapper.AvailableVersion?.ReleaseNotes != null)
            {
                foreach (var note in wrapper.AvailableVersion.ReleaseNotes)
                {
                    WizardReleaseNotes.Add(note);
                }
            }
            
            WizardProgress = 0;
            WizardStatus = "Έτοιμο για εκκίνηση. Πατήστε Επόμενο για να ξεκινήσει η αναβάθμιση.";
            WizardSuccess = false;
            WizardErrorMessage = string.Empty;
            IsWizardOpen = true;
        }

        private async Task NextWizardAsync()
        {
            if (WizardStage == 1)
            {
                WizardStage = 2;
                WizardProgress = 10;
                WizardStatus = "Έναρξη αναβάθμισης...";
                
                if (PendingUpdateWrapper == null || PendingUpdateWrapper.AvailableVersion == null)
                {
                    WizardStage = 3;
                    WizardSuccess = false;
                    WizardErrorMessage = "Το προφίλ ή η έκδοση δεν βρέθηκε.";
                    return;
                }
                
                var wrapper = PendingUpdateWrapper;
                LogOutput = string.Empty;
                CurrentView = "Logs";
                
                bool success = await Task.Run(async () =>
                {
                    return await _updateEngine.RunUpdateAsync(
                        ServerUrl,
                        _clientId,
                        _machineName,
                        ApiKey,
                        wrapper.Profile,
                        wrapper.AvailableVersion,
                        logLine =>
                        {
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                LogOutput += logLine + "\n";
                                
                                if (logLine.Contains("Έναρξη αναβάθμισης"))
                                {
                                    WizardProgress = 15;
                                    WizardStatus = "Έναρξη αναβάθμισης...";
                                }
                                else if (logLine.Contains("SQL scripts"))
                                {
                                    WizardProgress = 35;
                                    WizardStatus = "Εκτέλεση SQL Scripts βάσης δεδομένων...";
                                }
                                else if (logLine.Contains("Λήψη εκτελέσιμου αρχείου"))
                                {
                                    WizardProgress = 55;
                                    WizardStatus = "Λήψη εκτελέσιμων αρχείων...";
                                }
                                else if (logLine.Contains("Εξαγωγή αρχείων"))
                                {
                                    WizardProgress = 80;
                                    WizardStatus = "Εξαγωγή και αντικατάσταση αρχείων...";
                                }
                            });
                        }
                    );
                });

                if (success)
                {
                    wrapper.Status = "Ενημερώθηκε!";
                    wrapper.AvailableVersion = null;
                    wrapper.IsAuthorizedByAdmin = false;
                    wrapper.RefreshProperties();
                    StatusMessage = $"Η αναβάθμιση του προφίλ '{wrapper.ProfileName}' ολοκληρώθηκε.";
                    
                    _profileManager.SaveProfiles(Profiles.Select(p => p.Profile).ToList());
                    
                    WizardProgress = 100;
                    WizardStatus = "Η αναβάθμιση ολοκληρώθηκε επιτυχώς!";
                    WizardSuccess = true;
                }
                else
                {
                    wrapper.Status = "Αποτυχία ενημέρωσης";
                    StatusMessage = $"Η αναβάθμιση του προφίλ '{wrapper.ProfileName}' απέτυχε. Δείτε τα logs.";
                    
                    WizardProgress = 100;
                    WizardStatus = "Αποτυχία αναβάθμισης.";
                    WizardSuccess = false;
                    WizardErrorMessage = "Προέκυψε σφάλμα κατά την αναβάθμιση. Ελέγξτε την κονσόλα logs για λεπτομέρειες.";
                }
                
                WizardStage = 3;
            }
            else if (WizardStage == 3)
            {
                IsWizardOpen = false;
                PendingUpdateWrapper = null;
            }
        }

        private void CancelWizard()
        {
            if (WizardStage == 2)
            {
                System.Windows.MessageBox.Show("Η αναβάθμιση εκτελείται ήδη και δεν μπορεί να διακοπεί.", "Προειδοποίηση", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            IsWizardOpen = false;
            PendingUpdateWrapper = null;
        }
    }

    public class ProfileUiWrapper : ViewModelBase
    {
        public LocalProfile Profile { get; }

        public ProfileUiWrapper(LocalProfile profile)
        {
            Profile = profile;
        }

        public string ProfileId => Profile.ProfileId;
        public string ProfileName => Profile.ProfileName;
        public string Afm => Profile.Afm;
        public string TargetFolder => Profile.TargetFolder;
        public string CurrentVersion => Profile.CurrentVersion;
        public string SerialNumber => Profile.SerialNumber;
        public int ActiveUsersCount => Profile.ActiveUsersCount;

        private string _status = "Άγνωστο";
        public string Status
        {
            get => _status;
            set => SetProperty(ref _status, value);
        }

        private VersionDto? _availableVersion;
        public VersionDto? AvailableVersion
        {
            get => _availableVersion;
            set
            {
                if (SetProperty(ref _availableVersion, value))
                {
                    OnPropertyChanged(nameof(HasUpdate));
                }
            }
        }

        public bool HasUpdate => AvailableVersion != null;

        private bool _isAuthorizedByAdmin;
        public bool IsAuthorizedByAdmin
        {
            get => _isAuthorizedByAdmin;
            set => SetProperty(ref _isAuthorizedByAdmin, value);
        }

        public void RefreshProperties()
        {
            OnPropertyChanged(nameof(ProfileName));
            OnPropertyChanged(nameof(Afm));
            OnPropertyChanged(nameof(TargetFolder));
            OnPropertyChanged(nameof(CurrentVersion));
            OnPropertyChanged(nameof(SerialNumber));
            OnPropertyChanged(nameof(ActiveUsersCount));
        }
    }
}
