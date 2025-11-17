using System.Collections.Generic;
using System.Windows;
using RecX_Studio.Services;

namespace RecX_Studio.Views
{
    public partial class WebcamSelectionWindow : Window
    {
        public WebcamCaptureService.WebcamDeviceInfo SelectedWebcam { get; private set; }

        public WebcamSelectionWindow(List<WebcamCaptureService.WebcamDeviceInfo> webcams)
        {
            InitializeComponent();
            WebcamsListBox.ItemsSource = webcams;
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            SelectedWebcam = WebcamsListBox.SelectedItem as WebcamCaptureService.WebcamDeviceInfo;
            DialogResult = true;
            Close();
        }
    }
}