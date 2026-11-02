using BulkCarnageForever.V10.Core.Model;
using BulkCarnageForever.V10.DAL;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace BulkCarnageForever.V10.Desktop;

public partial class MainWindow : Window
{
    public static readonly DependencyProperty LibraryColumnsProperty = DependencyProperty.Register(
        nameof(LibraryColumns), typeof(int), typeof(MainWindow), new PropertyMetadata(3));
    public static readonly DependencyProperty WorkspaceHeightProperty = DependencyProperty.Register(
        nameof(WorkspaceHeight), typeof(double), typeof(MainWindow), new PropertyMetadata(640d));
    public int LibraryColumns
    {
        get => (int)GetValue(LibraryColumnsProperty);
        private set => SetValue(LibraryColumnsProperty, value);
    }
    public double WorkspaceHeight
    {
        get => (double)GetValue(WorkspaceHeightProperty);
        private set => SetValue(WorkspaceHeightProperty, value);
    }
    private void Workspace_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Keep cards usable at laptop widths and use more columns on wide monitors.
        LibraryColumns = Math.Clamp((int)(e.NewSize.Width / 290), 2, 6);
        WorkspaceHeight = Math.Max(640, e.NewSize.Height - 2);
    }
    private readonly Func<AppDbContext> _database;
    private readonly string _preferencesPath;
    private readonly bool _initializeDatabase;
    private JournalPreferences _preferences = new();
    private Dictionary<string, FoodItem> _catalog = new(StringComparer.Ordinal);
    private List<MealEntry> _entries = new();
    private FoodItem? _selected;
    private DateTime _day = DateTime.Today;
    private string _mode = "Daily";
    private bool _ready, _busy, _updating, _library, _started;
    private long? _lastAdded;

    public MainWindow() : this(() => new AppDbContext(), Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BulkCarnageForever")) { }

    // The alternate constructor allows verification against an isolated database.
    public MainWindow(Func<AppDbContext> database, string preferencesDirectory, bool initializeDatabase = true)
    {
        _database = database;
        _preferencesPath = Path.Combine(preferencesDirectory, "journal-preferences.json");
        _initializeDatabase = initializeDatabase;
        InitializeComponent();
        PreviewKeyDown += Window_KeyDown;
        Closed += (_, _) => { _ready = false; _searchTimer?.Stop(); _foodRequest++; _libraryRequest++; };
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await InitializeJournalAsync();

    internal async Task InitializeJournalAsync()
    {
        if (_started) return;
        _started = true;
        LoadingText.Text = "Preparing your battle station…";
        try
        {
            await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            await Task.Run(async () => { await using var db = _database(); if (_initializeDatabase) await db.InitializeAsync(); });
            await using var db = _database();
            _foodCount = await db.FoodItems.CountAsync();
            _updating = true;
            DayPicker.SelectedDate = _day;
            MealInput.ItemsSource = Enum.GetValues<MealType>();
            UnitInput.ItemsSource = new[] { "servings" }; UnitInput.SelectedIndex = 0;
            CategoryInput.ItemsSource = new[] { "All categories" }.Concat(await db.FoodItems.Select(x => x.GroupName ?? "Uncategorized").Distinct().OrderBy(x => x).ToListAsync()).ToList();
            CategoryInput.SelectedIndex = 0;
            await LoadBrandsAsync();
            _profiles = await db.UserProfiles.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
            _updating = false;
            LoadingOverlay.Visibility = Visibility.Collapsed;
            if (_profiles.Count == 0) { ShowProfileSetup(); return; }
            long last = ReadLastProfile();
            await SwitchProfileAsync(_profiles.FirstOrDefault(x => x.Id == last)?.Id ?? _profiles[0].Id);
            StatusText.Text = $"{_foodCount:N0} foods ready · Your data stays on this machine.";
        }
        catch (Exception ex)
        {
            _updating = false; _started = false;
            LoadingText.Text = $"Could not open your battle station: {ex.Message}";
            LoadingProgress.Visibility = Visibility.Collapsed;
            RetryLoadingButton.Visibility = Visibility.Visible;
            LoadingOverlay.Visibility = Visibility.Visible;
        }
    }

    private void FillTargetInputs()
    {
        CalorieTargetInput.Text = _preferences.Calories.ToString(CultureInfo.CurrentCulture);
        ProteinTargetInput.Text = _preferences.Protein.ToString(CultureInfo.CurrentCulture);
        CarbTargetInput.Text = _preferences.Carbs.ToString(CultureInfo.CurrentCulture);
        FatTargetInput.Text = _preferences.Fat.ToString(CultureInfo.CurrentCulture);
    }

    internal void Render()
    {
        if (!_ready || _profile is null) return;
        ApplyGoalForDate();
        var active = (Brush)Resources["AccentSurface"];
        OverviewNav.Background = _library ? Brushes.Transparent : active;
        OverviewNav.Foreground = _library ? Brushes.WhiteSmoke : (Brush)Resources["Lime"];
        OverviewNav.BorderBrush = _library ? Brushes.Transparent : (Brush)Resources["AccentBorder"];
        LibraryNav.Background = _library ? active : Brushes.Transparent;
        LibraryNav.Foreground = _library ? (Brush)Resources["Lime"] : Brushes.WhiteSmoke;
        LibraryNav.BorderBrush = _library ? (Brush)Resources["AccentBorder"] : Brushes.Transparent;
        LibraryFilters.Visibility = _library ? Visibility.Visible : Visibility.Collapsed;
        ViewSelector.Visibility = PeriodNavigation.Visibility = _library ? Visibility.Collapsed : Visibility.Visible;
        PageEyebrow.Text = _library ? "FOOD EXPLORER" : "DAILY NUTRITION";
        PageHeading.Text = _library ? "Find your fuel." : "Fuel your day.";
        PageDescription.Text = _library ? "Explore foods, compare macros, and build your day." : "Log what you eat. Watch your progress. Own your day.";
        LogDateLabel.Text = $"Logging to {_day:dddd, dd MMMM}";
        var selected = _entries.Where(x => x.Date == DateOnly.FromDateTime(_day)).OrderBy(x => x.MealType).ThenBy(x => x.Id).ToList();
        var totals = BuildCard(_day, selected);
        SummaryCards.ItemsSource = new[]
        {
            MakeSummary("CALORIES", totals.Calories, _preferences.Calories, "", "Lime", totals.UnknownCount),
            MakeSummary("PROTEIN", totals.Protein, _preferences.Protein, "g", "Teal", totals.UnknownCount),
            MakeSummary("CARBS", totals.Carbs, _preferences.Carbs, "g", "Amber", totals.UnknownCount),
            MakeSummary("FATS", totals.Fat, _preferences.Fat, "g", "Violet", totals.UnknownCount)
        };
        if (_library)
        {
            MainContent.ContentTemplate = (DataTemplate)Resources["LibraryTemplate"];
            MainContent.Content = _libraryView;
            PeriodTitle.Text = "Food database";
            PeriodSummary.Text = _libraryView.Summary;
        }
        else
        {
            var start = _mode switch
            {
                "Week" => _day.AddDays(-MondayOffset(_day)),
                "Month" => new DateTime(_day.Year, _day.Month, 1),
                _ => _day.AddDays(-1)
            };
            int count = _mode == "Month" ? 42 : _mode == "Week" ? 7 : 3;
            if (_mode == "Month") start = start.AddDays(-MondayOffset(start));
            var groups = _entries.GroupBy(x => x.Date).ToDictionary(x => x.Key, x => x.ToList());
            var cards = Enumerable.Range(0, count).Select(i =>
            {
                var date = start.AddDays(i);
                var rows = groups.GetValueOrDefault(DateOnly.FromDateTime(date)) ?? new();
                return BuildCard(date, rows, _mode != "Month" || date.Month == _day.Month);
            }).ToList();
            MainContent.ContentTemplate = (DataTemplate)Resources[_mode + "Template"];
            MainContent.Content = _mode == "Daily" ? new JournalDailyView
            {
                DateLabel = _day.ToString("dddd, dd MMMM"), Days = cards,
                Meals = Enum.GetValues<MealType>().Select(meal =>
                {
                    var entries = selected.Where(x => x.MealType == meal).ToList();
                    var mealTotal = BuildCard(_day, entries);
                    return new JournalMealCard
                    {
                        Meal = meal, Name = meal.ToString(), AddLabel = $"＋  Add to {meal}",
                        Summary = $"{entries.Count} foods · {mealTotal.Calories:0.#} cal" + (mealTotal.UnknownCount > 0 ? " *" : ""),
                        Entries = entries.Select(x => new JournalEntryRow(x.Id, x.FoodName, EntryDetails(x))).ToList()
                    };
                }).ToList()
            } : cards;
            PeriodTitle.Text = _mode == "Daily" ? "Your daily dashboard" : _mode == "Month" ? _day.ToString("MMMM yyyy") :
                $"{start:dd MMM} – {start.AddDays(count - 1):dd MMM yyyy}";
            var inPeriod = cards.Where(x => x.Opacity == 1).ToList();
            int recorded = inPeriod.Count(x => x.EntryCount > 0);
            PeriodSummary.Text = _mode == "Daily"
                ? $"{selected.Count} foods logged · {totals.Status} · {_day:ddd, dd MMM}"
                : $"{recorded} recorded days · {inPeriod.Count(x => x.IsOver)} over target · Select a day to log food";
        }
        RefreshSection();
    }

    private JournalSummaryCard MakeSummary(string name, double value, double target, string unit, string accent, int unknown)
    {
        string detail = value > target ? $"{value - target:0.#}{unit} over · goal {target:0.#}{unit}" : $"{target - value:0.#}{unit} left · goal {target:0.#}{unit}";
        if (unknown > 0) detail += " · incomplete";
        return new JournalSummaryCard(name, $"{value:0.#}{unit}", detail,
            Math.Clamp(value / target * 100, 0, 100), (Brush)Resources[accent]);
    }

    internal JournalDayCard BuildCard(DateTime date, List<MealEntry> rows, bool inMonth = true)
    {
        double calories = 0, protein = 0, carbs = 0, fat = 0;
        int unknown = 0;
        foreach (var entry in rows)
        {
            if (!_catalog.TryGetValue(entry.FoodName, out var food) || !ValidNutrition(food) ||
                !double.IsFinite(entry.AmountEaten) || entry.AmountEaten <= 0)
            { unknown++; continue; }
            double servings = entry.AmountEaten / food.MeasurementServings;
            calories += servings * food.CaloriesPerServing;
            protein += servings * food.Protein;
            carbs += servings * food.Carbs;
            fat += servings * food.Fats;
        }
        var dayGoal = GoalFor(date);
        double calorieTarget = dayGoal?.Calories ?? _preferences.Calories;
        bool over = calories > calorieTarget;
        string status = rows.Count == 0 ? "No entries" : over ? $"{calories - calorieTarget:N0} kcal over" : $"{calorieTarget - calories:N0} kcal left";
        if (unknown > 0) status = over ? status + " · incomplete" : "Nutrition incomplete";
        return new JournalDayCard
        {
            Date = date, EntryCount = rows.Count, IsOver = over,
            Calories = calories, Protein = protein, Carbs = carbs, Fat = fat, UnknownCount = unknown,
            Heading = date == DateTime.Today ? "TODAY" : date.ToString("dddd").ToUpperInvariant(),
            DateLabel = date.ToString("dd MMM"), ShortDate = date.ToString("dd MMM"),
            CaloriesLabel = rows.Count == 0 ? "—" : $"{calories:N0}",
            TargetLabel = rows.Count == 0 ? "No food logged" : $"of {calorieTarget:N0} kcal" + (unknown > 0 ? " · known total" : ""),
            Percent = Math.Clamp(calories / calorieTarget * 100, 0, 100), Status = status,
            StatusBrush = (Brush)Resources[over ? "Amber" : rows.Count == 0 || unknown > 0 ? "Muted" : "Lime"],
            ProteinLabel = MacroLabel("Protein", protein, dayGoal?.Protein ?? _preferences.Protein, rows.Count),
            CarbsLabel = MacroLabel("Carbs", carbs, dayGoal?.Carbs ?? _preferences.Carbs, rows.Count),
            FatLabel = MacroLabel("Fat", fat, dayGoal?.Fats ?? _preferences.Fat, rows.Count),
            CompactMacros = rows.Count == 0 ? "" : $"P {protein:0} / C {carbs:0} / F {fat:0}g",
            SelectionLabel = date == _day ? "● Selected" : "", Opacity = inMonth ? 1 : 0.45
        };
    }

    private static string MacroLabel(string name, double value, double target, int count) => count == 0 ? $"{name} — / {target:0}g" :
        $"{name} {value:0.#} / {target:0.#}g" + (value > target ? $" · {value - target:0.#}g over" : "");
    private static int MondayOffset(DateTime date) => ((int)date.DayOfWeek + 6) % 7;
    private static bool ValidNutrition(FoodItem food) => food.MeasurementServings > 0 && double.IsFinite(food.MeasurementServings) &&
        new[] { food.CaloriesPerServing, food.Protein, food.Carbs, food.Fats }.All(x => double.IsFinite(x) && x >= 0);

    private string EntryDetails(MealEntry entry)
    {
        if (!_catalog.TryGetValue(entry.FoodName, out var food) || !ValidNutrition(food))
            return $"{entry.MealType} · nutrition unavailable";
        return $"{entry.MealType} · {entry.AmountEaten:0.##} {food.MeasurementType ?? "units"} · {entry.AmountEaten / food.MeasurementServings * food.CaloriesPerServing:0.#} kcal";
    }

    private async void Favorites_Changed(object sender, RoutedEventArgs e) { if (!_updating) { _libraryPage = 0; RefreshSearch(); await RefreshLibraryAsync(); } }
    private void Result_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_ready && !_updating && SearchResults.SelectedItem is JournalFoodResult result) SelectFood(result.Food);
    }
    private void SelectFood(FoodItem food)
    {
        _selected = food;
        _updating = true;
        UnitInput.ItemsSource = new[] { "servings", food.MeasurementType ?? "catalog units" };
        UnitInput.SelectedIndex = 0;
        QuantityInput.Text = "1";
        _updating = false;
        UpdatePreview();
        QuantityInput.Focus();
        QuantityInput.SelectAll();
    }
    private void LibraryFood_Click(object sender, RoutedEventArgs e)
    {
        if (_ready && sender is FrameworkElement { Tag: FoodItem food }) SelectFood(food);
    }
    private void AddMeal_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: MealType meal }) MealInput.SelectedItem = meal;
        FoodSearch.Focus(); FoodSearch.SelectAll();
    }
    private void Search_KeyDown(object sender, KeyEventArgs e)
    {
        if (!_ready || SearchResults.Items.Count == 0) return;
        if (e.Key is Key.Down or Key.Up)
        {
            int index = SearchResults.SelectedIndex;
            _updating = true;
            SearchResults.SelectedIndex = Math.Clamp(index + (e.Key == Key.Down ? 1 : -1), 0, SearchResults.Items.Count - 1);
            SearchResults.ScrollIntoView(SearchResults.SelectedItem);
            _updating = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            var result = SearchResults.SelectedItem as JournalFoodResult ?? (JournalFoodResult)SearchResults.Items[0];
            SelectFood(result.Food);
            e.Handled = true;
        }
    }
    private void Results_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && SearchResults.SelectedItem is JournalFoodResult result)
        { SelectFood(result.Food); e.Handled = true; }
    }
    private async void Quantity_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { e.Handled = true; await LogAsync(); }
    }
    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.L && Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        { Navigate("Food Log"); FoodSearch.Focus(); FoodSearch.SelectAll(); e.Handled = true; }
    }
    private void Quantity_Changed(object sender, TextChangedEventArgs e) { if (_ready && !_updating) UpdatePreview(); }
    private void Unit_Changed(object sender, SelectionChangedEventArgs e) { if (_ready && !_updating) UpdatePreview(); }

    private bool TryAmount(out double amount)
    {
        amount = 0;
        if (_selected is null || !ValidNutrition(_selected) ||
            !double.TryParse(QuantityInput.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double quantity) ||
            !double.IsFinite(quantity) || quantity <= 0) return false;
        amount = UnitInput.SelectedIndex == 0 ? quantity * _selected.MeasurementServings : quantity;
        double servings = amount / _selected.MeasurementServings;
        return double.IsFinite(amount) && amount > 0 &&
            new[] { _selected.CaloriesPerServing, _selected.Protein, _selected.Carbs, _selected.Fats }.All(x => double.IsFinite(x * servings));
    }
    private void UpdatePreview()
    {
        FavoriteButton.IsEnabled = _selected is not null;
        FavoriteButton.Content = _selected is not null && _preferences.Favorites.Contains(_selected.RecipeName) ? "★" : "☆";
        SelectedFoodLabel.Text = _selected?.RecipeName ?? "Choose a food above";
        LogButton.IsEnabled = !_busy && TryAmount(out _);
        if (_selected is null) { NutritionPreview.Text = "One serving is ready by default."; return; }
        if (!ValidNutrition(_selected)) { NutritionPreview.Text = "This food has missing or invalid nutrition/serving data and cannot be logged."; return; }
        if (!TryAmount(out double amount)) { NutritionPreview.Text = "Enter a finite quantity greater than zero."; return; }
        double servings = amount / _selected.MeasurementServings;
        NutritionPreview.Text = $"{amount:0.##} {_selected.MeasurementType ?? "units"} · {servings * _selected.CaloriesPerServing:0.#} kcal\nP {servings * _selected.Protein:0.#}g  /  C {servings * _selected.Carbs:0.#}g  /  F {servings * _selected.Fats:0.#}g";
    }

    private async void Log_Click(object sender, RoutedEventArgs e) => await LogAsync();
    internal async Task LogAsync()
    {
        if (!_ready || _busy || _switching || _profile is null || _selected is null || !TryAmount(out double amount) || MealInput.SelectedItem is not MealType meal) return;
        _busy = true;
        UpdatePreview();
        var entry = new MealEntry { UserProfileId = _profile!.Id, Date = DateOnly.FromDateTime(_day), MealType = meal, FoodName = _selected.RecipeName, AmountEaten = amount };
        try
        {
            await using var db = _database();
            db.MealEntries.Add(entry);
            await db.SaveChangesAsync();
            _entries.Add(entry);
            _lastAdded = entry.Id;
            UndoButton.Visibility = Visibility.Visible;
            _selected = null;
            _updating = true;
            FoodSearch.Clear();
            QuantityInput.Text = "1";
            _updating = false;
            Render();
            RefreshSearch();
            StatusText.Text = $"Logged {entry.FoodName} to {meal} · {entry.Date:dd MMM}.";
            FoodSearch.Focus();
        }
        catch (Exception ex) { StatusText.Text = $"Could not log food: {ex.Message}"; }
        finally { _busy = false; UpdatePreview(); }
    }
    private async void Undo_Click(object sender, RoutedEventArgs e)
    { if (_lastAdded is long id) await RemoveAsync(id, true); }
    private async void Remove_Click(object sender, RoutedEventArgs e)
    { if (sender is FrameworkElement { Tag: long id }) await RemoveAsync(id, false); }
    internal async Task RemoveAsync(long id, bool undo)
    {
        if (_busy || !_ready || _switching || _profile is null) return;
        _busy = true;
        UpdatePreview();
        try
        {
            await using var db = _database();
            var entry = await db.MealEntries.FirstOrDefaultAsync(x => x.Id == id && x.UserProfileId == _profile!.Id);
            if (entry is not null) { db.MealEntries.Remove(entry); await db.SaveChangesAsync(); }
            _entries.RemoveAll(x => x.Id == id);
            if (_lastAdded == id) { _lastAdded = null; UndoButton.Visibility = Visibility.Collapsed; }
            Render(); RefreshSearch();
            StatusText.Text = undo ? "Last log undone." : "Entry removed.";
        }
        catch (Exception ex) { StatusText.Text = $"Could not remove entry: {ex.Message}"; }
        finally { _busy = false; UpdatePreview(); }
    }
    private void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var previous = _preferences.Favorites.ToList();
        if (!_preferences.Favorites.Remove(_selected.RecipeName)) _preferences.Favorites.Add(_selected.RecipeName);
        if (!SavePreferences()) _preferences.Favorites = previous;
        RefreshSearch(); UpdatePreview();
    }
    private void Meal_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _updating || MealInput.SelectedItem is not MealType meal) return;
        _preferences.LastMeal = meal;
        SavePreferences();
    }
    private void Targets_Click(object sender, RoutedEventArgs e) => Navigate("Goals");
    private void SaveTargets_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready || _profile is null) return;
        static bool Parse(string text, out double value) => double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) && double.IsFinite(value) && value > 0;
        if (!Parse(CalorieTargetInput.Text, out double calories) || !Parse(ProteinTargetInput.Text, out double protein) ||
            !Parse(CarbTargetInput.Text, out double carbs) || !Parse(FatTargetInput.Text, out double fat))
        { StatusText.Text = "Each target must be a finite number greater than zero."; return; }
        var old = (_preferences.Calories, _preferences.Protein, _preferences.Carbs, _preferences.Fat);
        var oldGoal = (_preferences.Goal, _preferences.Adjustment);
        (_preferences.Calories, _preferences.Protein, _preferences.Carbs, _preferences.Fat) = (calories, protein, carbs, fat);
        if (calories != old.Calories) { _preferences.Goal = "Custom"; _preferences.Adjustment = calories - _preferences.Tdee; }
        if (!SavePreferences(true))
        { (_preferences.Goal, _preferences.Adjustment) = oldGoal; (_preferences.Calories, _preferences.Protein, _preferences.Carbs, _preferences.Fat) = old; return; }
        Render();
        StatusText.Text = "Targets saved for the selected date and following days.";
    }
    private void Overview_Click(object sender, RoutedEventArgs e) => Navigate("Food Log");
    private void Library_Click(object sender, RoutedEventArgs e) => Navigate("Food Database");
    private void View_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || sender is not RadioButton { Tag: string mode }) return;
        _mode = mode; _page = "Food Log"; _library = false; Render(); RefreshSearch();
    }
    private void Day_Click(object sender, RoutedEventArgs e)
    { if (sender is FrameworkElement { Tag: DateTime date }) SetDay(date); }
    private void Date_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _updating) return;
        if (DayPicker.SelectedDate is DateTime date) { _day = date.Date; _estimateInput = null; ApplyGoalForDate(); FillTargetInputs(); Render(); }
        else { _updating = true; DayPicker.SelectedDate = _day; _updating = false; }
    }
    private void SetDay(DateTime date)
    {
        _day = date.Date; _estimateInput = null; ApplyGoalForDate(); FillTargetInputs();
        _updating = true; DayPicker.SelectedDate = _day; _updating = false;
        Render();
    }
    private void Previous_Click(object sender, RoutedEventArgs e) => MovePeriod(-1);
    private void Next_Click(object sender, RoutedEventArgs e) => MovePeriod(1);
    private void Today_Click(object sender, RoutedEventArgs e) => SetDay(DateTime.Today);
    private void MovePeriod(int direction)
    {
        try { SetDay(_mode == "Month" ? _day.AddMonths(direction) : _day.AddDays(direction * (_mode == "Week" ? 7 : 1))); }
        catch (ArgumentOutOfRangeException) { StatusText.Text = "That date is outside the supported range."; }
    }
}

