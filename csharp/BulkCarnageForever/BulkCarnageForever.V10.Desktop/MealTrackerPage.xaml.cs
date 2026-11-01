using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using BulkCarnageForever.V10.Core.Model;
using BulkCarnageForever.V10.DAL;
using Microsoft.EntityFrameworkCore;

namespace BulkCarnageForever.V10.Desktop;

public partial class MealTrackerPage : Page
{
    private List<FoodItem> _foods = [];
    private Dictionary<string, FoodItem> _foodsByName = new(StringComparer.Ordinal);
    private FoodItem? _selectedFood;
    private bool _ready;

    public MealTrackerPage()
    {
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            await using var db = new AppDbContext();
            await db.InitializeAsync();

            _foods = await db.FoodItems
                .AsNoTracking()
                .OrderBy(x => x.RecipeName)
                .ToListAsync();

            _foodsByName = _foods.ToDictionary(
                x => x.RecipeName, StringComparer.Ordinal);

            MealTypeBox.ItemsSource = Enum.GetValues<MealType>();
            MealTypeBox.SelectedItem = MealType.Breakfast;
            DayPicker.SelectedDate = DateTime.Today;

            _ready = true;
            ShowFoodResults();
            await LoadDayAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow mainWindow)
            mainWindow.ShowCatalog();
    }

    private async void DayPicker_SelectedDateChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (!_ready) return;
        try
        {
            await LoadDayAsync();
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private void FoodSearchBox_TextChanged(
        object sender, TextChangedEventArgs e)
    {
        if (_ready) ShowFoodResults();
    }

    private void ShowFoodResults()
    {
        string search = FoodSearchBox.Text.Trim();
        IEnumerable<FoodItem> query = _foods;

        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(x =>
                x.RecipeName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                (x.BrandType?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        FoodResults.ItemsSource = query.Take(50).ToList();
    }

    private void FoodResults_SelectionChanged(
        object sender, SelectionChangedEventArgs e)
    {
        if (FoodResults.SelectedItem is not FoodItem food)
            return;

        _selectedFood = food;
        SelectedFoodText.Text = food.RecipeName;
        UnitText.Text = food.MeasurementType ?? "units";

        ServingText.Text = food.MeasurementServings > 0
            ? $"1 serving = {food.MeasurementServings:0.##} {UnitText.Text}"
            : "This food has no valid serving size.";

        // Start with one full serving, not an arbitrary one-unit amount.
        AmountBox.Text = food.MeasurementServings > 0
            ? food.MeasurementServings.ToString("0.##", CultureInfo.CurrentCulture)
            : "";

        UpdatePreview();
    }

    private void AmountBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_ready) UpdatePreview();
    }

    private bool TryGetAmount(out double amount)
    {
        return double.TryParse(
                   AmountBox.Text,
                   NumberStyles.Float,
                   CultureInfo.CurrentCulture,
                   out amount)
               && double.IsFinite(amount)
               && amount > 0;
    }

    private void UpdatePreview()
    {
        AddButton.IsEnabled = false;

        if (_selectedFood is null)
        {
            PreviewText.Text = "Choose a food to preview its nutrition.";
            return;
        }

        if (_selectedFood.MeasurementServings <= 0)
        {
            PreviewText.Text = "Cannot calculate nutrition: serving size is missing.";
            return;
        }

        if (!TryGetAmount(out double amount))
        {
            PreviewText.Text = "Enter an amount greater than zero.";
            return;
        }

        double servings = amount / _selectedFood.MeasurementServings;
        PreviewText.Text =
            $"{servings:0.##} servings  •  " +
            $"{servings * _selectedFood.CaloriesPerServing:0.#} cal  •  " +
            $"{servings * _selectedFood.Protein:0.#}g protein  •  " +
            $"{servings * _selectedFood.Carbs:0.#}g carbs  •  " +
            $"{servings * _selectedFood.Fats:0.#}g fats";

        AddButton.IsEnabled = true;
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedFood is null || DayPicker.SelectedDate is not DateTime day ||
            MealTypeBox.SelectedItem is not MealType meal ||
            _selectedFood.MeasurementServings <= 0 || !TryGetAmount(out double amount))
            return;

        try
        {
            AddButton.IsEnabled = false;

            await using var db = new AppDbContext();
            db.MealEntries.Add(new MealEntry
            {
                Date = DateOnly.FromDateTime(day),
                MealType = meal,
                FoodName = _selectedFood.RecipeName,
                AmountEaten = amount
            });

            await db.SaveChangesAsync();
            await LoadDayAsync();
            StatusText.Text = $"Added {_selectedFood.RecipeName} to {meal}.";
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            UpdatePreview();
        }
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: long id }) return;

        try
        {
            await using var db = new AppDbContext();
            var entry = await db.MealEntries.FindAsync(id);
            if (entry is null) return;

            db.MealEntries.Remove(entry);
            await db.SaveChangesAsync();
            await LoadDayAsync();
            StatusText.Text = "Meal entry removed.";
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    private async Task LoadDayAsync()
    {
        if (DayPicker.SelectedDate is not DateTime day) return;

        DateOnly date = DateOnly.FromDateTime(day);
        await using var db = new AppDbContext();

        var entries = await db.MealEntries
            .AsNoTracking()
            .Where(x => x.Date == date)
            .OrderBy(x => x.Id)
            .ToListAsync();

        var rows = entries.Select(entry =>
        {
            bool found = _foodsByName.TryGetValue(entry.FoodName, out FoodItem? food);
            bool validServing = found && food!.MeasurementServings > 0;
            double servings = validServing
                ? entry.AmountEaten / food!.MeasurementServings
                : 0;

            double calories = servings * (food?.CaloriesPerServing ?? 0);
            double protein = servings * (food?.Protein ?? 0);
            double carbs = servings * (food?.Carbs ?? 0);
            double fats = servings * (food?.Fats ?? 0);
            string unit = food?.MeasurementType ?? "units";
            string warning = !found ? "  •  food missing from catalog"
                : !validServing ? "  •  serving size missing" : "";

            return new MealDisplayRow
            {
                Id = entry.Id,
                MealType = entry.MealType,
                FoodName = entry.FoodName,
                Details = $"{entry.AmountEaten:0.##} {unit}  •  " +
                          $"{calories:0.#} cal  •  {protein:0.#}g protein{warning}",
                Calories = calories,
                Protein = protein,
                Carbs = carbs,
                Fats = fats
            };
        }).ToList();

        SetMealSection(MealType.Breakfast, rows, BreakfastItems, BreakfastSummary);
        SetMealSection(MealType.Lunch, rows, LunchItems, LunchSummary);
        SetMealSection(MealType.Dinner, rows, DinnerItems, DinnerSummary);
        SetMealSection(MealType.Snack, rows, SnackItems, SnackSummary);

        CaloriesTotal.Text = rows.Sum(x => x.Calories).ToString("0.#");
        ProteinTotal.Text = $"{rows.Sum(x => x.Protein):0.#}g";
        CarbsTotal.Text = $"{rows.Sum(x => x.Carbs):0.#}g";
        FatsTotal.Text = $"{rows.Sum(x => x.Fats):0.#}g";
    }

    private static void SetMealSection(
        MealType type,
        List<MealDisplayRow> allRows,
        ItemsControl list,
        TextBlock summary)
    {
        var rows = allRows.Where(x => x.MealType == type).ToList();
        list.ItemsSource = rows;
        string label = rows.Count == 1 ? "food" : "foods";
        summary.Text = $"{rows.Count} {label}  •  {rows.Sum(x => x.Calories):0.#} cal";
    }

    private void ShowError(Exception ex)
    {
        MessageBox.Show(Window.GetWindow(this), ex.Message, "Meal Tracker Error",
            MessageBoxButton.OK, MessageBoxImage.Error);
    }
}

public sealed class MealDisplayRow
{
    public long Id { get; init; }
    public MealType MealType { get; init; }
    public string FoodName { get; init; } = "";
    public string Details { get; init; } = "";
    public double Calories { get; init; }
    public double Protein { get; init; }
    public double Carbs { get; init; }
    public double Fats { get; init; }
}
