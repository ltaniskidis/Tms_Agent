using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using Tms.Agent.Core.Models;
using Tms.Agent.Core.Services;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using MessageBox = System.Windows.MessageBox;
using Brushes = System.Windows.Media.Brushes;

namespace Tms.Agent.Wpf
{
    /// <summary>
    /// Interaction logic for SetupWizardWindow.xaml
    /// </summary>
    public partial class SetupWizardWindow : Window
    {
        private int _currentStep = 1;
        private readonly UpdateEngine _updateEngine = new();
        private readonly SettingsManager _settingsManager = new();
        private string _tempClientId = string.Empty;

        public SetupWizardWindow()
        {
            InitializeComponent();
            InitializeTempClientId();
            UpdateStepUi();
        }

        private void InitializeTempClientId()
        {
            try
            {
                var appDataPath = PathHelper.GetAgentDataFolder();
                var idFile = Path.Combine(appDataPath, "client_id.txt");
                if (File.Exists(idFile))
                {
                    _tempClientId = File.ReadAllText(idFile).Trim();
                }
                else
                {
                    _tempClientId = Guid.NewGuid().ToString();
                }
            }
            catch
            {
                _tempClientId = Guid.NewGuid().ToString();
            }
        }

        private void UpdateStepUi()
        {
            // Reset visibility of panels
            Step1Panel.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
            Step2Panel.Visibility = _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
            Step3Panel.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;

            // Reset step navigation buttons
            BackButton.IsEnabled = _currentStep > 1;
            NextButton.Visibility = _currentStep < 3 ? Visibility.Visible : Visibility.Collapsed;
            FinishButton.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;

            // Update sidebar indicators styling
            UpdateStepIndicator(Step1Indicator, Step1NumberBorder, _currentStep == 1);
            UpdateStepIndicator(Step2Indicator, Step2NumberBorder, _currentStep == 2);
            UpdateStepIndicator(Step3Indicator, Step3NumberBorder, _currentStep == 3);

            // If we arrived at step 3, populate the summary fields
            if (_currentStep == 3)
            {
                SummaryUrlText.Text = ServerUrlInput.Text.Trim();
                SummaryApiKeyText.Text = string.IsNullOrEmpty(ApiKeyInput.Text.Trim()) ? "-" : ApiKeyInput.Text.Trim();
                SummaryRoleText.Text = GetSelectedRoleFriendlyName();
                SummaryServiceText.Text = StartWithWindowsCheckbox.IsChecked == true ? "Ναι" : "Όχι";
            }
        }

        private void UpdateStepIndicator(System.Windows.Controls.StackPanel indicator, System.Windows.Controls.Border numberBorder, bool isActive)
        {
            if (isActive)
            {
                indicator.Opacity = 1.0;
                numberBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6366F1"));
                foreach (var child in indicator.Children)
                {
                    if (child is System.Windows.Controls.TextBlock tb)
                    {
                        tb.Foreground = Brushes.White;
                        tb.FontWeight = FontWeights.SemiBold;
                    }
                }
            }
            else
            {
                indicator.Opacity = 0.5;
                numberBorder.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2B2B3C"));
                foreach (var child in indicator.Children)
                {
                    if (child is System.Windows.Controls.TextBlock tb)
                    {
                        tb.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A0A0B0"));
                        tb.FontWeight = FontWeights.Normal;
                    }
                }
            }
        }

        private string GetSelectedRole()
        {
            if (RoleSqlServerRadio.IsChecked == true) return "SqlServer";
            if (RoleClientRadio.IsChecked == true) return "Client";
            return "Both";
        }

        private string GetSelectedRoleFriendlyName()
        {
            if (RoleSqlServerRadio.IsChecked == true) return "SQL Server";
            if (RoleClientRadio.IsChecked == true) return "Client (Μόνο)";
            return "SQL Server & Client";
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep > 1)
            {
                _currentStep--;
                UpdateStepUi();
            }
        }

