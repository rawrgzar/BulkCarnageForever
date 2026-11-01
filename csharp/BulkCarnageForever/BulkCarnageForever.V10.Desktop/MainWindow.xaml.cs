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
        nameof(WorkspaceHeight), typeof(double), typeof(MainWindow), new PropertyMetadata(780d));
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
        WorkspaceHeight = Math.Max(780, e.NewSize.Height - 2);
    }
    private readonly Func<AppDbContext> _database;
    private readonly string _preferencesPath;
    private readonly bool _initializeDatabase;
    private JournalPreferences _preferences = new();
    private List<FoodItem> _foods = new();
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
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e) => await InitializeJournalAsync();

    internal async Task InitializeJournalAsync()
    {
        if (_started) return;
        _started = true;
        try
        {
            string? preferencesWarning = null;
            try
            {
                if (File.Exists(_preferencesPath))
                    _preferences = JsonSerializer.Deserialize<JournalPreferences>(await File.ReadAllTextAsync(_preferencesPath)) ?? new();
                _preferences.Normalize();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                _preferences = new();
                preferencesWarning = "Preferences could not be read; defaults are in use.";
            }
            await using var db = _database();
            if (_initializeDatabase) await db.InitializeAsync();
            _foods = await db.FoodItems.AsNoTracking().OrderBy(x => x.RecipeName).ToListAsync();
            _catalog = _foods.ToDictionary(x => x.RecipeName, StringComparer.Ordinal);
            _entries = await db.MealEntries.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
            _updating = true;
            DayPicker.SelectedDate = _day;
            MealInput.ItemsSource = Enum.GetValues<MealType>();
            MealInput.SelectedItem = _preferences.LastMeal;
            UnitInput.ItemsSource = new[] { "servings" };
            UnitInput.SelectedIndex = 0;
            CategoryInput.ItemsSource = new[] { "All categories" }.Concat(_foods.Select(x => x.GroupName ?? "Uncategorized").Distinct().OrderBy(x => x)).ToList();
            CategoryInput.SelectedIndex = 0;
            BrandInput.ItemsSource = new[] { "All brands" }.Concat(_foods.Select(x => x.BrandType ?? "Generic").Distinct().OrderBy(x => x)).ToList();
            BrandInput.SelectedIndex = 0;
            FillTargetInputs();
            _updating = false;
            _ready = true;
            Render();
            RefreshSearch();
            StatusText.Text = preferencesWarning ?? $"{_foods.Count:N0} foods ready · Select a day and start typing. Your data stays on this machine.";
        }
        catch (Exception ex)
        {
            _updating = false;
            StatusText.Text = $"Could not load the journal: {ex.Message}";
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
        if (!_ready) return;
        var active = new SolidColorBrush(Color.FromRgb(39, 64, 48));
        OverviewNav.Background = _library ? Brushes.Transparent : active;
        OverviewNav.Foreground = _library ? Brushes.WhiteSmoke : (Brush)Resources["Lime"];
        OverviewNav.BorderBrush = _library ? Brushes.Transparent : new SolidColorBrush(Color.FromRgb(71, 101, 66));
        LibraryNav.Background = _library ? active : Brushes.Transparent;
        LibraryNav.Foreground = _library ? (Brush)Resources["Lime"] : Brushes.WhiteSmoke;
        LibraryNav.BorderBrush = _library ? new SolidColorBrush(Color.FromRgb(71, 101, 66)) : Brushes.Transparent;
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
            var matches = MatchingFoods(FoodSearch.Text.Trim()).ToList();
            MainContent.Content = matches;
            PeriodTitle.Text = "Food library";
            PeriodSummary.Text = $"{matches.Count:N0} of {_foods.Count:N0} foods · Select a card to log it.";
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
        bool over = calories > _preferences.Calories;
        string status = rows.Count == 0 ? "No entries" : over ? $"{calories - _preferences.Calories:N0} kcal over" : $"{_preferences.Calories - calories:N0} kcal left";
        if (unknown > 0) status = over ? status + " · incomplete" : "Nutrition incomplete";
        return new JournalDayCard
        {
            Date = date, EntryCount = rows.Count, IsOver = over,
            Calories = calories, Protein = protein, Carbs = carbs, Fat = fat, UnknownCount = unknown,
            Heading = date == DateTime.Today ? "TODAY" : date.ToString("dddd").ToUpperInvariant(),
            DateLabel = date.ToString("dd MMM"), ShortDate = date.ToString("dd MMM"),
            CaloriesLabel = rows.Count == 0 ? "—" : $"{calories:N0}",
            TargetLabel = rows.Count == 0 ? "No food logged" : $"of {_preferences.Calories:N0} kcal" + (unknown > 0 ? " · known total" : ""),
            Percent = Math.Clamp(calories / _preferences.Calories * 100, 0, 100), Status = status,
            StatusBrush = new SolidColorBrush(over ? Color.FromRgb(232, 183, 125) :
                rows.Count == 0 || unknown > 0 ? Color.FromRgb(156, 178, 201) : Color.FromRgb(189, 244, 115)),
            ProteinLabel = MacroLabel("Protein", protein, _preferences.Protein, rows.Count),
            CarbsLabel = MacroLabel("Carbs", carbs, _preferences.Carbs, rows.Count),
            FatLabel = MacroLabel("Fat", fat, _preferences.Fat, rows.Count),
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

    private IEnumerable<FoodItem> MatchingFoods(string term) => _foods.Where(x =>
        (!_library || CategoryInput.SelectedItem is not string category || category == "All categories" || (x.GroupName ?? "Uncategorized") == category) &&
        (!_library || BrandInput.SelectedItem is not string brand || brand == "All brands" || (x.BrandType ?? "Generic") == brand) &&
        (term.Length == 0 || x.RecipeName.Contains(term, StringComparison.OrdinalIgnoreCase) ||
        (x.BrandType?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false) ||
        (x.GroupName?.Contains(term, StringComparison.OrdinalIgnoreCase) ?? false)));

    private void Category_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _updating) return;
        _updating = true;
        var category = CategoryInput.SelectedItem as string;
        BrandInput.ItemsSource = new[] { "All brands" }.Concat(_foods
            .Where(x => category == "All categories" || (x.GroupName ?? "Uncategorized") == category)
            .Select(x => x.BrandType ?? "Generic").Distinct().OrderBy(x => x)).ToList();
        BrandInput.SelectedIndex = 0;
        _updating = false;
        RefreshSearch(); Render();
    }
    private void Brand_Changed(object sender, SelectionChangedEventArgs e)
    { if (_ready && !_updating) { RefreshSearch(); Render(); } }

    private void RefreshSearch()
    {
        if (!_ready || _updating) return;
        string term = FoodSearch.Text.Trim();
        var recent = _entries.OrderByDescending(x => x.Id).Select(x => x.FoodName).Distinct().ToList();
        var rank = recent.Select((name, i) => (name, i)).ToDictionary(x => x.name, x => x.i);
        var query = MatchingFoods(term);
        if (FavoritesOnly.IsChecked == true) query = query.Where(x => _preferences.Favorites.Contains(x.RecipeName));
        var results = query.OrderByDescending(x => _preferences.Favorites.Contains(x.RecipeName))
            .ThenBy(x => rank.GetValueOrDefault(x.RecipeName, int.MaxValue))
            .ThenBy(x => x.RecipeName).Take(40)
            .Select(x => new JournalFoodResult(x, (_preferences.Favorites.Contains(x.RecipeName) ? "★ " : "") +
                $"{x.RecipeName} · {x.BrandType ?? "unbranded"} · {x.CaloriesPerServing:0.#} kcal · P {x.Protein:0.#} / C {x.Carbs:0.#} / F {x.Fats:0.#}g")).ToList();
        _updating = true;
        SearchResults.ItemsSource = results;
        SearchResults.SelectedIndex = -1;
        _updating = false;
        ResultsLabel.Text = results.Count == 0 ? "No matching foods" : term.Length == 0 ? "Favorites & recents" : $"{results.Count} matches · max 40";
    }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _updating) return;
        _selected = null;
        RefreshSearch();
        UpdatePreview();
        if (_library) Render();
    }
    private void Favorites_Changed(object sender, RoutedEventArgs e) => RefreshSearch();
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
        { FoodSearch.Focus(); FoodSearch.SelectAll(); e.Handled = true; }
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
        if (!_ready || _busy || _selected is null || !TryAmount(out double amount) || MealInput.SelectedItem is not MealType meal) return;
        _busy = true;
        UpdatePreview();
        var entry = new MealEntry { Date = DateOnly.FromDateTime(_day), MealType = meal, FoodName = _selected.RecipeName, AmountEaten = amount };
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
        if (_busy || !_ready) return;
        _busy = true;
        UpdatePreview();
        try
        {
            await using var db = _database();
            var entry = await db.MealEntries.FindAsync(id);
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
    private void Targets_Click(object sender, RoutedEventArgs e) => TargetsPanel.Visibility =
        TargetsPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    private void SaveTargets_Click(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        static bool Parse(string text, out double value) => double.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) && double.IsFinite(value) && value > 0;
        if (!Parse(CalorieTargetInput.Text, out double calories) || !Parse(ProteinTargetInput.Text, out double protein) ||
            !Parse(CarbTargetInput.Text, out double carbs) || !Parse(FatTargetInput.Text, out double fat))
        { StatusText.Text = "Each target must be a finite number greater than zero."; return; }
        var old = (_preferences.Calories, _preferences.Protein, _preferences.Carbs, _preferences.Fat);
        (_preferences.Calories, _preferences.Protein, _preferences.Carbs, _preferences.Fat) = (calories, protein, carbs, fat);
        if (!SavePreferences())
        { (_preferences.Calories, _preferences.Protein, _preferences.Carbs, _preferences.Fat) = old; return; }
        TargetsPanel.Visibility = Visibility.Collapsed;
        Render();
        StatusText.Text = "Daily targets saved and applied across your journal.";
    }
    private bool SavePreferences()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_preferencesPath)!);
            File.WriteAllText(_preferencesPath + ".tmp", JsonSerializer.Serialize(_preferences, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(_preferencesPath + ".tmp", _preferencesPath, true);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { StatusText.Text = $"Preferences could not be saved: {ex.Message}"; return false; }
    }
    private void Overview_Click(object sender, RoutedEventArgs e) { _library = false; Render(); RefreshSearch(); }
    private void Library_Click(object sender, RoutedEventArgs e) { _library = true; Render(); RefreshSearch(); }
    private void View_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready || sender is not RadioButton { Tag: string mode }) return;
        _mode = mode; _library = false; Render(); RefreshSearch();
    }
    private void Day_Click(object sender, RoutedEventArgs e)
    { if (sender is FrameworkElement { Tag: DateTime date }) SetDay(date); }
    private void Date_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _updating) return;
        if (DayPicker.SelectedDate is DateTime date) { _day = date.Date; Render(); }
        else { _updating = true; DayPicker.SelectedDate = _day; _updating = false; }
    }
    private void SetDay(DateTime date)
    {
        _day = date.Date;
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
    public void Normalize()
    {
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
