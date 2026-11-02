using BulkCarnageForever.V10.Core.Model;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace BulkCarnageForever.V10.Desktop;

public partial class MainWindow
{
    private List<UserProfile> _profiles = new();
    private UserProfile? _profile;
    private List<NutritionGoal> _goals = new();
    private NutritionGoal? _estimateInput;
    private bool _switching;
    private string LastProfilePath => Path.Combine(Path.GetDirectoryName(_preferencesPath)!, "app-settings.json");

    private long ReadLastProfile()
    {
        try { return JsonSerializer.Deserialize<LocalAppSettings>(File.ReadAllText(LastProfilePath))?.LastProfileId ?? 0; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return 0; }
    }

    private void RememberProfile()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LastProfilePath)!);
            File.WriteAllText(LastProfilePath + ".tmp", JsonSerializer.Serialize(new LocalAppSettings { LastProfileId = _profile!.Id }));
            File.Move(LastProfilePath + ".tmp", LastProfilePath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { StatusText.Text = $"Profile is open, but the last-used selection could not be remembered: {ex.Message}"; }
    }

    private void ShowProfileSetup()
    {
        ApplicationShell.IsEnabled = false;
        SetupOverlay.Visibility = Visibility.Visible;
        SetupContent.Children.Clear();
        SetupContent.Children.Add(new Image { Source = Presentation.ThemeManager.Artwork("carnage-pro"), Height = 120, Stretch = System.Windows.Media.Stretch.UniformToFill, Margin = new Thickness(0, 0, 0, 20) });
        SetupContent.Children.Add(new Image { Source = (System.Windows.Media.ImageSource)Resources["BrandLogo"], Width = 88, Height = 88, Margin = new Thickness(0, 20, 0, 20) });
        SetupContent.Children.Add(Label(_profile is null ? "Your battle station starts here." : "Add a local profile", 28));
        SetupContent.Children.Add(Label("One device. Your own meals, progress, goals, and theme. No login needed.", 14, "Muted"));
        var name = Field(SetupContent, "Profile name", ""); name.MaxLength = 80;
        var theme = Choice(SetupContent, "Theme", new[] { "Carnage Pro", "Neon Nights" }, "Carnage Pro");
        SetupContent.Children.Add(Label("You can add height, birthday, and goals in Settings. Weight displays in pounds by default.", 12, "Muted"));
        var error = Label("", 13, "Lime"); SetupContent.Children.Add(error);
        Wpf.Ui.Controls.Button? create = null;
        create = Action("Create profile & start", async () =>
        {
            create!.IsEnabled = false;
            try { await CreateProfileAsync(name.Text, theme.SelectedItem as string ?? "Carnage Pro"); }
            catch (Exception ex) { error.Text = $"Could not create profile: {ex.Message}"; }
            finally { create.IsEnabled = true; }
        }, true);
        SetupContent.Children.Add(create);
        if (_profile is not null) SetupContent.Children.Add(Action("Cancel", () => { SetupOverlay.Visibility = Visibility.Collapsed; ApplicationShell.IsEnabled = true; }));
        name.Focus();
    }

    internal async Task<long> CreateProfileAsync(string name, string theme = "CarnagePro")
    {
        name = name.Trim();
        if (name.Length is < 1 or > 80) throw new ArgumentException("Enter a profile name from 1 to 80 characters.");
        if (_profiles.Any(x => string.Equals(x.DisplayName, name, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("That profile name is already in use.");
        theme = Presentation.ThemeManager.Normalize(theme);
        if (_busy || _switching) throw new InvalidOperationException("Wait for the current action to finish.");
        bool first = _profiles.Count == 0;
        var legacy = new JournalPreferences();
        if (first && File.Exists(_preferencesPath))
        {
            try { legacy = JsonSerializer.Deserialize<JournalPreferences>(await File.ReadAllTextAsync(_preferencesPath)) ?? new(); legacy.Normalize(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            { StatusText.Text = "Previous preferences could not be read. The new profile will use starting defaults."; }
        }
        await using var db = _database();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var profile = new UserProfile { DisplayName = name };
        db.UserProfiles.Add(profile); await db.SaveChangesAsync();
        db.UserPreferences.Add(new UserPreferences { UserProfileId = profile.Id, Theme = theme, WeightUnit = "lb", HeightUnit = "ft-in", LastMeal = legacy.LastMeal });
        db.NutritionGoals.Add(new NutritionGoal { UserProfileId = profile.Id, EffectiveFrom = DateOnly.MinValue,
            Calories = legacy.Calories, Protein = legacy.Protein, Carbs = legacy.Carbs, Fats = legacy.Fat,
            Tdee = legacy.Tdee, CalorieAdjustment = legacy.Adjustment, Goal = Enum.TryParse<GoalType>(legacy.Goal, out var g) ? g : GoalType.Custom });
        if (first)
        {
            db.FoodFavorites.AddRange(legacy.Favorites.Select(x => new FoodFavorite { UserProfileId = profile.Id, FoodName = x }));
            db.WeightEntries.AddRange(legacy.Weights.Select(x => new WeightEntry { UserProfileId = profile.Id, Date = x.Date, WeightKg = x.Kg }));
            await db.MealEntries.Where(x => x.UserProfileId == 0).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.UserProfileId, profile.Id));
        }
        await db.SaveChangesAsync(); await transaction.CommitAsync();
        _profiles = await db.UserProfiles.AsNoTracking().OrderBy(x => x.Id).ToListAsync();
        await SwitchProfileAsync(profile.Id);
        return profile.Id;
    }

    internal async Task SwitchProfileAsync(long id)
    {
        if (_busy || _switching) return;
        var selected = _profiles.FirstOrDefault(x => x.Id == id);
        if (selected is null) return;
        _switching = true; _searchTimer?.Stop(); _foodRequest++; _libraryRequest++;
        ApplicationShell.IsEnabled = false;
        LoadingText.Text = $"Opening {selected.DisplayName}'s battle station…";
        LoadingOverlay.Visibility = Visibility.Visible;
        try
        {
            await using var db = _database();
            var pref = await db.UserPreferences.AsNoTracking().SingleAsync(x => x.UserProfileId == id);
            var goals = await db.NutritionGoals.AsNoTracking().Where(x => x.UserProfileId == id).OrderBy(x => x.EffectiveFrom).ToListAsync();
            var weights = await db.WeightEntries.AsNoTracking().Where(x => x.UserProfileId == id).OrderBy(x => x.Date).ToListAsync();
            var favorites = await db.FoodFavorites.AsNoTracking().Where(x => x.UserProfileId == id).Select(x => x.FoodName).ToListAsync();
            var entries = await db.MealEntries.AsNoTracking().Where(x => x.UserProfileId == id).OrderBy(x => x.Id).ToListAsync();
            await CacheEntryFoodsAsync(db, entries);
            _profile = selected; _goals = goals; _entries = entries; _weightUnit = pref.WeightUnit == "kg" ? "kg" : "lb";
            _preferences = new JournalPreferences { Theme = pref.Theme, MatureArtworkEnabled = pref.MatureArtworkEnabled, BackgroundPreset = pref.BackgroundPreset, LastMeal = pref.LastMeal, Favorites = favorites,
                Weights = weights.Select(x => new JournalWeightEntry { Date = x.Date, Kg = x.WeightKg }).ToList() };
            _preferences.Normalize(); ApplyGoalForDate(); ApplyTheme(_preferences.Theme);
            _estimateInput = null; _selected = null; _lastAdded = null; UndoButton.Visibility = Visibility.Collapsed;
            _updating = true;
            ProfileInput.ItemsSource = _profiles; ProfileInput.SelectedItem = selected;
            FoodSearch.Clear(); QuantityInput.Text = "1"; FavoritesOnly.IsChecked = false;
            MealInput.SelectedItem = _preferences.LastMeal;
            CategoryInput.SelectedIndex = 0; await LoadBrandsAsync();
            _updating = false; _ready = true;
            _libraryPage = 0; FillTargetInputs(); Navigate("Dashboard"); UpdatePreview();
            SetupOverlay.Visibility = Visibility.Collapsed;
            RememberProfile(); StatusText.Text = $"{selected.DisplayName} · {_foodCount:N0} foods ready · Saved locally.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not switch profile: {ex.Message}";
            if (_profile is null) throw;
        }
        finally
        {
            _switching = false; _updating = false;
            ProfileInput.SelectedItem = _profile;
            LoadingOverlay.Visibility = Visibility.Collapsed;
            ApplicationShell.IsEnabled = _profile is not null;
        }
    }

    private NutritionGoal? GoalFor(DateTime date) => _goals.LastOrDefault(x => x.EffectiveFrom <= DateOnly.FromDateTime(date));
    private void ApplyGoalForDate()
    {
        var goal = GoalFor(_day);
        if (goal is null) return;
        (_preferences.Calories, _preferences.Protein, _preferences.Carbs, _preferences.Fat) = (goal.Calories, goal.Protein, goal.Carbs, goal.Fats);
        (_preferences.Tdee, _preferences.Adjustment, _preferences.Goal) = (goal.Tdee, goal.CalorieAdjustment, goal.Goal.ToString());
    }

    private bool SavePreferences(bool saveGoal = false)
    {
        if (_profile is null || _switching) return false;
        try
        {
            using var db = _database(); using var transaction = db.Database.BeginTransaction();
            var pref = db.UserPreferences.Single(x => x.UserProfileId == _profile.Id);
            pref.MatureArtworkEnabled = _preferences.MatureArtworkEnabled; pref.BackgroundPreset = _preferences.BackgroundPreset;
            pref.Theme = Presentation.ThemeManager.Normalize(_preferences.Theme, pref.MatureArtworkEnabled); pref.LastMeal = _preferences.LastMeal; pref.WeightUnit = _weightUnit; pref.HeightUnit = "ft-in";
            // Only changed records are updated. A theme change never recreates weights or favorites.
            var favorites = _preferences.Favorites.ToHashSet(StringComparer.Ordinal);
            var savedFavorites = db.FoodFavorites.Where(x => x.UserProfileId == _profile.Id).ToList();
            db.FoodFavorites.RemoveRange(savedFavorites.Where(x => !favorites.Contains(x.FoodName)));
            var savedNames = savedFavorites.Select(x => x.FoodName).ToHashSet(StringComparer.Ordinal);
            db.FoodFavorites.AddRange(favorites.Where(x => !savedNames.Contains(x)).Select(x => new FoodFavorite { UserProfileId = _profile.Id, FoodName = x }));
            var weights = _preferences.Weights.ToDictionary(x => x.Date);
            var savedWeights = db.WeightEntries.Where(x => x.UserProfileId == _profile.Id).ToList();
            db.WeightEntries.RemoveRange(savedWeights.Where(x => !weights.ContainsKey(x.Date)));
            foreach (var record in savedWeights.Where(x => weights.ContainsKey(x.Date))) record.WeightKg = weights[record.Date].Kg;
            var savedDates = savedWeights.Select(x => x.Date).ToHashSet();
            db.WeightEntries.AddRange(_preferences.Weights.Where(x => !savedDates.Contains(x.Date)).Select(x => new WeightEntry { UserProfileId = _profile.Id, Date = x.Date, WeightKg = x.Kg }));
            if (saveGoal)
            {
                var date = DateOnly.FromDateTime(_day);
                var goal = db.NutritionGoals.SingleOrDefault(x => x.UserProfileId == _profile.Id && x.EffectiveFrom == date);
                if (goal is null) { goal = new NutritionGoal { UserProfileId = _profile.Id, EffectiveFrom = date }; db.NutritionGoals.Add(goal); }
                goal.Calories = _preferences.Calories; goal.Protein = _preferences.Protein; goal.Carbs = _preferences.Carbs; goal.Fats = _preferences.Fat;
                goal.Tdee = _preferences.Tdee; goal.CalorieAdjustment = _preferences.Adjustment;
                goal.Goal = Enum.TryParse<GoalType>(_preferences.Goal, out var type) ? type : GoalType.Custom;
                var estimate = _estimateInput ?? GoalFor(_day);
                bool estimated = estimate is not null && estimate.TdeeMethod == TdeeMethod.MifflinStJeor && Math.Abs(estimate.Tdee - goal.Tdee) < .01;
                goal.TdeeMethod = estimated ? TdeeMethod.MifflinStJeor : TdeeMethod.Manual;
                goal.AgeYears = estimated ? estimate!.AgeYears : null; goal.WeightKg = estimated ? estimate!.WeightKg : null;
                goal.HeightCm = estimated ? estimate!.HeightCm : null; goal.IsMale = estimated ? estimate!.IsMale : null;
                goal.ActivityFactor = estimated ? estimate!.ActivityFactor : null;
            }
            db.SaveChanges(); transaction.Commit();
            if (saveGoal) _goals = db.NutritionGoals.AsNoTracking().Where(x => x.UserProfileId == _profile.Id).OrderBy(x => x.EffectiveFrom).ToList();
            return true;
        }
        catch (Exception ex) { StatusText.Text = $"Profile settings could not be saved: {ex.Message}"; return false; }
    }

    private string _weightUnit = "lb";
    private double DisplayWeight(double kg) => _weightUnit == "lb" ? kg * 2.2046226218 : kg;
    private double StoredWeight(double value) => _weightUnit == "lb" ? value / 2.2046226218 : value;
    internal static double HeightCm(double feet, double inches) => (feet * 12 + inches) * 2.54;
    private int? ProfileAge()
    {
        if (_profile?.DateOfBirth is not DateOnly birth) return null;
        var today = DateOnly.FromDateTime(DateTime.Today); int age = today.Year - birth.Year;
        if (birth > today.AddYears(-age)) age--; return age;
    }

    private void AddProfileSettings()
    {
        if (_profile is null) return;
        var panel = Section("Local profile");
        var name = Field(panel, "Display name", _profile.DisplayName); name.MaxLength = 80;
        panel.Children.Add(Label("Birthday (optional)", 12, "Muted"));
        var birth = new DatePicker { SelectedDate = _profile.DateOfBirth?.ToDateTime(TimeOnly.MinValue), Width = 220,
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 14), DisplayDateEnd = DateTime.Today };
        panel.Children.Add(birth);
        double? total = _profile.HeightCm / 2.54;
        var feet = Field(panel, "Height — feet (optional)", total.HasValue ? Format(Math.Floor(total.Value / 12)) : "");
        var inches = Field(panel, "Height — inches", total.HasValue ? Format(total.Value % 12) : "");
        var unit = Choice(panel, "Weight display unit", new[] { "lb", "kg" }, _weightUnit);
        panel.Children.Add(Action("Save profile", () =>
        {
            string value = name.Text.Trim();
            if (value.Length is < 1 or > 80 || _profiles.Any(x => x.Id != _profile.Id && string.Equals(x.DisplayName, value, StringComparison.OrdinalIgnoreCase)))
            { StatusText.Text = "Enter a unique profile name, up to 80 characters."; return; }
            double? cm = null;
            if (feet.Text.Length > 0 || inches.Text.Length > 0)
            {
                if (!Number(feet.Text, out var ft) || ft < 0 || ft > 8 || ft != Math.Floor(ft) || !Number(inches.Text.Length == 0 ? "0" : inches.Text, out var inch) || inch < 0 || inch >= 12 || HeightCm(ft, inch) is < 80 or > 260)
                { StatusText.Text = "Check height: whole feet and inches from 0 up to 12 (exclusive)."; return; }
                cm = HeightCm(ft, inch);
            }
            try
            {
                using var db = _database(); var record = db.UserProfiles.Single(x => x.Id == _profile.Id);
                record.DisplayName = value; record.DateOfBirth = birth.SelectedDate is DateTime date ? DateOnly.FromDateTime(date) : null;
                record.HeightCm = cm;
                var pref = db.UserPreferences.Single(x => x.UserProfileId == record.Id); pref.WeightUnit = (string)unit.SelectedItem;
                db.SaveChanges();
                _profile.DisplayName = value; _profile.DateOfBirth = record.DateOfBirth; _profile.HeightCm = cm; _weightUnit = pref.WeightUnit;
                _updating = true; ProfileInput.ItemsSource = null; ProfileInput.ItemsSource = _profiles; ProfileInput.SelectedItem = _profile; _updating = false;
                StatusText.Text = "Profile saved.";
            }
            catch (Exception ex) { StatusText.Text = $"Could not save profile: {ex.Message}"; }
        }, true));
    }

    private async void Profile_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_updating || _switching || ProfileInput.SelectedItem is not UserProfile profile || profile.Id == _profile?.Id) return;
        if (_busy) { _updating = true; ProfileInput.SelectedItem = _profile; _updating = false; StatusText.Text = "Wait for the current save to finish before switching profiles."; return; }
        await SwitchProfileAsync(profile.Id);
    }
    private void NewProfile_Click(object sender, RoutedEventArgs e) { if (!_busy && !_switching) ShowProfileSetup(); }
    private async Task CycleProfile(int direction)
    {
        if (_profiles.Count < 2 || _profile is null) return;
        int index = _profiles.FindIndex(x => x.Id == _profile.Id);
        await SwitchProfileAsync(_profiles[(index + direction + _profiles.Count) % _profiles.Count].Id);
    }
    private async void PreviousProfile_Click(object sender, RoutedEventArgs e) => await CycleProfile(-1);
    private async void NextProfile_Click(object sender, RoutedEventArgs e) => await CycleProfile(1);
    private void Profile_ManipulationStarting(object sender, ManipulationStartingEventArgs e) => e.Mode = ManipulationModes.TranslateX;
    private async void Profile_ManipulationCompleted(object sender, ManipulationCompletedEventArgs e)
    { if (Math.Abs(e.TotalManipulation.Translation.X) >= 80) { e.Handled = true; await CycleProfile(e.TotalManipulation.Translation.X < 0 ? 1 : -1); } }
    private async void RetryLoading_Click(object sender, RoutedEventArgs e)
    { LoadingProgress.Visibility = Visibility.Visible; RetryLoadingButton.Visibility = Visibility.Collapsed; await InitializeJournalAsync(); }
}

public sealed class LocalAppSettings
{
    public long LastProfileId { get; set; }
}
