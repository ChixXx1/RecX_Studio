using System;
using System.Drawing;
using System.Windows;
using RecX_Studio.Views;

namespace RecX_Studio.Services;

public class AreaSelectionService
{
    public void StartAreaSelection(Action<Rectangle> onAreaSelected)
    {
        try
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                var selectionWindow = new AreaSelectionWindow();
                selectionWindow.Owner = Application.Current.MainWindow;
                selectionWindow.WindowStartupLocation = WindowStartupLocation.CenterOwner;

                bool dialogResult = selectionWindow.ShowDialog() == true;

                if (dialogResult && selectionWindow.SelectedArea.HasValue)
                {
                    onAreaSelected?.Invoke(selectionWindow.SelectedArea.Value);
                }
                // Если DialogResult = false (ESC или закрытие), просто ничего не делаем
            });
        }
        catch (Exception ex)
        {
            // Показываем ошибку в главном потоке
            Application.Current.Dispatcher.Invoke(() =>
            {
                MessageBox.Show($"Ошибка при выборе области: {ex.Message}", "Ошибка", 
                    MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }
    }
}