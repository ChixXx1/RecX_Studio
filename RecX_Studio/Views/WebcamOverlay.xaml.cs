using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Point = System.Windows.Point;

namespace RecX_Studio.Views
{
    public partial class WebcamOverlay : UserControl
    {
        private bool _isDragging = false;
        private Point _startPoint;
        private Point _offset;

        public WebcamOverlay()
        {
            InitializeComponent();
        }

        private void WebcamBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _startPoint = e.GetPosition(this);
            _offset = new Point(Canvas.GetLeft(this), Canvas.GetTop(this));
            CaptureMouse();
        }

        private void WebcamBorder_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isDragging)
            {
                Point currentPoint = e.GetPosition(Parent as UIElement);
                Canvas.SetLeft(this, currentPoint.X - _startPoint.X);
                Canvas.SetTop(this, currentPoint.Y - _startPoint.Y);
            }
        }

        private void WebcamBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging = false;
            ReleaseMouseCapture();
        }
    }
}