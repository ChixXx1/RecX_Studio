using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace RecX_Studio.Utils
{
    public static class TimeInputBehavior
    {
        // Словарь для хранения таймеров для каждого TextBox
        private static readonly Dictionary<TextBox, DispatcherTimer> _debounceTimers = new Dictionary<TextBox, DispatcherTimer>();

        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsEnabled",
                typeof(bool),
                typeof(TimeInputBehavior),
                new PropertyMetadata(false, OnIsEnabledChanged));

        public static bool GetIsEnabled(DependencyObject obj)
        {
            return (bool)obj.GetValue(IsEnabledProperty);
        }

        public static void SetIsEnabled(DependencyObject obj, bool value)
        {
            obj.SetValue(IsEnabledProperty, value);
        }

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is TextBox textBox)
            {
                if ((bool)e.NewValue)
                {
                    // Подписываемся на события
                    textBox.PreviewTextInput -= OnPreviewTextInput;
                    textBox.PreviewKeyDown += OnPreviewKeyDown;
                    DataObject.AddPastingHandler(textBox, OnPaste);
                    textBox.TextChanged += OnTextChanged;

                    // Создаем и сохраняем таймер для этого TextBox
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) }; // Пауза в 0.8 сек
                    timer.Tick += (s, args) =>
                    {
                        timer.Stop();
                        FormatTextBox(textBox); // Вызываем форматирование по истечении таймера
                    };
                    _debounceTimers[textBox] = timer;
                }
                else
                {
                    // Отписываемся и очищаем ресурсы
                    textBox.PreviewTextInput -= OnPreviewTextInput;
                    textBox.PreviewKeyDown -= OnPreviewKeyDown;
                    DataObject.RemovePastingHandler(textBox, OnPaste);
                    textBox.TextChanged -= OnTextChanged;

                    if (_debounceTimers.TryGetValue(textBox, out var timer))
                    {
                        timer.Stop();
                        _debounceTimers.Remove(textBox);
                    }
                }
            }
        }

        private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            // Разрешаем любой ввод. Фильтрация и форматирование будет в OnTextChanged.
            e.Handled = false;
        }

        private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Блокируем только пробел
            if (e.Key == Key.Space)
            {
                e.Handled = true;
            }
        }

        private static void OnPaste(object sender, DataObjectPastingEventArgs e)
        {
            // Отменяем стандартную вставку, чтобы обработать ее самостоятельно
            e.CancelCommand();
            
            if (e.DataObject.GetDataPresent(DataFormats.Text))
            {
                var text = e.DataObject.GetData(DataFormats.Text) as string;
                if (!string.IsNullOrEmpty(text))
                {
                    var textBox = sender as TextBox;
                    if (textBox != null)
                    {
                        var formattedText = FormatTimeText(text);
                        var caretIndex = textBox.CaretIndex;
                        var selectionLength = textBox.SelectionLength;

                        textBox.Text = textBox.Text.Remove(caretIndex, selectionLength).Insert(caretIndex, formattedText);
                        textBox.CaretIndex = caretIndex + formattedText.Length;
                    }
                }
            }
        }

        // --- КЛЮЧЕВОЕ ИСПРАВЛЕНИЕ: Новая логика в OnTextChanged ---
        private static void OnTextChanged(object sender, TextChangedEventArgs e)
        {
            var textBox = sender as TextBox;
            if (textBox == null || !_debounceTimers.TryGetValue(textBox, out var timer)) return;

            var change = e.Changes.First(); // Берем первое изменение

            // Проверяем, является ли изменение простым добавлением одного символа в конец
            bool isSimpleDigitAddition = change.AddedLength == 1 &&               // Добавлен 1 символ
                                          change.RemovedLength == 0 &&               // Ничего не удалено
                                          change.Offset == textBox.Text.Length - change.AddedLength && // Добавлено в самый конец
                                          char.IsDigit(textBox.Text.Last());         // И этот символ - цифра

            if (isSimpleDigitAddition)
            {
                // Пользователь набирает последовательность цифр. Перезапускаем таймер.
                // Форматирование не происходит.
                timer.Stop();
                timer.Start();
            }
            else
            {
                // Это более сложное изменение (вставка, удаление и т.д.).
                // Форматируем немедленно.
                timer.Stop();
                FormatTextBox(textBox);
            }
        }
        // ----------------------------------------------------

        // --- НОВЫЙ МЕТОД: Централизованная логика форматирования ---
        private static void FormatTextBox(TextBox textBox)
        {
            // Используем Tag, чтобы избежать рекурсивного вызова этого метода
            if (textBox.Tag is string && textBox.Tag.ToString() == "Formatting") return;
            textBox.Tag = "Formatting";

            var caretIndex = textBox.CaretIndex;
            var formattedText = FormatTimeText(textBox.Text);

            if (textBox.Text != formattedText)
            {
                textBox.Text = formattedText;
                // После полного форматирования логично ставить курсор в конец
                textBox.CaretIndex = formattedText.Length;
            }

            // Снимаем флаг
            textBox.Tag = null;
        }
        // ----------------------------------------------------

        private static string FormatTimeText(string input)
        {
            // Извлекаем все цифры из введенного текста
            var digits = Regex.Replace(input ?? "", @"\D", "");

            if (string.IsNullOrEmpty(digits))
            {
                return "";
            }

            // Ограничиваем до 6 цифр (для ЧЧ:ММ:СС)
            if (digits.Length > 6)
            {
                digits = digits.Substring(0, 6);
            }

            // Форматируем в ЧЧ:ММ:СС
            digits = digits.PadLeft(6, '0');

            return $"{digits.Substring(0, 2)}:{digits.Substring(2, 2)}:{digits.Substring(4, 2)}";
        }
    }
}