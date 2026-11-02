using BulkCarnageForever.V10.Core.Model;
using System;
using System.Globalization;
using System.Windows.Data;

namespace BulkCarnageForever.V10.Desktop.Presentation;

public sealed class MealArtworkConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        ThemeManager.MealArtwork(value is MealType meal ? meal : MealType.Lunch);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
