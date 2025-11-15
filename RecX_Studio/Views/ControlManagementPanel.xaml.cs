using System.Windows;
using System.Windows.Controls;
using RecX_Studio.ViewModels;

namespace RecX_Studio.Views;

public partial class ControlManagementPanel : UserControl
{
    public ControlManagementPanel()
    {
        InitializeComponent();
    }
    
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainVm)
        {
            mainVm.OpenSettings();
        }
    }
    
    private void OpenEditorButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainVm)
        {
            mainVm.OpenEditor();
        }
    }
}