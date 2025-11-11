using System.Globalization;
using System.Windows.Data;

namespace GaussianImageProcessingSystem.Converters
{
    /// <summary>
    /// Конвертер для проверки содержания подстроки в строке
    /// </summary>
    public class StringContainsConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null)
                return false;

            string text = value.ToString();
            string searchText = parameter.ToString();

            return text.Contains(searchText);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
