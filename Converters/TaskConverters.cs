using System;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;

namespace FluentTaskScheduler.Converters
{
    public class StateToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string state = value?.ToString() ?? "";
            return state switch
            {
                "Running" => "\uE768", // Play
                "Ready" => "\uE73E",   // Accept
                "Disabled" => "\uE71A", // Cancel
                _ => "\uE9CE"          // Help
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class StateToColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            string state = value?.ToString() ?? "";
            var color = state switch
            {
                "Running" => Colors.Green,
                "Ready" => Colors.RoyalBlue,
                "Disabled" => Colors.Gray,
                _ => Colors.Orange
            };
            return new SolidColorBrush(color);
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }

    public class DateTimeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, string language)
        {
            if (value is DateTime dt)
            {
                return dt.ToString("g");
            }
            return "N/A";
        }

        public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotImplementedException();
    }
}
