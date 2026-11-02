using BulkCarnageForever.V10.Core.Model;
using System;
using System.Globalization;
using System.Windows.Data;

namespace BulkCarnageForever.V10.Desktop.Presentation;

public sealed class FoodArtworkConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var food = value as FoodItem;
        var meal = food?.IsBreakfast == true ? MealType.Breakfast : food?.IsSnack == true ? MealType.Snack : food?.IsDinner == true ? MealType.Dinner : MealType.Lunch;
        return ThemeManager.MealArtwork(meal);
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => Binding.DoNothing;
}
