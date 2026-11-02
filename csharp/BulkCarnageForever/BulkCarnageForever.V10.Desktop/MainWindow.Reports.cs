using BulkCarnageForever.V10.Desktop.Presentation;
using System.Windows.Media;
using BulkCarnageForever.V10.Core.Model;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace BulkCarnageForever.V10.Desktop;

public partial class MainWindow
{
    private int _reportDays = 30;
    private DiscoveryNutrient _reportNutrient = DiscoveryNutrient.Calories;
    private DiscoveryBasis _reportBasis = DiscoveryBasis.TotalConsumed;

    private void BuildReports()
    {
        if (_profile is null) return;
        var end = DateOnly.FromDateTime(_day);
        var start = DateOnly.FromDayNumber(Math.Max(0, end.DayNumber - _reportDays + 1));
        var report = BuildNutritionDiscovery(_entries, _catalog, _profile.Id, start, end, _reportNutrient, _reportBasis);
        string nutrient = _reportNutrient.ToString();
        string unit = _reportNutrient == DiscoveryNutrient.Calories ? "kcal" : "g";
        string nutrientBrush = _reportNutrient switch { DiscoveryNutrient.Protein => "Teal", DiscoveryNutrient.Carbs => "Amber", DiscoveryNutrient.Fat => "Violet", DiscoveryNutrient.Fiber => "FiberBrush", _ => "Lime" };

        var range = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (int days in new[] { 7, 30, 90 })
            range.Children.Add(Action($"Past {days} days", () => { _reportDays = days; RefreshSection(); }, _reportDays == days));
        SectionContent.Children.Add(range);
        SectionContent.Children.Add(Label($"{start:dd MMM yyyy} – {end:dd MMM yyyy} · {_profile.DisplayName}", 12, "Muted"));

        var summary = Section("Your logged intake");
        summary.Children.Add(Label($"{report.LoggedDays} recorded days · {report.EntryCount} food entries · {report.FoodCount} different foods", 16));
        summary.Children.Add(Label(report.AverageCalories is double average
            ? $"Average logged intake: {average:N0} kcal on {report.CompleteCalorieDays} days with complete calorie data. Unlogged days are excluded."
            : "Log foods with calorie information to see an average for complete recorded days.", 12, "Muted"));

        var nutrients = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
        foreach (var metric in Enum.GetValues<DiscoveryNutrient>())
            nutrients.Children.Add(Action(metric.ToString(), () => { _reportNutrient = metric; RefreshSection(); }, metric == _reportNutrient));
        SectionContent.Children.Add(nutrients);
        var basis = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        basis.Children.Add(Action("Total consumed", () => { _reportBasis = DiscoveryBasis.TotalConsumed; RefreshSection(); }, _reportBasis == DiscoveryBasis.TotalConsumed));
        basis.Children.Add(Action("Per serving", () => { _reportBasis = DiscoveryBasis.PerServing; RefreshSection(); }, _reportBasis == DiscoveryBasis.PerServing));
        SectionContent.Children.Add(basis);

        var panel = Section(_reportBasis == DiscoveryBasis.TotalConsumed ? $"Top 5 contributors · {nutrient}" : $"Top 5 per serving · {nutrient}");
        panel.Children.Add(Label(_reportBasis == DiscoveryBasis.TotalConsumed
            ? "Ranked by the amount in your logged portions. Percentages show each food’s share of the known total."
            : "Compared among foods you logged in this period. Serving sizes vary; check the amount listed for each food.", 12, "Muted"));
        if (report.TopFoods.Count == 0)
        {
            panel.Children.Add(Label(report.EntryCount == 0
                ? "No foods logged in this period. Try a longer period or choose another tracking date."
                : $"No positive {nutrient.ToLowerInvariant()} values are available for these logged foods.", 16));
        }
        else
        {
            double largest = report.TopFoods[0].Value;
            for (int i = 0; i < report.TopFoods.Count; i++)
            {
                var food = report.TopFoods[i];
                var row = new Grid { Margin = new Thickness(0, 10, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(32) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
                var rank = Label((i + 1).ToString(), 20, nutrientBrush); row.Children.Add(rank);
                if (_catalog.TryGetValue(food.FoodName, out var catalogFood))
                {
                    var converter = new FoodArtworkConverter();
                    var illustration = new Image { Source = (ImageSource)converter.Convert(catalogFood, typeof(ImageSource), "", System.Globalization.CultureInfo.CurrentCulture),
                        Width = 42, Height = 42, Stretch = Stretch.UniformToFill, ToolTip = "Food illustration", Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Top };
                    Grid.SetColumn(illustration, 1); row.Children.Add(illustration);
                }
                var details = new StackPanel { Margin = new Thickness(0, 0, 12, 0) }; Grid.SetColumn(details, 2); row.Children.Add(details);
                var name = Label(food.FoodName, 16); name.FontWeight = FontWeights.SemiBold; details.Children.Add(name);
                details.Children.Add(Label($"{food.LogCount} log{(food.LogCount == 1 ? "" : "s")} · {food.Servings:0.##} servings consumed · 1 serving = {food.ServingLabel}", 11, "Muted"));
                var amount = new StackPanel(); Grid.SetColumn(amount, 3); row.Children.Add(amount);
                var value = Label(_reportNutrient == DiscoveryNutrient.Calories ? $"{food.Value:N0} kcal" : $"{food.Value:0.#} g", 21, nutrientBrush); value.TextAlignment = TextAlignment.Right; amount.Children.Add(value);
                var caption = Label(_reportBasis == DiscoveryBasis.TotalConsumed
                    ? $"{food.SharePercent:0.#}% of known {nutrient.ToLowerInvariant()}" : "per serving", 11, "Muted");
                caption.TextAlignment = TextAlignment.Right; amount.Children.Add(caption);
                var bar = new ProgressBar { Height = 4, Maximum = 100, Value = _reportBasis == DiscoveryBasis.TotalConsumed ? food.SharePercent : food.Value / largest * 100,
                    Foreground = Palette(nutrientBrush), Background = Palette("TrackBackground"), Margin = new Thickness(0, 0, 0, 12) };
                panel.Children.Add(row); panel.Children.Add(bar);
            }
        }
        if (report.SkippedEntries > 0)
            panel.Children.Add(Label($"{report.SkippedEntries} entr{(report.SkippedEntries == 1 ? "y was" : "ies were")} excluded from this ranking because quantity or {nutrient.ToLowerInvariant()} information was unavailable.", 12, "Muted"));
        if (_reportBasis == DiscoveryBasis.TotalConsumed && report.TopFoods.Count > 0)
            panel.Children.Add(Label($"Known total for the period: {report.KnownTotal:N1} {unit}. Percentages include all foods with usable information, beyond the five shown.", 12, "Muted"));
        else if (_reportBasis == DiscoveryBasis.PerServing && report.TopFoods.Count > 0)
            panel.Children.Add(Label("Bars compare each per-serving amount with the highest shown. They do not represent your total intake.", 12, "Muted"));
        panel.Children.Add(Label("Amounts use your logged portions and current food nutrition.", 11, "Muted"));
        if (_reportNutrient == DiscoveryNutrient.Fiber)
            panel.Children.Add(Label("A zero fiber value may also mean fiber information wasn’t provided.", 11, "Muted"));
    }

    internal static NutritionDiscoveryReport BuildNutritionDiscovery(IEnumerable<MealEntry> entries,
        IReadOnlyDictionary<string, FoodItem> catalog, long profileId, DateOnly start, DateOnly end,
        DiscoveryNutrient nutrient, DiscoveryBasis basis)
    {
        var selected = entries.Where(x => x.UserProfileId == profileId && x.Date >= start && x.Date <= end).ToList();
        var groups = new Dictionary<string, DiscoveryFoodTotal>(StringComparer.Ordinal);
        var days = selected.GroupBy(x => x.Date).ToDictionary(x => x.Key, x => new DiscoveryDay { Entries = x.Count() });
        int skipped = 0; double knownTotal = 0;
        foreach (var entry in selected)
        {
            if (!catalog.TryGetValue(entry.FoodName, out var food)) { skipped++; continue; }
            if (TryDiscoveryAmount(entry, food, DiscoveryNutrient.Calories, out _, out var calories) && double.IsFinite(days[entry.Date].Calories + calories))
            { days[entry.Date].KnownEntries++; days[entry.Date].Calories += calories; }
            if (!TryDiscoveryAmount(entry, food, nutrient, out var servings, out var value)) { skipped++; continue; }
            var group = groups.GetValueOrDefault(entry.FoodName) ?? new DiscoveryFoodTotal { Food = food };
            if (!double.IsFinite(knownTotal + value) || !double.IsFinite(group.Total + value) || !double.IsFinite(group.Servings + servings)) { skipped++; continue; }
            group.Total += value; group.Servings += servings; group.LogCount++; knownTotal += value;
            groups[entry.FoodName] = group;
        }
        var complete = days.Values.Where(x => x.KnownEntries == x.Entries).ToList();
        var ranked = groups.Values.Select(x => new FoodDiscoveryRow(x.Food.RecipeName,
            basis == DiscoveryBasis.TotalConsumed ? x.Total : DiscoveryValue(x.Food, nutrient),
            knownTotal > 0 ? x.Total / knownTotal * 100 : 0, x.Servings, x.LogCount,
            $"{x.Food.MeasurementServings:0.##} {x.Food.MeasurementType ?? "catalog units"}"))
            .Where(x => x.Value > 0).OrderByDescending(x => x.Value).ThenBy(x => x.FoodName, StringComparer.Ordinal).Take(5).ToList();
        return new NutritionDiscoveryReport(selected.Count, selected.Select(x => x.FoodName).Distinct(StringComparer.Ordinal).Count(),
            days.Count, complete.Count, complete.Count == 0 ? null : complete.Sum(x => x.Calories / complete.Count), knownTotal, skipped, ranked);
    }

    private static bool TryDiscoveryAmount(MealEntry entry, FoodItem food, DiscoveryNutrient nutrient, out double servings, out double total)
    {
        servings = total = 0;
        if (!double.IsFinite(entry.AmountEaten) || entry.AmountEaten <= 0 || !double.IsFinite(food.MeasurementServings) || food.MeasurementServings <= 0) return false;
        double value = DiscoveryValue(food, nutrient);
        if (!double.IsFinite(value) || value < 0) return false;
        servings = entry.AmountEaten / food.MeasurementServings; total = servings * value;
        return double.IsFinite(servings) && servings > 0 && double.IsFinite(total);
    }
    private static double DiscoveryValue(FoodItem food, DiscoveryNutrient nutrient) => nutrient switch
    {
        DiscoveryNutrient.Protein => food.Protein, DiscoveryNutrient.Carbs => food.Carbs,
        DiscoveryNutrient.Fat => food.Fats, DiscoveryNutrient.Fiber => food.Fiber, _ => food.CaloriesPerServing
    };
}

internal enum DiscoveryNutrient { Calories, Protein, Carbs, Fat, Fiber }
internal enum DiscoveryBasis { TotalConsumed, PerServing }
internal sealed record NutritionDiscoveryReport(int EntryCount, int FoodCount, int LoggedDays, int CompleteCalorieDays,
    double? AverageCalories, double KnownTotal, int SkippedEntries, IReadOnlyList<FoodDiscoveryRow> TopFoods);
internal sealed record FoodDiscoveryRow(string FoodName, double Value, double SharePercent, double Servings, int LogCount, string ServingLabel);
internal sealed class DiscoveryFoodTotal
{
    public FoodItem Food { get; init; } = new();
    public double Total { get; set; }
    public double Servings { get; set; }
    public int LogCount { get; set; }
}
internal sealed class DiscoveryDay
{
    public int Entries { get; init; }
    public int KnownEntries { get; set; }
    public double Calories { get; set; }
}
