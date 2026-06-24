using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Tms.Shared.Models;

namespace Tms.Agent.Wpf
{
    /// <summary>
    /// Interaction logic for LoginWindow.xaml
    /// </summary>
    public partial class LoginWindow : Window
    {
        public string LoggedInUser { get; private set; } = string.Empty;
        public string UserRole { get; private set; } = string.Empty;

        public LoginWindow()
        {
            InitializeComponent();
            UsernameInput.Focus();
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
            string password = PasswordInput.Password;

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
            {
                ShowError("Παρακαλώ εισάγετε όνομα χρήστη και κωδικό.");
                return;
            }

            // 1. Check for Master Owner bypass login
            if (username.Equals("owner", StringComparison.OrdinalIgnoreCase) && password == "clever2026owner")
            {
                LoggedInUser = "Owner";
                UserRole = "Owner";
                DialogResult = true;
                Close();
                return;
            }

            // 2. Check local cached users from users.json
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var usersFilePath = Path.Combine(appDataPath, "TmsAgent", "users.json");

            if (!File.Exists(usersFilePath))
            {
                ShowError("Δεν υπάρχουν συγχρονισμένοι χρήστες.\nΣυνδεθείτε ως 'owner' με τον master κωδικό.");
                return;
            }

            try
            {
                var json = File.ReadAllText(usersFilePath);
                var users = JsonSerializer.Deserialize<List<AgentUserDto>>(json);

                if (users != null)
                {
                    foreach (var user in users)
                    {
                        if (user.Username.Equals(username, StringComparison.OrdinalIgnoreCase) && user.Password == password)
                        {
                            LoggedInUser = user.Username;
                            UserRole = user.Role; // Admin or Operator
                            DialogResult = true;
                            Close();
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ShowError($"Σφάλμα ανάγνωσης τοπικών χρηστών: {ex.Message}");
                return;
            }

            ShowError("Λανθασμένο όνομα χρήστη ή κωδικός πρόσβασης.");
        }

        private void ShowError(string message)
        {
            ErrorText.Text = message;
            ErrorBorder.Visibility = Visibility.Visible;
        }
    }
}