        private void NextButton_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep == 1)
            {
                // Validate URL and API Key
                string url = ServerUrlInput.Text.Trim();
                string apiKey = ApiKeyInput.Text.Trim();

                if (string.IsNullOrEmpty(url))
                {
                    MessageBox.Show("Παρακαλώ εισάγετε τη διεύθυνση URL του Central Server.", "Σφάλμα", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                {
                    MessageBox.Show("Η διεύθυνση URL πρέπει να ξεκινάει με http:// ή https://", "Σφάλμα", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrEmpty(apiKey))
                {
                    MessageBox.Show("Παρακαλώ εισάγετε το API Key πιστοποίησης.", "Σφάλμα", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            if (_currentStep < 3)
            {
                _currentStep++;
                UpdateStepUi();
            }
        }

        private async void TestConnectionButton_Click(object sender, RoutedEventArgs e)
        {
            string url = ServerUrlInput.Text.Trim();
            string apiKey = ApiKeyInput.Text.Trim();
            string role = GetSelectedRole();
            bool startWithWindows = StartWithWindowsCheckbox.IsChecked == true;

            TestConnectionButton.IsEnabled = false;
            LoadingText.Visibility = Visibility.Visible;
            StatusBanner.Visibility = Visibility.Collapsed;

            try
            {
                var response = await Task.Run(() => 
                    _updateEngine.CheckForUpdatesAsync(
                        url,
                        _tempClientId,
                        Environment.MachineName,
                        role,
                        "1.5.18",
                        apiKey,
                        new List<LocalProfile>(),
                        startWithWindows
                    )
                );

                if (response != null)
                {
                    // Success
                    StatusBanner.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#064E3B"));
                    StatusBanner.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#059669"));
                    StatusText.Text = "✅ Επιτυχής σύνδεση με τον διακομιστή! Το API Key είναι έγκυρο.";
                    StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A7F3D0"));
                }
                else
                {
                    // Failure
                    StatusBanner.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#450A0A"));
                    StatusBanner.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
                    StatusText.Text = "❌ Αποτυχία σύνδεσης. Ελέγξτε τη διεύθυνση URL, το API Key ή τη σύνδεσή σας στο δίκτυο.";
                    StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FCA5A5"));
                }
            }
            catch (Exception ex)
            {
                StatusBanner.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#450A0A"));
                StatusBanner.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#DC2626"));
                StatusText.Text = $"❌ Σφάλμα κατά τη σύνδεση: {ex.Message}";
                StatusText.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FCA5A5"));
            }
            finally
            {
                LoadingText.Visibility = Visibility.Collapsed;
                StatusBanner.Visibility = Visibility.Visible;
                TestConnectionButton.IsEnabled = true;
            }
        }

        private void FinishButton_Click(object sender, RoutedEventArgs e)
        {
            // Finalize and Save
            try
            {
                // Ensure the client directory exists
                var appDataPath = PathHelper.GetAgentDataFolder();
                if (!Directory.Exists(appDataPath))
                {
                    Directory.CreateDirectory(appDataPath);
                }

                // Save Client ID
                var idFile = Path.Combine(appDataPath, "client_id.txt");
                File.WriteAllText(idFile, _tempClientId);

                // Save Settings
                var settings = new AgentSettings
                {
                    ServerUrl = ServerUrlInput.Text.Trim(),
                    ApiKey = ApiKeyInput.Text.Trim(),
                    MachineRole = GetSelectedRole(),
                    StartWithWindows = StartWithWindowsCheckbox.IsChecked == true
                };
                _settingsManager.SaveSettings(settings);

                // Apply Start with Windows service loop registration
                bool serviceEnable = settings.StartWithWindows;
                Task.Run(() =>
                {
                    try
                    {
                        ServiceControlHelper.ApplyStartWithWindows(serviceEnable);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to apply startup service configuration: {ex.Message}");
                    }
                });

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Σφάλμα κατά την αποθήκευση των ρυθμίσεων: {ex.Message}", "Σφάλμα", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
