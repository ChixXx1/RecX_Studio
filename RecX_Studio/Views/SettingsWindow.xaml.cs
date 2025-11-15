using System.Windows;
using RecX_Studio.ViewModels;

namespace RecX_Studio.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        
        Owner = Application.Current.MainWindow;
    }
}