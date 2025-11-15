using System.Windows.Controls;
using RecX_Studio.ViewModels;

namespace RecX_Studio.Views;

public partial class ControlPanel : UserControl
{
    public ControlPanel()
    {
        InitializeComponent();
        DataContext = new ControlPanelViewModel();
    }
    public IRecordingController RecordingController => (ControlPanelViewModel)DataContext;
    
    
}