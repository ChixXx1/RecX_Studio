using System;
using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using RecX_Studio.Models;
using RecX_Studio.ViewModels;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;

namespace RecX_Studio.Views;

public partial class SourcesPanel : UserControl
{
    private Window _sourcesMenuWindow;

    public SourcesPanel()
    {
        InitializeComponent();
    }

    private void AddSource_Click(object sender, RoutedEventArgs e)
    {
        ShowSourcesMenu(sender as Button);
    }

    private void ShowSourcesMenu(Button targetButton)
    {
        if (_sourcesMenuWindow != null)
        {
            _sourcesMenuWindow.Close();
            _sourcesMenuWindow = null;
            return;
        }

        _sourcesMenuWindow = new Window
        {
            Width = 250,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStyle = WindowStyle.None,
            Background = new SolidColorBrush(Color.FromRgb(0x2D, 0x2D, 0x30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x40)),
            BorderThickness = new Thickness(1),
            ShowInTaskbar = false,
            Topmost = true
        };

        var stackPanel = new StackPanel();
        
        AddMenuButton(stackPanel, "Захват экрана", "ScreenCapture");
        AddMenuButton(stackPanel, "Захват области", "AreaCapture");
        //AddMenuButton(stackPanel, "Веб-камера", "Webcam"); // ДОБАВЛЕНО
        AddMenuSeparator(stackPanel);

        _sourcesMenuWindow.Content = stackPanel;
        
        _sourcesMenuWindow.Show();
        _sourcesMenuWindow.Hide();
        
        var buttonPosition = targetButton.PointToScreen(new Point(0, 0));
        _sourcesMenuWindow.Left = buttonPosition.X;
        _sourcesMenuWindow.Top = buttonPosition.Y - _sourcesMenuWindow.ActualHeight;
        
        _sourcesMenuWindow.Show();
        
        _sourcesMenuWindow.Deactivated += OnSourcesMenuWindowDeactivated;
        _sourcesMenuWindow.Closed += OnSourcesMenuWindowClosed;
    }

