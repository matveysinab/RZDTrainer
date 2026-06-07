using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace RZDTrainer.Converters
{
    /// <summary>
    /// Преобразует абсолютный путь к файлу картинки в готовое изображение.
    /// Картинка загружается с OnLoad-кэшированием, поэтому файл не блокируется
    /// и его можно спокойно заменить во время работы программы.
    /// Если пути нет или файл не найден — возвращает null (картинка просто не покажется).
    /// </summary>
    public sealed class PathToImageConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var path = value as string;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
                return null;

            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
                bmp.UriSource = new Uri(path, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Преобразует текст сложности ("Лёгкий" / "Средний" / "Сложный") в цвет бейджа.
    /// </summary>
    public sealed class DifficultyToBrushConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var text = (value as string ?? "").Trim().ToLowerInvariant();
            var hex = text switch
            {
                "лёгкий" or "легкий" => "#2E7D32", // зелёный
                "сложный" => "#C62828",            // красный
                "средний" => "#EF6C00",            // оранжевый
                _ => "#607D8B"                     // серо-синий (по умолчанию)
            };
            return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
