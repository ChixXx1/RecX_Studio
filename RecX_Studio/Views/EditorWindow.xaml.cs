using System;
using System.Windows;
using RecX_Studio.ViewModels;

namespace RecX_Studio.Views
{
    public partial class EditorWindow : Window
    {
        private EditorViewModel _viewModel;

        public EditorWindow(string videoPath)
        {
            InitializeComponent();
            
            _viewModel = new EditorViewModel(videoPath, PlayerMediaElement);
            this.DataContext = _viewModel;

            this.Closing += EditorWindow_Closing;
        }

        private void EditorWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _viewModel.Cleanup();
        }
    }
}