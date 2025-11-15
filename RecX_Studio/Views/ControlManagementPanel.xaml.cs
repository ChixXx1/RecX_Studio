using System.Windows;
using System.Windows.Controls;

namespace RecX_Studio.Views;

public partial class ControlManagementPanel : UserControl
{
    public ControlManagementPanel()
    {
        InitializeComponent();
    }
    
    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel mainVm)
        {
            mainVm.OpenSettings();
        }
    }
    
    private void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel mainVm)
        {
            mainVm.ToggleRecording();
        }
    }

    // НОВЫЙ ОБРАБОТЧИК СОБЫТИЯ
    private void OpenEditorButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel mainVm)
        {
            mainVm.OpenEditor();
        }
    }
}