    private void OnSourcesMenuWindowDeactivated(object sender, EventArgs e)
    {
        if (_sourcesMenuWindow != null && _sourcesMenuWindow.IsLoaded)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_sourcesMenuWindow != null && _sourcesMenuWindow.IsLoaded)
                {
                    _sourcesMenuWindow.Close();
                }
            }), System.Windows.Threading.DispatcherPriority.Background);
        }
    }

    private void OnSourcesMenuWindowClosed(object sender, EventArgs e)
    {
        _sourcesMenuWindow = null;
    }

    private void AddMenuButton(StackPanel panel, string content, string sourceType)
    {
        var button = new Button
        {
            Content = content,
            Style = (Style)FindResource("ZoomButtonStyle"),
            Height = 40,
            Margin = new Thickness(3),
            Tag = sourceType
        };
        button.Click += (s, e) => 
        {
            AddSourceByType(sourceType);
            _sourcesMenuWindow?.Close();
        };
        panel.Children.Add(button);
    }

    private void AddMenuSeparator(StackPanel panel)
    {
        var separator = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x3E, 0x3E, 0x40)),
            Height = 1,
            Margin = new Thickness(0, 4, 0, 4)
        };
        panel.Children.Add(separator);
    }

    private void AddSourceByType(string sourceType)
    {
        if (DataContext is MainViewModel mainVm)
        {
            if (sourceType == "WindowCapture")
            {
                ShowWindowSelectionDialog(mainVm);
            }
            else if (sourceType == "AreaCapture")
            {
                StartAreaSelection(mainVm);
            }
            else if (sourceType == "Webcam") // ДОБАВЛЕНО
            {
                ShowWebcamSelectionDialog(mainVm);
            }
            else
            {
                MediaSource newSource = sourceType switch
                {
                    "ScreenCapture" => new MediaSource("Захват экрана", SourceType.ScreenCapture) { 
                        IsEnabled = true
                    },
                    "AudioInput" => new MediaSource("Захват входного аудиопотока", SourceType.AudioInput) { 
                        IsEnabled = true
                    },
                    "AudioOutput" => new MediaSource("Захват выходного аудиопотока", SourceType.AudioOutput) { 
                        IsEnabled = true
                    },
                    _ => null
                };
        
                if (newSource != null)
                {
                    mainVm.AddSource(newSource);
                }
            }
        }
    }
    
    // --- НОВЫЙ МЕТОД ДЛЯ ВЫБОРА ВЕБ-КАМЕРЫ ---
    private void ShowWebcamSelectionDialog(MainViewModel mainVm)
    {
        var webcams = mainVm.GetAvailableWebcams();
        
        if (webcams.Count == 0)
        {
            MessageBox.Show("Веб-камеры не найдены", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var webcamSelectionDialog = new WebcamSelectionWindow(webcams)
        {
            Owner = Application.Current.MainWindow
        };

        if (webcamSelectionDialog.ShowDialog() == true && webcamSelectionDialog.SelectedWebcam != null)
        {
            var selectedWebcam = webcamSelectionDialog.SelectedWebcam;
            mainVm.AddWebcamSource(selectedWebcam.Index, selectedWebcam.Name);
        }
    }
    // -----------------------------------------
    
    private void StartAreaSelection(MainViewModel mainVm)
    {
        mainVm.StartAreaSelection(area =>
        {
            // Создаем источник с выбранной областью
            var source = new MediaSource($"Область: {area.Width}x{area.Height}", SourceType.AreaCapture)
            {
                IsEnabled = true,
                CaptureArea = area
            };
        
            // Добавляем в главную VM
            Application.Current.Dispatcher.Invoke(() =>
            {
                mainVm.AddSource(source);
            });
        });
    }

    private void ShowWindowSelectionDialog(MainViewModel mainVm)
    {
        var windowSelectionDialog = new WindowSelectionWindow
        {
            Owner = Application.Current.MainWindow
        };

        if (windowSelectionDialog.ShowDialog() == true && windowSelectionDialog.SelectedWindow != null)
        {
            var selectedWindow = windowSelectionDialog.SelectedWindow;
            
            // Добавляем источник через специальный метод
            mainVm.AddWindowSource(selectedWindow.Handle, selectedWindow.Title);
        }
    }

    private void MoveSourceUp_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainVm)
        {
            mainVm.MoveSourceUp();
        }
    }

    private void MoveSourceDown_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainVm)
        {
            mainVm.MoveSourceDown();
        }
    }

    private void RemoveSelectedSource_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel mainVm)
        {
            mainVm.RemoveSelectedSource();
        }
    }

    // Обработчик выбора источника
    private void SourceItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is MediaSource source)
        {
            if (DataContext is MainViewModel mainVm)
            {
                // Просто устанавливаем выбранный источник для визуального выделения.
                // Вся логика захвата теперь зависит от ActiveSource.
                mainVm.SelectedSource = source;
            }
        }
    }

    // Обработчики для CheckBox включения/выключения источников
    private void SourceCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox && checkBox.DataContext is MediaSource source)
        {
            var mainVm = DataContext as MainViewModel;
            // Просто переключаем состояние. MainViewModel сам разберется, что делать дальше.
            mainVm?.ToggleSource(source);
        }
    }

    private void SourceCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (sender is CheckBox checkBox && checkBox.DataContext is MediaSource source)
        {
            var mainVm = DataContext as MainViewModel;
            mainVm?.ToggleSource(source);
        }
    }

    // Обработчик удаления источника через кнопку × (если она есть в шаблоне)
    private void RemoveSource_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is MediaSource source)
        {
            var mainVm = DataContext as MainViewModel;
            mainVm?.RemoveSource(source);
        }
    }
}