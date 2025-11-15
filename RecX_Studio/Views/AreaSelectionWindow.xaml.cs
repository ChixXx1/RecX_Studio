using System;
using System.Drawing;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using Point = System.Windows.Point;
using Rectangle = System.Drawing.Rectangle;

namespace RecX_Studio.Views;

public partial class AreaSelectionWindow : Window
{
    private Point _startPoint;
    private bool _isSelecting = false;
    private bool _isCompleted = false;

    public Rectangle? SelectedArea { get; private set; }

    public AreaSelectionWindow()
    {
        InitializeComponent();
        
        // Устанавливаем размеры оверлея
        OverlayRect.Width = SystemParameters.PrimaryScreenWidth;
        OverlayRect.Height = SystemParameters.PrimaryScreenHeight;
        
        SetupEventHandlers();
    }

    private void SetupEventHandlers()
    {
        MouseDown += OnMouseDown;
        MouseMove += OnMouseMove;
        MouseUp += OnMouseUp;
        KeyDown += OnKeyDown;
        
        Loaded += OnWindowLoaded;
    }

    private void OnWindowLoaded(object sender, RoutedEventArgs e)
    {
        // Фокус на окно для обработки клавиш
        Focus();
    }

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && !_isCompleted)
        {
            _startPoint = e.GetPosition(this);
            _isSelecting = true;
            InstructionBorder.Visibility = Visibility.Collapsed;
        }
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (_isSelecting && e.LeftButton == MouseButtonState.Pressed && !_isCompleted)
        {
            var currentPoint = e.GetPosition(this);
            UpdateSelectionVisual(_startPoint, currentPoint);
        }
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_isSelecting && e.LeftButton == MouseButtonState.Released && !_isCompleted)
        {
            _isSelecting = false;
            _isCompleted = true;
            
            var endPoint = e.GetPosition(this);
            
            // Создаем Rectangle
            int x = (int)Math.Min(_startPoint.X, endPoint.X);
            int y = (int)Math.Min(_startPoint.Y, endPoint.Y);
            int width = (int)Math.Abs(endPoint.X - _startPoint.X);
            int height = (int)Math.Abs(endPoint.Y - _startPoint.Y);
            
            // Минимальный размер области
            if (width >= 50 && height >= 50)
            {
                SelectedArea = new Rectangle(x, y, width, height);
                
                // Закрываем окно через диспетчер, чтобы избежать конфликтов
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    DialogResult = true;
                }));
            }
            else
            {
                MessageBox.Show("Область слишком мала. Минимальный размер: 50x50 пикселей.", 
                    "Слишком маленькая область", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Warning);
                
                // Сбрасываем состояние
                ResetSelection();
            }
        }
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !_isCompleted)
        {
            _isCompleted = true;
            
            // Закрываем окно через диспетчер
            Dispatcher.BeginInvoke(new Action(() =>
            {
                DialogResult = false;
            }));
        }
    }

    private void UpdateSelectionVisual(Point start, Point end)
    {
        double x = Math.Min(start.X, end.X);
        double y = Math.Min(start.Y, end.Y);
        double width = Math.Abs(end.X - start.X);
        double height = Math.Abs(end.Y - start.Y);

        // Обновляем прямоугольник выделения
        SelectionRect.Width = width;
        SelectionRect.Height = height;
        Canvas.SetLeft(SelectionRect, x);
        Canvas.SetTop(SelectionRect, y);
        SelectionRect.Visibility = Visibility.Visible;

        // Обновляем информацию о размерах
        SizeInfoText.Text = $"{width:F0} × {height:F0}";
        Canvas.SetLeft(SizeInfoBorder, x);
        Canvas.SetTop(SizeInfoBorder, y - 25);
        SizeInfoBorder.Visibility = Visibility.Visible;
    }

    private void ResetSelection()
    {
        _isSelecting = false;
        _isCompleted = false;
        SelectionRect.Visibility = Visibility.Collapsed;
        SizeInfoBorder.Visibility = Visibility.Collapsed;
        InstructionBorder.Visibility = Visibility.Visible;
    }
}