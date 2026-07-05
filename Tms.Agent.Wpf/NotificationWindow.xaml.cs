using System;
using System.Windows;

namespace Tms.Agent.Wpf
{
    /// <summary>
    /// Interaction logic for NotificationWindow.xaml
    /// </summary>
    public partial class NotificationWindow : Window
    {
        private readonly Action _onViewAction;

        public NotificationWindow(string title, string message, Action onViewAction)
        {
            InitializeComponent();
            _onViewAction = onViewAction;

            TitleText.Text = title;
            MessageText.Text = message;

            Loaded += NotificationWindow_Loaded;
        }

        public NotificationWindow(string title, string message, string buttonText, string headerText, string iconText, string accentBrushHex, Action onViewAction)
        {
            InitializeComponent();
            _onViewAction = onViewAction;

            TitleText.Text = title;
            MessageText.Text = message;
            ViewButton.Content = buttonText;
            HeaderText.Text = headerText;
            IconText.Text = iconText;

            try
            {
                var converter = new System.Windows.Media.BrushConverter();
                var brush = (System.Windows.Media.Brush?)converter.ConvertFromString(accentBrushHex);
                if (brush != null)
                {
                    AccentBar.Background = brush;
                    ViewButton.Background = brush;
                }
            }
            catch
            {
                // Ignored
            }

            Loaded += NotificationWindow_Loaded;
        }

        private void NotificationWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Position in the bottom right corner of the primary screen, above the taskbar
            var desktopWorkingArea = System.Windows.SystemParameters.WorkArea;
            this.Left = desktopWorkingArea.Right - this.Width - 10;
            this.Top = desktopWorkingArea.Bottom - this.Height - 10;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ViewButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _onViewAction?.Invoke();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to execute notification click callback: {ex.Message}");
            }
            Close();
        }
    }
}
