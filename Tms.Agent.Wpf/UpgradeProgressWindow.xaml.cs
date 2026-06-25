using System;
using System.ComponentModel;
using System.Windows;
using Tms.Agent.Wpf.ViewModels;

namespace Tms.Agent.Wpf
{
    public partial class UpgradeProgressWindow : Window
    {
        public UpgradeProgressWindow()
        {
            InitializeComponent();
            this.Loaded += UpgradeProgressWindow_Loaded;
        }

        private void UpgradeProgressWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.PropertyChanged += ViewModel_PropertyChanged;
            }
        }

        private void ViewModel_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.WizardStage) || e.PropertyName == nameof(MainViewModel.IsWizardOpen))
            {
                if (DataContext is MainViewModel vm)
                {
                    // Close the progress window if the wizard finishes (stage 3) or is closed
                    if (vm.WizardStage == 3 || !vm.IsWizardOpen)
                    {
                        System.Windows.Application.Current.Dispatcher.Invoke(() =>
                        {
                            vm.PropertyChanged -= ViewModel_PropertyChanged;
                            this.Close();
                        });
                    }
                }
            }
        }
    }
}
