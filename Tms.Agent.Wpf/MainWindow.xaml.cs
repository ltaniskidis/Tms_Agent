using System;
using System.Windows;
using Tms.Agent.Wpf.ViewModels;

namespace Tms.Agent.Wpf
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private System.Windows.Forms.NotifyIcon? _notifyIcon;
        private bool _isExiting = false;
        private string _balloonTargetView = string.Empty;

        private readonly System.Collections.Generic.HashSet<int> _dismissedBroadcastIds = new();
        private readonly System.Collections.Generic.HashSet<int> _activeBroadcastIds = new();
        private NotificationWindow? _activeUpdateNotificationWindow;

        public MainWindow()
        {
            InitializeComponent();
            var vm = new MainViewModel();
            DataContext = vm;

            // Check if launched with --startup (silent mode on Windows boot/restart)
            string[] args = Environment.GetCommandLineArgs();
            if (args != null && args.Any(a => string.Equals(a, "--startup", StringComparison.OrdinalIgnoreCase)))
            {
                vm.IsSilentMode = true;
            }

            if (!string.IsNullOrEmpty(App.LoggedInUser))
            {
                vm.ApplyUserAuthentication(App.LoggedInUser, App.UserRole);
            }

            vm.UpdateDetected += (companyName, versionNumber) =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    try { _activeUpdateNotificationWindow?.Close(); } catch { }

                    var profile = vm.Profiles.FirstOrDefault(p => p.ProfileName == companyName);
                    _activeUpdateNotificationWindow = new NotificationWindow(
                        $"Νέα έκδοση ({versionNumber}) για '{companyName}'!",
                        "Εγκεκριμένη αναβάθμιση. Πατήστε Εκτέλεση για να ξεκινήσει ο οδηγός εγκατάστασης.",
                        "Εκτέλεση",
                        "Διαθέσιμη Αναβάθμιση",
                        "⚙️",
                        "#10B981",
                        () =>
                        {
                            ShowWindow();
                            vm.CurrentView = "Dashboard";
                            if (profile != null && vm.UpdateProfileCommand.CanExecute(profile))
                            {
                                vm.UpdateProfileCommand.Execute(profile);
                            }
                        }
                    );
                    _activeUpdateNotificationWindow.Show();
                });
            };

            vm.OperatorClosePromptDetected += (companyName, versionNumber, duration) =>
            {
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    try { _activeUpdateNotificationWindow?.Close(); } catch { }

                    var profile = vm.Profiles.FirstOrDefault(p => p.ProfileName == companyName);
                    _activeUpdateNotificationWindow = new NotificationWindow(
                        $"Εκκρεμεί εγκατάσταση (v{versionNumber}) για '{companyName}'!",
                        "Παρακαλώ κλείστε το ERP (τιμολόγηση) και πατήστε Εκτέλεση για να ολοκληρωθεί η εγκατάσταση.",
                        "Εκτέλεση",
                        "Εκκρεμεί Εγκατάσταση",
                        "⚠️",
                        "#F59E0B",
                        () =>
                        {
                            ShowWindow();
                            vm.CurrentView = "Dashboard";
                            if (profile != null && vm.UpdateProfileCommand.CanExecute(profile))
                            {
                                vm.UpdateProfileCommand.Execute(profile);
                            }
                        }
                    );
                    _activeUpdateNotificationWindow.Show();
                });
            };

            vm.BroadcastDetected += (id, title, content) =>
            {
                if (_dismissedBroadcastIds.Contains(id) || _activeBroadcastIds.Contains(id))
                {
                    return;
                }

                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    bool clickedView = false;
                    var notificationWin = new NotificationWindow(title, content, () =>
                    {
                        clickedView = true;
                        ShowWindow();
                        if (DataContext is MainViewModel mainVm)
                        {
                            mainVm.MarkBroadcastAsRead(id);
                            mainVm.CurrentView = "Broadcasts";
                        }
                    });

                    _activeBroadcastIds.Add(id);

                    notificationWin.Closed += (s, ev) =>
                    {
                        _activeBroadcastIds.Remove(id);
                        _dismissedBroadcastIds.Add(id);
                        if (DataContext is MainViewModel mainVm)
                        {
                            mainVm.MarkBroadcastAsRead(id);
                        }
                    };

                    notificationWin.Show();
                });
            };

            vm.SupportTicketUpdated += (subject, description, reply) =>
            {
                _balloonTargetView = "Support";
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    _notifyIcon?.ShowBalloonTip(5000, $"✉️ Ενημέρωση Αιτήματος Support", $"Θέμα: {subject}\n{description}", System.Windows.Forms.ToolTipIcon.Info);
                });
            };

            // Setup periodic check timer (every 2 minutes)
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(2)
            };
            timer.Tick += (s, e) =>
            {
                if (vm.CheckUpdatesCommand.CanExecute(null))
                {
                    vm.CheckUpdatesCommand.Execute(null);
                }
            };
            timer.Start();

            // Run check immediately on startup
            if (vm.CheckUpdatesCommand.CanExecute(null))
            {
                vm.CheckUpdatesCommand.Execute(null);
            }

            InitializeTrayIcon();

            // Start named event listener to bring this instance to foreground on second launch
            const string EventName = "TmsAgentEvent_CleverData";
            System.Threading.Tasks.Task.Run(() =>
            {
                try
                {
                    using (var eventWaitHandle = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, EventName))
                    {
                        while (true)
                        {
                            eventWaitHandle.WaitOne();
                            System.Windows.Application.Current.Dispatcher.Invoke(() =>
                            {
                                ShowWindow();
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error in single instance event wait loop: {ex.Message}");
                }
            });
        }

        private void InitializeTrayIcon()
        {
            try
            {
                System.Drawing.Icon? icon = null;
                try
                {
                    var uri = new Uri("pack://application:,,,/logo.png");
                    var streamInfo = System.Windows.Application.GetResourceStream(uri);
                    if (streamInfo != null)
                    {
                        using (var stream = streamInfo.Stream)
                        using (var bitmap = new System.Drawing.Bitmap(stream))
                        {
                            var hIcon = bitmap.GetHicon();
                            icon = System.Drawing.Icon.FromHandle(hIcon);
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load custom tray icon: {ex.Message}");
                    icon = System.Drawing.SystemIcons.Shield;
                }

                _notifyIcon = new System.Windows.Forms.NotifyIcon
                {
                    Icon = icon ?? System.Drawing.SystemIcons.Shield,
                    Text = "TMS Agent Panel - Διαχείριση Ενημερώσεων",
                    Visible = true
                };

                _notifyIcon.DoubleClick += (s, e) => ShowWindow();
                
                _notifyIcon.BalloonTipClicked += (s, e) => 
                {
                    ShowWindow();
                    if (DataContext is MainViewModel vm)
                    {
                        if (!string.IsNullOrEmpty(_balloonTargetView))
                        {
                            vm.CurrentView = _balloonTargetView;
                            if (_balloonTargetView == "Broadcasts")
                            {
                                vm.MarkBroadcastsAsRead();
                            }
                        }
                    }
                };

                var contextMenu = new System.Windows.Forms.ContextMenuStrip();
                contextMenu.Items.Add("Άνοιγμα Πάνελ", null, (s, e) => ShowWindow());
                contextMenu.Items.Add("Έλεγχος Ενημερώσεων", null, (s, e) => CheckUpdates());
                contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                contextMenu.Items.Add("Γράψτε ένα αίτημα support", null, (s, e) => WriteSupportRequest());
                contextMenu.Items.Add("Εκδόσεις Desktop Προγράμματος", null, (s, e) => ShowDesktopVersions());
                contextMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                contextMenu.Items.Add("Έξοδος", null, (s, e) => ExitApplication());
                
                _notifyIcon.ContextMenuStrip = contextMenu;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Σφάλμα αρχικοποίησης Tray Icon: {ex.Message}", "Προειδοποίηση", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ShowWindow()
        {
            if (string.IsNullOrEmpty(App.LoggedInUser))
            {
                var loginWindow = new LoginWindow();
                if (loginWindow.ShowDialog() == true)
                {
                    App.LoggedInUser = loginWindow.LoggedInUser;
                    App.UserRole = loginWindow.UserRole;
                    if (DataContext is MainViewModel vm)
                    {
                        vm.ApplyUserAuthentication(App.LoggedInUser, App.UserRole);
                    }
                }
                else
                {
                    return;
                }
            }

            Show();
            WindowState = WindowState.Normal;
            Activate();
        }

        private void CheckUpdates()
        {
            ShowWindow();
            if (DataContext is MainViewModel vm)
            {
                if (vm.CheckUpdatesCommand.CanExecute(null))
                {
                    vm.CheckUpdatesCommand.Execute(null);
                }
            }
        }

        private void ExitApplication()
        {
            _isExiting = true;
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
            }
        }

        public void ShowTrayBalloon(string title, string message, System.Windows.Forms.ToolTipIcon icon = System.Windows.Forms.ToolTipIcon.Info)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                _notifyIcon?.ShowBalloonTip(3000, title, message, icon);
            });
        }

        public void UpdateTrayTooltip(string text)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                if (_notifyIcon != null)
                {
                    _notifyIcon.Text = text.Length > 63 ? text.Substring(0, 63) : text;
                }
            });
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_isExiting)
            {
                e.Cancel = true;
                Hide();

                // Reset logged in user so next show requires login!
                App.LoggedInUser = string.Empty;
                App.UserRole = string.Empty;
                if (DataContext is MainViewModel vm)
                {
                    vm.ApplyUserAuthentication(string.Empty, string.Empty);
                }

                ShowTrayBalloon(
                    "TMS Agent",
                    "Η εφαρμογή TMS Agent Panel θα συνεχίσει να εκτελείται στη γραμμή εργασιών (System Tray) για αυτόματη λήψη ενημερώσεων.",
                    System.Windows.Forms.ToolTipIcon.Info
                );
            }
            else
            {
                base.OnClosing(e);
            }
        }

        private void WriteSupportRequest()
        {
            ShowWindow();
            if (DataContext is MainViewModel vm)
            {
                vm.CurrentView = "Support";
            }
        }

        private void ShowDesktopVersions()
        {
            if (DataContext is MainViewModel vm)
            {
                var summary = "TMS Agent - Εκδόσεις Desktop Προγράμματος:\n\n";
                if (vm.Profiles != null && vm.Profiles.Any())
                {
                    foreach (var p in vm.Profiles)
                    {
                        summary += $"• {p.ProfileName}: v{p.CurrentVersion}\n";
                    }
                }
                else
                {
                    summary += "Δεν βρέθηκαν καταχωρημένα εταιρικά προφίλ.";
                }
                System.Windows.MessageBox.Show(summary, "Εκδόσεις Desktop Προγράμματος", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}