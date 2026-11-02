using BulkCarnageForever.V10.Core.Model;
using BulkCarnageForever.V10.Desktop.Presentation;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace BulkCarnageForever.V10.Desktop;

public partial class MainWindow
{
    private void BuildDashboard()
    {
        var entries = _entries.Where(x => x.Date == DateOnly.FromDateTime(_day)).ToList();
        var today = BuildCard(_day, entries);
        var overview = Section("Daily command");
        var grid = new Grid(); grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(210) }); grid.ColumnDefinitions.Add(new ColumnDefinition());
        overview.Children.Add(grid);
        var gauge = new NutritionGauge { Width = 190, Height = 190, Value = today.Calories, Target = _preferences.Calories, Foreground = Palette("PrimaryText"), Accent = Palette("Lime"), Track = Palette("TrackBackground") };
        System.Windows.Automation.AutomationProperties.SetName(gauge, $"Calories: {today.Calories:N0} of {_preferences.Calories:N0} kilocalories"); grid.Children.Add(gauge);
        var macros = new StackPanel { Margin = new Thickness(12, 0, 0, 0) }; Grid.SetColumn(macros, 1); grid.Children.Add(macros);
        macros.Children.Add(Label($"Target {_preferences.Calories:N0} kcal   ·   {(_preferences.Calories >= today.Calories ? $"{_preferences.Calories - today.Calories:N0} remaining" : $"{today.Calories - _preferences.Calories:N0} over target")}", 18));
        foreach (var (name, value, target, color) in new[] { ("Protein", today.Protein, _preferences.Protein, "Teal"), ("Carbs", today.Carbs, _preferences.Carbs, "Amber"), ("Fat", today.Fat, _preferences.Fat, "Violet") })
        {
            var line = new DockPanel();
            var amount = Label($"{value:0.#} / {target:0.#} g", 12); amount.TextAlignment = TextAlignment.Right; DockPanel.SetDock(amount, Dock.Right); line.Children.Add(amount);
            line.Children.Add(Label(name.ToUpperInvariant(), 11, color)); macros.Children.Add(line);
            macros.Children.Add(new ProgressBar { Height = 9, Maximum = 100, Value = Math.Clamp(value / target * 100, 0, 100), Foreground = Palette(color), Background = Palette("TrackBackground"), Margin = new Thickness(0, 0, 0, 14) });
        }
        if (today.UnknownCount > 0) overview.Children.Add(Label("Known totals only · Some logged nutrition is unavailable.", 12, "Muted"));
        var quick = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        quick.Children.Add(Action("＋ Log food", () => { Navigate("Food Log"); FoodSearch.Focus(); }, true));
        quick.Children.Add(Action("Food database", () => Navigate("Food Database")));
        quick.Children.Add(Action("TDEE & goals", () => Navigate("Goals")));
        quick.Children.Add(Action("Discover your intake", () => Navigate("Reports"))); SectionContent.Children.Add(quick);

        var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition()); SectionContent.Children.Add(row);
        var mealCard = Card(row, "Your meals"); mealCard.Margin = new Thickness(0, 0, 12, 16); var meals = (StackPanel)mealCard.Child;
        foreach (var meal in Enum.GetValues<MealType>())
        {
            var total = BuildCard(_day, entries.Where(x => x.MealType == meal).ToList());
            var content = new DockPanel();
            var image = new Image { Source = ThemeManager.MealArtwork(meal), Width = 44, Height = 44, Stretch = Stretch.UniformToFill, Margin = new Thickness(0, 0, 12, 0), ToolTip = "Meal illustration" };
            content.Children.Add(image);
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center }; text.Children.Add(Label(meal.ToString(), 14));
            text.Children.Add(Label($"{total.EntryCount} foods · {total.Calories:N0} kcal" + (total.UnknownCount > 0 ? " · incomplete" : ""), 11, "Muted")); content.Children.Add(text);
            var button = Action("", () => { Navigate("Food Log"); MealInput.SelectedItem = meal; FoodSearch.Focus(); });
            button.Padding = new Thickness(10, 4, 10, 4); button.Content = content; button.HorizontalAlignment = HorizontalAlignment.Stretch; button.HorizontalContentAlignment = HorizontalAlignment.Stretch; meals.Children.Add(button);
        }
        var progressCard = Card(row, "Weight progress"); Grid.SetColumn(progressCard, 1); var progress = (StackPanel)progressCard.Child;
        var weights = _preferences.Weights.Where(x => x.Date <= DateOnly.FromDateTime(_day) && x.Date >= DateOnly.FromDateTime(_day.AddDays(-29))).OrderBy(x => x.Date).ToList();
        AddChart(progress, weights.Select(x => (x.Date.ToDateTime(TimeOnly.MinValue), (double?)DisplayWeight(x.Kg))).ToList(), _weightUnit, true);
        progress.Children.Add(Label("Recorded weights · Last 30 days", 12, "Muted"));
        progress.Children.Add(Action("Open progress", () => Navigate("Progress")));
        progress.Children.Add(Label($"{_preferences.Goal} · TDEE {_preferences.Tdee:N0} kcal", 14));
        progress.Children.Add(Label("Your targets are editable in TDEE & Goals.", 12, "Muted"));
        var trend = Section("Calorie rhythm · Last 7 days"); var days = Period(7);
        AddChart(trend, days.Select(x => (x.Date, x.EntryCount > 0 && x.UnknownCount == 0 ? (double?)x.Calories : null)).ToList(), "kcal");
        trend.Children.Add(Label("Gaps indicate unlogged days or incomplete nutrition.", 12, "Muted"));
    }
}
