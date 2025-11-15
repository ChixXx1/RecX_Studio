using System.Windows;
using System.Linq;
using RecX_Studio.Services;

namespace RecX_Studio.Views;

public partial class WindowSelectionWindow : Window
{
    public ModernWindowCaptureService.WindowInfo SelectedWindow { get; private set; }
    private readonly ModernWindowCaptureService _windowCaptureService = new ModernWindowCaptureService();

    public WindowSelectionWindow()
    {
        InitializeComponent();
        LoadWindows();
    }

    private void LoadWindows()
    {
        var windows = _windowCaptureService.GetAvailableWindows();
        WindowsListBox.ItemsSource = windows;
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadWindows();
    }

    private void SelectButton_Click(object sender, RoutedEventArgs e)
    {
        SelectedWindow = WindowsListBox.SelectedItem as ModernWindowCaptureService.WindowInfo;
        if (SelectedWindow != null)
        {
            DialogResult = true;
            Close();
        }
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}