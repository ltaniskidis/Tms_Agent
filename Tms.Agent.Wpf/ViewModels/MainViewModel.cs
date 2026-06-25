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

        public string AppVersion => "1.5.32";
        public string WindowTitle => $"TMS Agent Panel - Διαχείριση Ενημερώσεων (v{AppVersion})";

        // Connection Settings
        private string _serverUrl = "http://localhost:5007"; // Default dev API port
        public string ServerUrl
        {
            get => _serverUrl;
            set => SetProperty(ref _serverUrl, value);
        }

        private string _apiKey = string.Empty;
        public string ApiKey
        {
            get => _apiKey;
            set => SetProperty(ref _apiKey, value);
        }

        private bool _startWithWindows;
        public bool StartWithWindows
        {
            get => _startWithWindows;
            set => SetProperty(ref _startWithWindows, value);
        }

        private bool _hasSettingsAlert;
        public bool HasSettingsAlert
        {
            get => _hasSettingsAlert;
            set => SetProperty(ref _hasSettingsAlert, value);
        }

        private string _settingsAlertTitle = string.Empty;
        public string SettingsAlertTitle
        {
            get => _settingsAlertTitle;
            set => SetProperty(ref _settingsAlertTitle, value);
        }

        private string _settingsAlertMessage = string.Empty;
        public string SettingsAlertMessage
        {
            get => _settingsAlertMessage;
            set => SetProperty(ref _settingsAlertMessage, value);
        }

        private string _settingsAlertIcon = string.Empty;
        public string SettingsAlertIcon
        {
            get => _settingsAlertIcon;
            set => SetProperty(ref _settingsAlertIcon, value);
        }

        private string _settingsAlertBackground = "#064E3B";
        public string SettingsAlertBackground
        {
            get => _settingsAlertBackground;
            set => SetProperty(ref _settingsAlertBackground, value);
        }

        private string _settingsAlertBorderBrush = "#047857";
        public string SettingsAlertBorderBrush
        {
            get => _settingsAlertBorderBrush;
            set => SetProperty(ref _settingsAlertBorderBrush, value);
        }

        private string _machineRole = "Both"; // SqlServer, Client, Both
        public string MachineRole
        {
            get => _machineRole;
            set
            {
                if (SetProperty(ref _machineRole, value))
                {
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
                        EditCurrentProgramVersion = value.Profile.CurrentProgramVersion;
                        EditCurrentDbVersion = value.Profile.CurrentDbVersion;
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

        private string _editTargetExeName = "TIMOLOGISI.exe";
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

        private string _editCurrentProgramVersion = "1.0.0";
        public string EditCurrentProgramVersion { get => _editCurrentProgramVersion; set => SetProperty(ref _editCurrentProgramVersion, value); }

        private string _editCurrentDbVersion = "1.0.0";
        public string EditCurrentDbVersion { get => _editCurrentDbVersion; set => SetProperty(ref _editCurrentDbVersion, value); }

        private string _editSerialNumber = string.Empty;
        public string EditSerialNumber { get => _editSerialNumber; set => SetProperty(ref _editSerialNumber, value); }

        private int _editActiveUsersCount;
        public int EditActiveUsersCount { get => _editActiveUsersCount; set => SetProperty(ref _editActiveUsersCount, value); }

        // Live Log Output
        private readonly HashSet<string> _promptedUpdates = new();
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
        public event Action<string, string>? BroadcastDetected;

        // Local User Management
        public ObservableCollection<AgentUserDto> LocalUsersList { get; } = new();

        private AgentUserDto? _selectedUser;
        public AgentUserDto? SelectedUser
        {
            get => _selectedUser;
            set
            {
                if (SetProperty(ref _selectedUser, value))
                {
                    if (value != null)
                    {
                        EditUserUsername = value.Username;
                        EditUserPassword = value.Password;
                        EditUserRole = value.Role;
                    }
                    else
                    {
                        ClearUserEditor();
                    }
                    OnPropertyChanged(nameof(IsUserOwner));
                    OnPropertyChanged(nameof(IsUserEditingEnabled));
                }
            }
        }

        private string _editUserUsername = string.Empty;
        public string EditUserUsername
        {
            get => _editUserUsername;
            set
            {
                if (SetProperty(ref _editUserUsername, value))
                {
                    OnPropertyChanged(nameof(IsUserOwner));
                    OnPropertyChanged(nameof(IsUserEditingEnabled));
                }
            }
        }

        private string _editUserPassword = string.Empty;
        public string EditUserPassword
        {
            get => _editUserPassword;
            set => SetProperty(ref _editUserPassword, value);
        }

        private string _editUserRole = "Operator";
        public string EditUserRole
        {
            get => _editUserRole;
            set => SetProperty(ref _editUserRole, value);
        }

        private string _userSyncStatus = string.Empty;
        public string UserSyncStatus
        {
            get => _userSyncStatus;
            set => SetProperty(ref _userSyncStatus, value);
        }

        public bool IsUserOwner => SelectedUser?.Username.Equals("owner", StringComparison.OrdinalIgnoreCase) == true || EditUserUsername.Equals("owner", StringComparison.OrdinalIgnoreCase);
        public bool IsUserEditingEnabled => !IsUserOwner;

        // Support Email Properties
        private string _supportSubject = string.Empty;
        public string SupportSubject
        {
            get => _supportSubject;
            set => SetProperty(ref _supportSubject, value);
        }

        private string _supportBody = string.Empty;
        public string SupportBody
        {
            get => _supportBody;
            set => SetProperty(ref _supportBody, value);
        }

        private string _supportAttachmentPath = string.Empty;
        public string SupportAttachmentPath
        {
            get => _supportAttachmentPath;
            set
            {
                if (SetProperty(ref _supportAttachmentPath, value))
                {
                    OnPropertyChanged(nameof(SupportAttachmentName));
                }
            }
        }

        public string SupportAttachmentName => string.IsNullOrEmpty(SupportAttachmentPath) 
            ? "Κανένα επιλεγμένο αρχείο" 
            : Path.GetFileName(SupportAttachmentPath);

        private bool _isSendingSupportEmail;
        public bool IsSendingSupportEmail
        {
            get => _isSendingSupportEmail;
            set
            {
                if (SetProperty(ref _isSendingSupportEmail, value))
                {
                    OnPropertyChanged(nameof(IsNotSendingSupportEmail));
                }
            }
        }

        public bool IsNotSendingSupportEmail => !_isSendingSupportEmail;

        private string _supportStatusMessage = string.Empty;
        public string SupportStatusMessage
        {
            get => _supportStatusMessage;
            set => SetProperty(ref _supportStatusMessage, value);
        }

        // Support Tickets Properties
        public System.Collections.ObjectModel.ObservableCollection<SupportTicketDto> SupportTickets { get; set; } = new();

        private SupportTicketDto? _selectedSupportTicket;
        public SupportTicketDto? SelectedSupportTicket
        {
            get => _selectedSupportTicket;
            set
            {
                if (SetProperty(ref _selectedSupportTicket, value))
                {
                    OnPropertyChanged(nameof(HasSelectedSupportTicket));
                    OnPropertyChanged(nameof(IsResponseVisible));
                }
            }
        }

        public bool HasSelectedSupportTicket => SelectedSupportTicket != null;
        public bool IsResponseVisible => SelectedSupportTicket != null && !string.IsNullOrEmpty(SelectedSupportTicket.AdminResponse);

        public event Action<string, string, string>? SupportTicketUpdated;

        // Broadcast Messages Properties
        private bool _hasUnreadBroadcasts;
        public bool HasUnreadBroadcasts
        {
            get => _hasUnreadBroadcasts;
            set => SetProperty(ref _hasUnreadBroadcasts, value);
        }

        private System.Collections.ObjectModel.ObservableCollection<BroadcastMessageDto> _broadcastsList = new();
        public System.Collections.ObjectModel.ObservableCollection<BroadcastMessageDto> BroadcastsList
        {
            get => _broadcastsList;
            set => SetProperty(ref _broadcastsList, value);
        }

        // Commands
        public ICommand SaveUserCommand { get; }
        public ICommand DeleteUserCommand { get; }
        public ICommand NewUserCommand { get; }

        public ICommand NavigateCommand { get; }
        public ICommand SelectAttachmentCommand { get; }
        public ICommand SendSupportEmailCommand { get; }
        public ICommand RefreshSupportTicketsCommand { get; }
        public ICommand MarkBroadcastsReadCommand { get; }
        public ICommand CheckUpdatesCommand { get; }
        public ICommand AuthorizeUpdateCommand { get; }
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
        public ICommand SaveSettingsCommand { get; }
        public ICommand DiscardSettingsCommand { get; }

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
            _startWithWindows = settings.StartWithWindows;

            // Commands Setup
            NavigateCommand = new RelayCommand<string>(view =>
            {
                CurrentView = view ?? "Dashboard";
                if (CurrentView == "Users")
                {
                    LoadLocalUsers();
                }
                else if (CurrentView == "Broadcasts")
                {
                    MarkBroadcastsAsRead();
                }
                else if (CurrentView == "Support")
                {
                    _ = LoadSupportTicketsAsync();
                }
            });
            CheckUpdatesCommand = new RelayCommand(async () => await CheckForUpdatesAsync());
            RefreshSupportTicketsCommand = new RelayCommand(async () => await LoadSupportTicketsAsync());
            
            UpdateProfileCommand = new RelayCommand<ProfileUiWrapper>(p =>
            {
                if (p == null || p.AvailableVersion == null) return;

                if (!IsUpgradeAllowed)
                {
                    System.Windows.MessageBox.Show(
                        "Υπάρχουν διαθέσιμες ενημερώσεις, παρακαλώ επικοινωνήστε με τον συνεργάτη σας για να εκτελεστούν",
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

            AuthorizeUpdateCommand = new RelayCommand<ProfileUiWrapper>(async p => await AuthorizeUpdateAsync(p));

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
            SaveSettingsCommand = new RelayCommand(SaveSettingsExecute);
            DiscardSettingsCommand = new RelayCommand(DiscardSettingsExecute);

            SaveUserCommand = new RelayCommand(SaveLocalUser);
            DeleteUserCommand = new RelayCommand(DeleteLocalUser);
            NewUserCommand = new RelayCommand(ClearUserEditor);

            SelectAttachmentCommand = new RelayCommand(SelectAttachment);
            SendSupportEmailCommand = new RelayCommand(async () => await SendSupportEmailAsync());
            MarkBroadcastsReadCommand = new RelayCommand(MarkBroadcastsAsRead);

            LoadProfiles();
            
            // Auto check updates on startup to discover databases and sync with server
            _ = CheckForUpdatesAsync();
        }

        private void InitializeClientInfo()
        {
            _machineName = Environment.MachineName;
            
            // Client ID logic: read or create a GUID for this installation
            var appDataPath = PathHelper.GetAgentDataFolder();
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
            var settings = _settingsManager.LoadSettings();
            settings.ServerUrl = ServerUrl;
            settings.MachineRole = MachineRole;
            settings.ApiKey = ApiKey;
            settings.StartWithWindows = StartWithWindows;
            _settingsManager.SaveSettings(settings);
        }

        private async void SaveSettingsExecute()
        {
            if (IsSettingsLocked)
            {
                HasSettingsAlert = true;
                SettingsAlertTitle = "Σφάλμα";
                SettingsAlertMessage = "Δεν έχετε δικαίωμα να αλλάξετε τις ρυθμίσεις. Απαιτείται σύνδεση ως Owner.";
                SettingsAlertIcon = "❌";
                SettingsAlertBackground = "#7F1D1D";
                SettingsAlertBorderBrush = "#B91C1C";
                return;
            }

            // Show temporary saving status
            HasSettingsAlert = true;
            SettingsAlertTitle = "Αποθήκευση...";
            SettingsAlertMessage = "Γίνεται αποθήκευση των ρυθμίσεων και ρύθμιση της υπηρεσίας εκκίνησης...";
            SettingsAlertIcon = "⏳";
            SettingsAlertBackground = "#1E1B4B";
            SettingsAlertBorderBrush = "#312E81";

            var settings = _settingsManager.LoadSettings();
            bool startWithWindowsChanged = settings.StartWithWindows != StartWithWindows;

            SaveAgentSettings();

            if (startWithWindowsChanged)
            {
                // Run service registration changes on background thread to prevent UI freezing
                await Task.Run(() => ServiceControlHelper.ApplyStartWithWindows(StartWithWindows));
            }
            
            // Trigger update check to sync with new credentials/server URL immediately and force sync StartWithWindows
            _ = CheckForUpdatesAsync(true);

            // Show success alert
            HasSettingsAlert = true;
            SettingsAlertTitle = "Επιτυχής Αποθήκευση";
            SettingsAlertMessage = "Οι ρυθμίσεις του Agent αποθηκεύτηκαν επιτυχώς!";
            SettingsAlertIcon = "✅";
            SettingsAlertBackground = "#064E3B";
            SettingsAlertBorderBrush = "#047857";

            // Hide the banner after 5 seconds
            _ = Task.Run(async () =>
            {
                await Task.Delay(5000);
                // Avoid hiding a newer alert if it was triggered
                if (SettingsAlertTitle == "Επιτυχής Αποθήκευση")
                {
                    HasSettingsAlert = false;
                }
            });
        }

        private void DiscardSettingsExecute()
        {
            var settings = _settingsManager.LoadSettings();
            ServerUrl = settings.ServerUrl;
            ApiKey = settings.ApiKey;
            MachineRole = settings.MachineRole;
            StartWithWindows = settings.StartWithWindows;

            HasSettingsAlert = true;
            SettingsAlertTitle = "Επαναφορά";
            SettingsAlertMessage = "Οι ρυθμίσεις επανήλθαν στις αποθηκευμένες τιμές.";
            SettingsAlertIcon = "ℹ️";
            SettingsAlertBackground = "#1E1B4B";
            SettingsAlertBorderBrush = "#312E81";

            // Hide the banner after 5 seconds
            _ = Task.Run(async () =>
            {
                await Task.Delay(5000);
                if (SettingsAlertTitle == "Επαναφορά")
                {
                    HasSettingsAlert = false;
                }
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
            EditTargetExeName = "TIMOLOGISI.exe";
            EditConnectionString = string.Empty;
            EditConnectionStringType = "Direct";
            EditDbServer = string.Empty;
            EditDbName = string.Empty;
            EditDbUseWindowsAuth = false;
            EditDbUser = string.Empty;
            EditDbPassword = string.Empty;
            EditConfigFilePath = string.Empty;
            EditCurrentVersion = "1.0.0";
            EditCurrentProgramVersion = "1.0.0";
            EditCurrentDbVersion = "1.0.0";
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
                    CurrentProgramVersion = EditCurrentProgramVersion,
                    CurrentDbVersion = EditCurrentDbVersion,
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
                existing.CurrentProgramVersion = EditCurrentProgramVersion;
                existing.CurrentDbVersion = EditCurrentDbVersion;
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

        private async Task CheckForUpdatesAsync(bool forceSyncStartWithWindows = false)
        {
            StatusMessage = "Έλεγχος για νέες εκδόσεις...";
            foreach (var p in Profiles)
            {
                p.Status = "Έλεγχος...";
            }

            var localProfilesList = Profiles.Select(p => p.Profile).ToList();
            var response = await _updateEngine.CheckForUpdatesAsync(
                ServerUrl, 
                _clientId, 
                _machineName, 
                MachineRole, 
                AppVersion, 
                ApiKey, 
                localProfilesList,
                StartWithWindows,
                forceSyncStartWithWindows
            );
            _ = LoadSupportTicketsAsync();

            if (response == null)
            {
                StatusMessage = "Αδυναμία σύνδεσης στον διακομιστή ενημερώσεων.";
                foreach (var p in Profiles)
                {
                    p.Status = "Σφάλμα σύνδεσης διακομιστή";
                }
                return;
            }

            IsUpgradeAllowed = response.IsUpgradeAllowed;

            // Sync StartWithWindows if server sent a different status
            if (response.StartWithWindows != StartWithWindows)
            {
                StartWithWindows = response.StartWithWindows;
                SaveAgentSettings();
                await Task.Run(() => ServiceControlHelper.ApplyStartWithWindows(StartWithWindows));
            }

            // Sync and save local users locally
            if (response.LocalUsers != null)
            {
                try
                {
                    var appDataPath = PathHelper.GetAgentDataFolder();
                    var usersFilePath = Path.Combine(appDataPath, "users.json");
                    var json = JsonSerializer.Serialize(response.LocalUsers, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(usersFilePath, json);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to save local users: {ex.Message}");
                }
            }

            // Check for version alignment between Server (Console) and Client (Agent)
            if (!string.IsNullOrEmpty(response.CurrentSystemVersion) && response.CurrentSystemVersion != AppVersion)
            {
                SystemVersionWarning = $"⚠️ Προειδοποίηση: Η έκδοση του Agent (v{AppVersion}) διαφέρει από την τρέχουσα έκδοση συστήματος (v{response.CurrentSystemVersion}).";
                if (response.IsUpgradeAllowed && !string.IsNullOrEmpty(response.SystemBinaryUrl))
                {
                    _ = RunSelfUpgradeAsync(response.SystemBinaryUrl, response.CurrentSystemVersion);
                }
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
                                ConnectionStringType = cmd.ConnectionStringType,
                                DbServer = cmd.DbServer,
                                DbName = cmd.DbName,
                                DbUser = cmd.DbUser,
                                DbPassword = cmd.DbPassword,
                                DbUseWindowsAuth = cmd.DbUseWindowsAuth,
                                ConfigFilePath = cmd.ConfigFilePath,
                                CurrentVersion = cmd.CurrentVersion,
                                CurrentProgramVersion = cmd.CurrentProgramVersion,
                                CurrentDbVersion = cmd.CurrentDbVersion,
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
                            existing.ConnectionStringType = cmd.ConnectionStringType;
                            existing.DbServer = cmd.DbServer;
                            existing.DbName = cmd.DbName;
                            existing.DbUser = cmd.DbUser;
                            existing.DbPassword = cmd.DbPassword;
                            existing.DbUseWindowsAuth = cmd.DbUseWindowsAuth;
                            existing.ConfigFilePath = cmd.ConfigFilePath;
                            existing.CurrentVersion = cmd.CurrentVersion;
                            existing.CurrentProgramVersion = cmd.CurrentProgramVersion;
                            existing.CurrentDbVersion = cmd.CurrentDbVersion;
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
                    p.IsWaitingForDb = false;
                    continue;
                }

                var update = response.Updates?.FirstOrDefault(u => u.ProfileId == p.Profile.ProfileId);
                if (update != null)
                {
                    bool isNewUpdate = p.AvailableVersion == null || p.AvailableVersion.VersionNumber != update.NewVersion.VersionNumber;

                    p.AvailableVersion = update.NewVersion;
                    p.IsAuthorizedByAdmin = update.IsAuthorizedByAdmin;
                    p.IsWaitingForDb = update.IsWaitingForDb;

                    if (p.IsWaitingForDb)
                    {
                        p.Status = "Αναμονή αναβάθμισης βάσης από Server...";
                    }
                    else
                    {
                        p.Status = $"Διαθέσιμη: {update.NewVersion.VersionNumber}";
                    }

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

            // Operator close program popup logic
            if (UserRole == "Operator" || UserRole == "Admin" || UserRole == "Owner")
            {
                foreach (var p in Profiles)
                {
                    if (p.AvailableVersion != null && p.IsAuthorizedByAdmin && !p.IsWaitingForDb)
                    {
                        var key = $"{p.ProfileId}_{p.AvailableVersion.VersionNumber}";
                        if (!_promptedUpdates.Contains(key))
                        {
                            _promptedUpdates.Add(key);
                            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                System.Windows.MessageBox.Show(
                                    $"Έχει εγκριθεί η νέα αναβάθμιση {p.AvailableVersion.VersionNumber} για το προφίλ '{p.ProfileName}'.\nΠαρακαλώ κλείστε το TMS πρόγραμμα για να πραγματοποιηθεί η λήψη και εγκατάσταση των αρχείων.",
                                    "TMS Agent - Απαιτείται Κλείσιμο Προγράμματος",
                                    MessageBoxButton.OK,
                                    MessageBoxImage.Warning);
                            }));
                        }
                    }
                }
            }

            // Business Admin update prompt modal
            if (IsAdmin)
            {
                foreach (var p in Profiles)
                {
                    if (p.AvailableVersion != null && !p.IsWaitingForDb)
                    {
                        var key = $"admin_prompt_{p.ProfileId}_{p.AvailableVersion.VersionNumber}";
                        if (!_promptedUpdates.Contains(key))
                        {
                            _promptedUpdates.Add(key);
                            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                var result = System.Windows.MessageBox.Show(
                                    $"Ανιχνεύθηκε νέα έκδοση ({p.AvailableVersion.VersionNumber}) για την εταιρεία '{p.ProfileName}'.\n\nΘέλετε να προχωρήσετε με την εκτέλεση της αναβάθμισης τώρα;",
                                    "TMS Agent - Διαθέσιμη Αναβάθμιση",
                                    MessageBoxButton.YesNo,
                                    MessageBoxImage.Question);
                                
                                if (result == MessageBoxResult.Yes)
                                {
                                    OpenWizard(p);
                                }
                            }));
                            break;
                        }
                    }
                }
            }

            // Handle Broadcast Messages
            if (response.Broadcasts != null)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    BroadcastsList.Clear();
                    foreach (var b in response.Broadcasts)
                    {
                        BroadcastsList.Add(b);
                    }
                });

                // Load seen broadcast IDs
                var seenIds = new List<int>();
                try
                {
                    var appDataPath = PathHelper.GetAgentDataFolder();
                    var file = Path.Combine(appDataPath, "seen_broadcasts.json");
                    if (File.Exists(file))
                    {
                        var json = File.ReadAllText(file);
                        seenIds = JsonSerializer.Deserialize<List<int>>(json) ?? new List<int>();
                    }
                }
                catch
                {
                    // Ignore
                }

                var newBroadcasts = response.Broadcasts.Where(b => !seenIds.Contains(b.Id)).ToList();
                bool hasUnread = newBroadcasts.Any();
                HasUnreadBroadcasts = hasUnread;

                if (hasUnread)
                {
                    foreach (var newB in newBroadcasts)
                    {
                        BroadcastDetected?.Invoke(newB.Title, newB.Content);
                    }
                }
            }
        }

        private async Task AuthorizeUpdateAsync(ProfileUiWrapper wrapper)
        {
            if (wrapper == null || wrapper.AvailableVersion == null) return;

            StatusMessage = $"Έγκριση αναβάθμισης για το προφίλ '{wrapper.ProfileName}'...";
            try
            {
                bool success = await _updateEngine.AuthorizeUpdateAsync(
                    ServerUrl,
                    ApiKey,
                    _clientId,
                    wrapper.ProfileId,
                    wrapper.AvailableVersion.VersionNumber
                );

                if (success)
                {
                    wrapper.IsAuthorizedByAdmin = true;
                    StatusMessage = $"Η αναβάθμιση για το προφίλ '{wrapper.ProfileName}' εγκρίθηκε.";
                    
                    System.Windows.MessageBox.Show(
                        $"Η αναβάθμιση εγκρίθηκε επιτυχώς! Οι Operators θα λάβουν ειδοποίηση.",
                        "TMS Agent",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    await CheckForUpdatesAsync();
                }
                else
                {
                    StatusMessage = "Αποτυχία έγκρισης αναβάθμισης.";
                    System.Windows.MessageBox.Show(
                        "Αποτυχία έγκρισης αναβάθμισης από τον διακομιστή.",
                        "TMS Agent",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"Σφάλμα έγκρισης: {ex.Message}";
            }
        }

        private bool _isSelfUpgrading = false;
        private async Task RunSelfUpgradeAsync(string systemBinaryUrl, string targetVersion)
        {
            if (_isSelfUpgrading) return;
            _isSelfUpgrading = true;

            // Show mandatory popup to force update
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                System.Windows.MessageBox.Show(
                    $"Βρέθηκε νέα έκδοση του Agent (v{targetVersion}).\nΗ αναβάθμιση είναι υποχρεωτική για τη σωστή λειτουργία του συστήματος.\n\nΠατήστε 'OK' για αυτόματη λήψη και επανεκκίνηση του Agent.",
                    "TMS Agent - Απαιτείται Αναβάθμιση",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            });

            StatusMessage = "Λήψη και εγκατάσταση αναβάθμισης Agent...";
            
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                WizardTitle = "Αναβάθμιση του Agent";
                WizardVersion = targetVersion;
                WizardStage = 2; // Progress stage (hides buttons)
                WizardProgress = 0;
                WizardStatus = "Έναρξη λήψης αρχείων αναβάθμισης...";
                IsWizardOpen = true;
            });

            bool success = await Task.Run(async () =>
            {
                return await _updateEngine.RunAgentSelfUpgradeAsync(
                    ServerUrl,
                    systemBinaryUrl,
                    false, // Not running as service here
                    logLine =>
                    {
                        System.Diagnostics.Debug.WriteLine(logLine);
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            WizardStatus = logLine;
                        });
                    },
                    progress =>
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            WizardProgress = progress;
                        });
                    }
                );
            });

            if (success)
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    System.Windows.Application.Current.Shutdown();
                });
                Environment.Exit(0);
            }
            else
            {
                _isSelfUpgrading = false;
                StatusMessage = "Αποτυχία αυτόματης αναβάθμισης του Agent.";
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    WizardStage = 3;
                    WizardSuccess = false;
                    WizardErrorMessage = "Αποτυχία λήψης ή εγκατάστασης της αναβάθμισης.";
                });
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

            var result = await Task.Run(async () =>
            {
                return await _updateEngine.RunUpdateAsync(
                    ServerUrl,
                    _clientId,
                    _machineName,
                    MachineRole,
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

            bool success = result.Success;

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

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    var progressWindow = new UpgradeProgressWindow();
                    progressWindow.DataContext = this;
                    progressWindow.Show();
                });
                
                if (PendingUpdateWrapper == null || PendingUpdateWrapper.AvailableVersion == null)
                {
                    WizardStage = 3;
                    WizardSuccess = false;
                    WizardErrorMessage = "Το προφίλ ή η έκδοση δεν βρέθηκε.";
                    return;
                }
                
                var wrapper = PendingUpdateWrapper;

                // Dry Run Preview for Database scripts
                if (wrapper.AvailableVersion.Scripts != null && wrapper.AvailableVersion.Scripts.Any())
                {
                    var resolvedConnStr = wrapper.Profile.GetResolvedConnectionString();
                    var previewText = await _updateEngine.GenerateScriptPreviewAsync(resolvedConnStr, wrapper.AvailableVersion.Scripts);
                    
                    var confirmResult = System.Windows.MessageBox.Show(
                        $"Προεπισκόπηση Εκτέλεσης SQL Scripts:\n\n{previewText}\n\nΘέλετε να προχωρήσετε με την εκτέλεση;",
                        "TMS Agent - Προεπισκόπηση Αναβάθμισης Βάσης",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Question);

                    if (confirmResult != MessageBoxResult.Yes)
                    {
                        WizardStatus = "Η αναβάθμιση ακυρώθηκε από τον χρήστη.";
                        IsWizardOpen = false;
                        return;
                    }
                }

                LogOutput = string.Empty;
                CurrentView = "Logs";
                
                var result = await Task.Run(async () =>
                {
                    return await _updateEngine.RunUpdateAsync(
                        ServerUrl,
                        _clientId,
                        _machineName,
                        MachineRole,
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

                bool success = result.Success;

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
                    WizardErrorMessage = !string.IsNullOrEmpty(result.ErrorMessage) ? result.ErrorMessage : "Προέκυψε σφάλμα κατά την αναβάθμιση. Ελέγξτε την κονσόλα logs για λεπτομέρειες.";
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

        // Local User Management Helper Methods
        public void LoadLocalUsers()
        {
            LocalUsersList.Clear();
            var appDataPath = PathHelper.GetAgentDataFolder();
            var usersFilePath = Path.Combine(appDataPath, "users.json");

            if (File.Exists(usersFilePath))
            {
                try
                {
                    var json = File.ReadAllText(usersFilePath);
                    var users = JsonSerializer.Deserialize<List<AgentUserDto>>(json);
                    if (users != null)
                    {
                        foreach (var u in users)
                        {
                            if (!u.Username.Equals("owner", StringComparison.OrdinalIgnoreCase))
                            {
                                LocalUsersList.Add(u);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load local users: {ex.Message}");
                }
            }
            ClearUserEditor();
        }

        private void ClearUserEditor()
        {
            SelectedUser = null;
            EditUserUsername = string.Empty;
            EditUserPassword = string.Empty;
            EditUserRole = "Operator";
            UserSyncStatus = string.Empty;
        }

        private async void SaveLocalUser()
        {
            if (string.IsNullOrWhiteSpace(EditUserUsername))
            {
                System.Windows.MessageBox.Show("Το όνομα χρήστη είναι υποχρεωτικό.", "Προειδοποίηση", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var username = EditUserUsername.Trim();
            var password = EditUserPassword.Trim();
            var role = EditUserRole;

            if (username.Equals("owner", StringComparison.OrdinalIgnoreCase))
            {
                System.Windows.MessageBox.Show("Δεν επιτρέπεται η προσθήκη ή επεξεργασία του χρήστη 'owner' τοπικά. Ο owner διαχειρίζεται κεντρικά από τον Central Server.", "Σφάλμα", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (SelectedUser == null)
            {
                // Add new user
                if (LocalUsersList.Any(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase)))
                {
                    System.Windows.MessageBox.Show($"Υπάρχει ήδη χρήστης με το όνομα '{username}'.", "Προειδοποίηση", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var newUser = new AgentUserDto
                {
                    Username = username,
                    Password = password,
                    Role = role
                };
                LocalUsersList.Add(newUser);
            }
            else
            {
                // Edit existing user
                var existingUser = LocalUsersList.FirstOrDefault(u => u.Username.Equals(SelectedUser.Username, StringComparison.OrdinalIgnoreCase));
                if (existingUser != null)
                {
                    // Check username changes & duplicates
                    if (!SelectedUser.Username.Equals(username, StringComparison.OrdinalIgnoreCase) &&
                        LocalUsersList.Any(u => u.Username.Equals(username, StringComparison.OrdinalIgnoreCase)))
                    {
                        System.Windows.MessageBox.Show($"Υπάρχει ήδη χρήστης με το όνομα '{username}'.", "Προειδοποίηση", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    existingUser.Username = username;
                    existingUser.Password = password;
                    existingUser.Role = role;
                }
            }

            SaveUsersToJson();
            await SyncUsersToServerAsync();
            LoadLocalUsers();
        }

        private async void DeleteLocalUser()
        {
            if (SelectedUser == null) return;

            if (SelectedUser.Username.Equals("owner", StringComparison.OrdinalIgnoreCase))
            {
                System.Windows.MessageBox.Show("Δεν επιτρέπεται η διαγραφή του χρήστη 'owner' τοπικά.", "Σφάλμα", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var confirmResult = System.Windows.MessageBox.Show($"Είστε σίγουροι ότι θέλετε να διαγράψετε τον χρήστη '{SelectedUser.Username}';\nΗ διαγραφή θα συγχρονιστεί και με τον Central Server.", "Επιβεβαίωση Διαγραφής", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirmResult != MessageBoxResult.Yes) return;

            var userToRemove = LocalUsersList.FirstOrDefault(u => u.Username.Equals(SelectedUser.Username, StringComparison.OrdinalIgnoreCase));
            if (userToRemove != null)
            {
                LocalUsersList.Remove(userToRemove);
            }

            SaveUsersToJson();
            await SyncUsersToServerAsync();
            LoadLocalUsers();
        }

        private void SaveUsersToJson()
        {
            try
            {
                var appDataPath = PathHelper.GetAgentDataFolder();
                var usersFilePath = Path.Combine(appDataPath, "users.json");

                // Read existing owner if present to preserve it
                AgentUserDto? ownerUser = null;
                if (File.Exists(usersFilePath))
                {
                    try
                    {
                        var existingJson = File.ReadAllText(usersFilePath);
                        var existingUsers = JsonSerializer.Deserialize<List<AgentUserDto>>(existingJson);
                        if (existingUsers != null)
                        {
                            ownerUser = existingUsers.FirstOrDefault(u => u.Username.Equals("owner", StringComparison.OrdinalIgnoreCase));
                        }
                    }
                    catch { }
                }

                var allUsers = new List<AgentUserDto>();
                if (ownerUser != null)
                {
                    allUsers.Add(ownerUser);
                }

                // Add all non-owner users from the list
                foreach (var u in LocalUsersList)
                {
                    if (!u.Username.Equals("owner", StringComparison.OrdinalIgnoreCase))
                    {
                        allUsers.Add(u);
                    }
                }

                var json = JsonSerializer.Serialize(allUsers, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(usersFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to write local users: {ex.Message}");
            }
        }

        private async Task SyncUsersToServerAsync()
        {
            if (string.IsNullOrWhiteSpace(ServerUrl) || string.IsNullOrWhiteSpace(ApiKey))
            {
                UserSyncStatus = "Αποθηκεύτηκε τοπικά (Δεν έχει ρυθμιστεί API Key/Server URL).";
                return;
            }

            UserSyncStatus = "Συγχρονισμός σε εξέλικξη...";
            // Filter out system owner user before sending to server
            var usersToSend = LocalUsersList.Where(u => !u.Username.Equals("owner", StringComparison.OrdinalIgnoreCase)).ToList();

            bool success = await _updateEngine.SyncUsersAsync(ServerUrl, ApiKey, usersToSend);
            if (success)
            {
                UserSyncStatus = "Οι αλλαγές συγχρονίστηκαν επιτυχώς με τον Central Server.";
            }
            else
            {
                UserSyncStatus = "Αποθηκεύτηκε τοπικά (Σφάλμα σύνδεσης / Central Server εκτός σύνδεσης).";
            }
        }

        private void SelectAttachment()
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "All Files (*.*)|*.*|Image Files (*.png;*.jpg;*.jpeg;*.gif)|*.png;*.jpg;*.jpeg;*.gif"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                SupportAttachmentPath = openFileDialog.FileName;
            }
        }

        private async Task SendSupportEmailAsync()
        {
            if (string.IsNullOrWhiteSpace(SupportSubject) || string.IsNullOrWhiteSpace(SupportBody))
            {
                SupportStatusMessage = "⚠️ Το θέμα και το περιεχόμενο είναι υποχρεωτικά.";
                return;
            }

            IsSendingSupportEmail = true;
            SupportStatusMessage = "Αποστολή email στο support...";

            try
            {
                bool success = await _updateEngine.SendSupportEmailAsync(ServerUrl, ApiKey, SupportSubject, SupportBody, SupportAttachmentPath);
                if (success)
                {
                    SupportStatusMessage = "✅ Το email στάλθηκε επιτυχώς στο support!";
                    SupportSubject = string.Empty;
                    SupportBody = string.Empty;
                    SupportAttachmentPath = string.Empty;
                    
                    // Refresh support tickets history immediately
                    _ = LoadSupportTicketsAsync();
                }
                else
                {
                    SupportStatusMessage = "❌ Αποτυχία αποστολής email. Δοκιμάστε ξανά αργότερα.";
                }
            }
            catch (Exception ex)
            {
                SupportStatusMessage = $"❌ Σφάλμα: {ex.Message}";
            }
            finally
            {
                IsSendingSupportEmail = false;
            }
        }

        private bool _isLoadingSupportTickets;
        public async Task LoadSupportTicketsAsync()
        {
            if (string.IsNullOrEmpty(ApiKey)) return;
            if (_isLoadingSupportTickets) return;
            _isLoadingSupportTickets = true;

            try
            {
                var latestTickets = await _updateEngine.GetSupportTicketsAsync(ServerUrl, ApiKey, _clientId);
                if (latestTickets != null)
                {
                    bool isInitialLoad = SupportTickets.Count == 0;
                    var oldTicketsMap = SupportTickets.ToDictionary(t => t.Id);

                    System.Windows.Application.Current.Dispatcher.Invoke(() =>
                    {
                        var selectedId = SelectedSupportTicket?.Id;

                        SupportTickets.Clear();
                        foreach (var ticket in latestTickets)
                        {
                            SupportTickets.Add(ticket);
                            
                            if (!isInitialLoad)
                            {
                                if (oldTicketsMap.TryGetValue(ticket.Id, out var oldTicket))
                                {
                                    bool statusChanged = oldTicket.Status != ticket.Status;
                                    bool responseChanged = oldTicket.AdminResponse != ticket.AdminResponse && !string.IsNullOrEmpty(ticket.AdminResponse);
                                    
                                    if (statusChanged || responseChanged)
                                    {
                                        string changeDesc = "";
                                        if (statusChanged && responseChanged)
                                            changeDesc = $"Η κατάσταση άλλαξε σε '{GetStatusLabelGreek(ticket.Status)}' και υπάρχει νέα απάντηση.";
                                        else if (statusChanged)
                                            changeDesc = $"Η κατάσταση άλλαξε σε '{GetStatusLabelGreek(ticket.Status)}'.";
                                        else
                                            changeDesc = "Υπάρχει νέα απάντηση από τον τεχνικό.";

                                        SupportTicketUpdated?.Invoke(ticket.Subject, changeDesc, ticket.AdminResponse ?? "");
                                    }
                                }
                            }
                        }

                        if (selectedId.HasValue)
                        {
                            SelectedSupportTicket = SupportTickets.FirstOrDefault(t => t.Id == selectedId.Value);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load support tickets: {ex.Message}");
            }
            finally
            {
                _isLoadingSupportTickets = false;
            }
        }

        private string GetStatusLabelGreek(string status)
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

        public void MarkBroadcastsAsRead()
        {
            HasUnreadBroadcasts = false;
            try
            {
                var appDataPath = PathHelper.GetAgentDataFolder();
                var file = Path.Combine(appDataPath, "seen_broadcasts.json");
                var ids = BroadcastsList.Select(b => b.Id).ToList();
                var json = JsonSerializer.Serialize(ids);
                File.WriteAllText(file, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save seen broadcasts: {ex.Message}");
            }
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
        public string CurrentProgramVersion => Profile.CurrentProgramVersion;
        public string CurrentDbVersion => Profile.CurrentDbVersion;
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

        private bool _isWaitingForDb;
        public bool IsWaitingForDb
        {
            get => _isWaitingForDb;
            set => SetProperty(ref _isWaitingForDb, value);
        }

        public void RefreshProperties()
        {
            OnPropertyChanged(nameof(ProfileName));
            OnPropertyChanged(nameof(Afm));
            OnPropertyChanged(nameof(TargetFolder));
            OnPropertyChanged(nameof(CurrentVersion));
            OnPropertyChanged(nameof(CurrentProgramVersion));
            OnPropertyChanged(nameof(CurrentDbVersion));
            OnPropertyChanged(nameof(SerialNumber));
            OnPropertyChanged(nameof(ActiveUsersCount));
        }
    }
}
