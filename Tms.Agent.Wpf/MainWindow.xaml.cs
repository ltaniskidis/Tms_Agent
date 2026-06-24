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

        public MainWindow()
        {
            InitializeComponent();
            var vm = new MainViewModel();
            DataContext = vm;

            if (!string.IsNullOrEmpty(App.LoggedInUser))
            {
                vm.ApplyUserAuthentication(App.LoggedInUser, App.UserRole);
            }

            vm.UpdateDetected += (companyName, versionNumber) =>
            {
                _balloonTargetView = "Dashboard";
                // Show balloon tip notification
                _notifyIcon?.ShowBalloonTip(3000, "TMS Agent - Νέα Έκδοση", $"Διαθέσιμη νέα έκδοση ({versionNumber}) για την εταιρεία: {companyName}", System.Windows.Forms.ToolTipIcon.Info);

                // Show alert popup
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    System.Windows.MessageBox.Show(
                        $"Ανιχνεύθηκε νέα έκδοση ({versionNumber}) για την εταιρεία '{companyName}'!",
                        "Διαθέσιμη Ενημέρωση",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                });
            };

            vm.BroadcastDetected += (title, content) =>
            {
                _balloonTargetView = "Broadcasts";
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    _notifyIcon?.ShowBalloonTip(5000, $"📢 Νέα Ανακοίνωση: {title}", content, System.Windows.Forms.ToolTipIcon.Info);
                });
            };

            InitializeTrayIcon();
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
            Close();
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_isExiting)
            {
                e.Cancel = true;
                Hide();
                _notifyIcon?.ShowBalloonTip(2000, "TMS Agent", "Ο Agent εκτελείται στο System Tray.", System.Windows.Forms.ToolTipIcon.Info);
            }
            else
            {
                base.OnClosing(e);
            }
        }
    }
}