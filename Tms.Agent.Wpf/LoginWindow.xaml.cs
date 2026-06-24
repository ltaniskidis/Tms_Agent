using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Tms.Shared.Models;
using Tms.Agent.Core.Services;

namespace Tms.Agent.Wpf
{
    /// <summary>
    /// Interaction logic for LoginWindow.xaml
    /// </summary>
    public partial class LoginWindow : Window
    {
        public string LoggedInUser { get; private set; } = string.Empty;
        public string UserRole { get; private set; } = string.Empty;
        private readonly SettingsManager _settingsManager = new();

        public LoginWindow()
        {
            InitializeComponent();
            UsernameInput.Focus();

            try
            {
                var settings = _settingsManager.LoadSettings();
                if (settings.RememberMe)
                {
                    UsernameInput.Text = settings.SavedUsername ?? string.Empty;
                    PasswordInput.Password = settings.SavedPassword ?? string.Empty;
                    PasswordTextInput.Text = settings.SavedPassword ?? string.Empty;
                    RememberMeCheckbox.IsChecked = true;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load saved login credentials: {ex.Message}");
            }
        }

        private bool _isPasswordVisible = false;

        private void TogglePasswordButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isPasswordVisible)
            {
                // Hide password
                PasswordInput.Password = PasswordTextInput.Text;
                PasswordTextInput.Visibility = Visibility.Collapsed;
                PasswordInput.Visibility = Visibility.Visible;
                TogglePasswordButton.Content = "👁️";
                _isPasswordVisible = false;
                PasswordInput.Focus();
            }
            else
            {
                // Show password
                PasswordTextInput.Text = PasswordInput.Password;
                PasswordInput.Visibility = Visibility.Collapsed;
                PasswordTextInput.Visibility = Visibility.Visible;
                TogglePasswordButton.Content = "🙈";
                _isPasswordVisible = true;
                PasswordTextInput.Focus();
            }
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            PerformLogin();
        }

        private void Input_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                PerformLogin();
            }
        }

        private void PerformLogin()
        {
            string username = UsernameInput.Text.Trim();
            string password = _isPasswordVisible ? PasswordTextInput.Text : PasswordInput.Password;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ShowError("Παρακαλώ εισάγετε όνομα χρήστη και κωδικό.");
                return;
            }

            var appDataPath = Tms.Agent.Core.Services.PathHelper.GetAgentDataFolder();
            var usersFilePath = Path.Combine(appDataPath, "users.json");
            bool hasOwnerInJson = false;

            // 1. Check local cached users from users.json first
            if (File.Exists(usersFilePath))
            {
                try
                {
                    var json = File.ReadAllText(usersFilePath);
                    var users = JsonSerializer.Deserialize<List<AgentUserDto>>(json);

                    if (users != null)
                    {
                        // Check if owner is present in the json
                        hasOwnerInJson = users.Any(u => u.Username.Equals("owner", StringComparison.OrdinalIgnoreCase));

                        foreach (var user in users)
                        {
                            if (user.Username.Equals(username, StringComparison.OrdinalIgnoreCase) && user.Password == password)
                            {
                                LoggedInUser = user.Username;
                                UserRole = user.Role; // Owner, Admin, or Operator
                                SaveLoginCredentials(username, password);
                                DialogResult = true;
                                Close();
                                return;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to load users.json: {ex.Message}");
                }
            }

            // 2. Fallback: Check for Master Owner bypass login if owner is not synchronized in users.json yet
            if (!hasOwnerInJson && username.Equals("owner", StringComparison.OrdinalIgnoreCase) && password == "clever2026owner")
            {
                LoggedInUser = "Owner";
                UserRole = "Owner";
                SaveLoginCredentials(username, password);
                DialogResult = true;
                Close();
                return;
            }

            // If we are here, authentication failed. Customize the error message if users.json does not exist.
            if (!File.Exists(usersFilePath))
            {
                ShowError("Δεν υπάρχουν συγχρονισμένοι χρήστες.\nΣυνδεθείτε ως 'owner' με τον master κωδικό.");
            }
            else
            {
                ShowError("Λάθος Στοιχεία Χρήστη");
            }
        }

        private void SaveLoginCredentials(string username, string password)
        {
            try
            {
                var settings = _settingsManager.LoadSettings();
                if (RememberMeCheckbox.IsChecked == true)
                {
                    settings.RememberMe = true;
                    settings.SavedUsername = username;
                    settings.SavedPassword = password;
                }
                else
                {
                    settings.RememberMe = false;
                    settings.SavedUsername = string.Empty;
                    settings.SavedPassword = string.Empty;
                }
                _settingsManager.SaveSettings(settings);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save login credentials: {ex.Message}");
            }
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorBorder.Visibility = Visibility.Visible;
        }
    }
}