public sealed class JournalPreferences
{
    public double Calories { get; set; } = 2500;
    public double Protein { get; set; } = 180;
    public double Carbs { get; set; } = 280;
    public double Fat { get; set; } = 75;
    public MealType LastMeal { get; set; } = MealType.Breakfast;
    public List<string> Favorites { get; set; } = new();
    public string Theme { get; set; } = "CarnagePro";
    public bool MatureArtworkEnabled { get; set; }
    public string BackgroundPreset { get; set; } = "Theme";
    public double Tdee { get; set; } = 2900;
    public double Adjustment { get; set; } = -400;
    public string Goal { get; set; } = "Custom";
    public List<JournalWeightEntry> Weights { get; set; } = new();
    public void Normalize()
    {
        Theme = Presentation.ThemeManager.Normalize(Theme, MatureArtworkEnabled);
        if (BackgroundPreset is not ("Theme" or "Food" or "City" or "None")) BackgroundPreset = "Theme";
        if (!double.IsFinite(Tdee) || Tdee <= 0) Tdee = 2900;
        if (!double.IsFinite(Adjustment) || Tdee + Adjustment <= 0) Adjustment = 0;
        if (Goal is not ("Bulk" or "Cut" or "Maintain" or "Custom")) Goal = "Custom";
        Weights = (Weights ?? new()).Where(x => x is not null && double.IsFinite(x.Kg) && x.Kg > 0).GroupBy(x => x.Date).Select(x => x.Last()).OrderBy(x => x.Date).ToList();
        if (!double.IsFinite(Calories) || Calories <= 0) Calories = 2500;
        if (!double.IsFinite(Protein) || Protein <= 0) Protein = 180;
        if (!double.IsFinite(Carbs) || Carbs <= 0) Carbs = 280;
        if (!double.IsFinite(Fat) || Fat <= 0) Fat = 75;
        if (!Enum.IsDefined(LastMeal)) LastMeal = MealType.Breakfast;
        Favorites = (Favorites ?? new()).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).ToList();
    }
}
public sealed record JournalFoodResult(FoodItem Food, string Display);
public sealed record JournalEntryRow(long Id, string FoodName, string Details);
public sealed record JournalSummaryCard(string Name, string Value, string Detail, double Percent, Brush Accent);
public sealed class JournalDailyView
{
    public string DateLabel { get; init; } = "";
    public List<JournalDayCard> Days { get; init; } = new();
    public List<JournalMealCard> Meals { get; init; } = new();
}
public sealed class JournalMealCard
{
    public MealType Meal { get; init; }
    public string Name { get; init; } = "";
    public string Summary { get; init; } = "";
    public string AddLabel { get; init; } = "";
    public List<JournalEntryRow> Entries { get; init; } = new();
    public bool IsEmpty => Entries.Count == 0;
}
public sealed class JournalDayCard
{
    public DateTime Date { get; init; }
    public int EntryCount { get; init; }
    public bool IsOver { get; init; }
    public double Calories { get; init; }
    public double Protein { get; init; }
    public double Carbs { get; init; }
    public double Fat { get; init; }
    public int UnknownCount { get; init; }
    public string DayNumber => Date.ToString("dd");
    public string Heading { get; init; } = "";
    public string DateLabel { get; init; } = "";
    public string ShortDate { get; init; } = "";
    public string CaloriesLabel { get; init; } = "";
    public string TargetLabel { get; init; } = "";
    public double Percent { get; init; }
    public string Status { get; init; } = "";
    public Brush StatusBrush { get; init; } = Brushes.Gray;
    public string ProteinLabel { get; init; } = "";
    public string CarbsLabel { get; init; } = "";
    public string FatLabel { get; init; } = "";
    public string CompactMacros { get; init; } = "";
    public string SelectionLabel { get; init; } = "";
    public bool IsSelected => SelectionLabel.Length > 0;
    public string CalendarTooltip => $"{Date:dddd, dd MMMM yyyy}\n{TargetLabel}\n{Status}\n{ProteinLabel}\n{CarbsLabel}\n{FatLabel}";
    public double Opacity { get; init; } = 1;
}
