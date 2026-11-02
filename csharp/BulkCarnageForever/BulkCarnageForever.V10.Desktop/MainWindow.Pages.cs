using BulkCarnageForever.V10.Core.Model;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using Path = System.IO.Path;
using UiButton = Wpf.Ui.Controls.Button;
using UiTextBox = Wpf.Ui.Controls.TextBox;

namespace BulkCarnageForever.V10.Desktop;

// Additional sections share the existing journal and theme resources.
// Kept in a partial class so installation only requires these three UI files.
public partial class MainWindow
{
    private string _page = "Dashboard";
    private string _progressTab = "Weight";
    private Brush Palette(string key) => (Brush)Resources[key];

    private void Page_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string page }) Navigate(page);
    }

    internal void Navigate(string page)
    {
        if (!_ready || _profile is null) return;
        SectionScroller.ScrollToTop();
        _page = page;
        _library = page == "Food Database";
        if (page == "Goals") FillTargetInputs();
        Render();
        RefreshSearch();
        if (_library) _ = RefreshLibraryAsync();
    }

    private void RefreshSection()
    {
        bool journal = _page is "Food Log" or "Food Database";
        JournalWorkspace.Visibility = JournalToolbar.Visibility = journal ? Visibility.Visible : Visibility.Collapsed;
        SectionScroller.Visibility = journal ? Visibility.Collapsed : Visibility.Visible;
        SummaryCards.Visibility = _page == "Food Log" ? Visibility.Visible : Visibility.Collapsed;
        TargetsPanel.Visibility = _page == "Goals" ? Visibility.Visible : Visibility.Collapsed;
        LibraryFilters.Visibility = _library ? Visibility.Visible : Visibility.Collapsed;
        foreach (var (button, page) in new[] { (DashboardNav, "Dashboard"), (OverviewNav, "Food Log"),
            (ProgressNav, "Progress"), (GoalsNav, "Goals"), (LibraryNav, "Food Database"),
            (ReportsNav, "Reports"), (SettingsNav, "Settings") })
        {
            bool selected = page == _page;
            button.Background = selected ? Palette("AccentSurface") : Brushes.Transparent;
            button.Foreground = selected ? Palette("Lime") : Palette("PrimaryText");
            button.BorderBrush = selected ? Palette("AccentBorder") : Brushes.Transparent;
        }
        PageEyebrow.Text = "BULKCARNAGE FOREVER / LOCAL";
        PageHeading.Text = _page == "Dashboard" ? "Battle Station" : _page == "Goals" ? "TDEE & Goals" : _page;
        PageDescription.Text = _page switch
        {
            "Dashboard" => "Calorie Command Center · Your day at a glance.",
            "Food Log" => "Track your fuel. Choose a day, then add food to a meal.",
            "Food Database" => "Browse your local catalog. Select a food to prepare a log entry.",
            "Progress" => "Your recorded weight and nutrition trends, in focus.",
            "Goals" => "Set your daily energy target and macros. Targets take effect on the selected date.",
            "Reports" => "Discover the foods contributing to your intake. Your selected date sets the end of the period.",
            _ => "Make it yours. All settings and journal data stay on this device."
        };
        SectionContent.Children.Clear();
        if (journal) return;
        switch (_page)
        {
            case "Dashboard": BuildDashboard(); break;
            case "Goals": BuildGoals(); break;
            case "Progress": BuildProgress(); break;
            case "Reports": BuildReports(); break;
            case "Settings": BuildSettings(); break;
        }
    }

    internal void ApplyTheme(string theme) => Presentation.ThemeManager.Apply(Resources, theme, _preferences.MatureArtworkEnabled, _preferences.BackgroundPreset);

    private TextBlock Label(string text, double size = 14, string color = "PrimaryText") => new()
    {
        Text = text, FontSize = size, Foreground = Palette(color), TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 10)
    };
    private UiButton Action(string text, Action action, bool primary = false)
    {
        var button = new UiButton { Content = text, Style = (Style)Resources[primary ? "Primary" : "Action"],
            Margin = new Thickness(0, 4, 10, 4), HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += (_, _) => action();
        return button;
    }
    private Border Card(Panel parent, string title)
    {
        var content = new StackPanel();
        var heading = Label(title, 18); heading.FontWeight = FontWeights.SemiBold;
        content.Children.Add(heading);
        var border = new Border { Style = (Style)Resources["Surface"], Child = content, Margin = new Thickness(0, 0, 0, 16) };
        parent.Children.Add(border);
        return border;
    }
    private StackPanel Section(string title) => (StackPanel)Card(SectionContent, title).Child;
    private UiTextBox Field(Panel panel, string label, string value)
    {
        panel.Children.Add(Label(label, 12, "Muted"));
        var box = new UiTextBox { Text = value, Style = (Style)Resources["Input"], Width = 420,
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 14) };
        System.Windows.Automation.AutomationProperties.SetName(box, label);
        panel.Children.Add(box); return box;
    }
    private ComboBox Choice(Panel panel, string label, string[] values, string selected)
    {
        panel.Children.Add(Label(label, 12, "Muted"));
        var box = new ComboBox { ItemsSource = values, SelectedItem = selected, Width = 420, HorizontalAlignment = HorizontalAlignment.Left,
            Style = (Style)Resources["DarkComboBox"], Margin = new Thickness(0, 0, 0, 14) };
        System.Windows.Automation.AutomationProperties.SetName(box, label);
        panel.Children.Add(box); return box;
    }
    private static bool Number(string text, out double value) => double.TryParse(text, NumberStyles.Float,
        CultureInfo.CurrentCulture, out value) && double.IsFinite(value);
    private static string Format(double value) => value.ToString("0.##", CultureInfo.CurrentCulture);

    private List<JournalDayCard> Period(int days) => Enumerable.Range(1 - days, days).Select(offset =>
    {
        var date = _day.AddDays(offset);
        return BuildCard(date, _entries.Where(x => x.Date == DateOnly.FromDateTime(date)).ToList());
    }).ToList();

    private void BuildGoals()
    {
        var modes = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) }; SectionContent.Children.Add(modes);
        var panel = Section("Manual TDEE & calorie adjustment");
        panel.Children.Add(Label("Use your known maintenance intake, then choose a goal. Macro targets are saved above.", 13, "Muted"));
        var tdee = Field(panel, "TDEE (kcal/day)", Format(_preferences.Tdee));
        var goal = Choice(panel, "Goal", new[] { "Bulk", "Cut", "Maintain", "Custom" }, _preferences.Goal);
        var adjustment = Field(panel, "Calorie adjustment (kcal/day, signed)", Format(_preferences.Adjustment));
        var preview = Label($"Target intake: {_preferences.Calories:N0} kcal/day", 22, "Lime");
        panel.Children.Add(preview);
        tdee.TextChanged += (_, _) => Update(); adjustment.TextChanged += (_, _) => Update();
        void Update() => preview.Text = Number(tdee.Text, out var t) && Number(adjustment.Text, out var a) && double.IsFinite(t + a)
            ? $"Target intake: {t + a:N0} kcal/day" : "Enter valid numbers.";
        goal.SelectionChanged += (_, _) => adjustment.Text = (goal.SelectedItem as string) switch
        { "Bulk" => "500", "Cut" => "-300", "Maintain" => "0", _ => adjustment.Text };
        panel.Children.Add(Action("Save TDEE & goal", () =>
        {
            if (!Number(tdee.Text, out var t) || !Number(adjustment.Text, out var a) || t <= 0 || !double.IsFinite(t + a) || t + a <= 0)
            { StatusText.Text = "TDEE and the resulting calorie target must be finite and greater than zero."; return; }
            string g = goal.SelectedItem as string ?? "Custom";
            if ((g == "Bulk" && a <= 0) || (g == "Cut" && a >= 0) || (g == "Maintain" && a != 0)) g = "Custom";
            var old = (_preferences.Tdee, _preferences.Adjustment, _preferences.Calories, _preferences.Goal);
            (_preferences.Tdee, _preferences.Adjustment, _preferences.Calories, _preferences.Goal) = (t, a, t + a, g);
            if (!SavePreferences(true)) { (_preferences.Tdee, _preferences.Adjustment, _preferences.Calories, _preferences.Goal) = old; return; }
            FillTargetInputs(); Render(); StatusText.Text = "TDEE, goal, and daily calorie target saved.";
        }, true));

        var manualCard = (Border)panel.Parent;
        var calculator = Section("TDEE calculator · Estimate");
        var calculatorCard = (Border)calculator.Parent;
        calculator.Children.Add(Label("Mifflin–St Jeor resting-energy estimate × activity factor. Enter height in feet and inches. Review the estimate before using it.", 13, "Muted"));
        var savedEstimate = GoalFor(_day);
        var age = Field(calculator, "Age (years, 18–120)", (savedEstimate?.AgeYears ?? ProfileAge())?.ToString() ?? "");
        var lastWeight = _preferences.Weights.Where(x => x.Date <= DateOnly.FromDateTime(_day)).OrderBy(x => x.Date).LastOrDefault();
        var weight = Field(calculator, $"Weight ({_weightUnit})", lastWeight is null ? savedEstimate?.WeightKg is double savedKg ? Format(DisplayWeight(savedKg)) : "" : Format(DisplayWeight(lastWeight.Kg)));
        double? totalInches = (savedEstimate?.HeightCm ?? _profile?.HeightCm) / 2.54;
        var feet = Field(calculator, "Height — feet", totalInches.HasValue ? Format(Math.Floor(totalInches.Value / 12)) : "");
        var inches = Field(calculator, "Height — inches", totalInches.HasValue ? Format(totalInches.Value % 12) : "");
        var sex = Choice(calculator, "TDEE formula", new[] { "Male", "Female" }, (savedEstimate?.IsMale ?? _profile?.IsMale) == false ? "Female" : "Male");
        string[] levels = { "Sedentary", "Lightly active", "Moderately active", "Very active", "Extra active" };
        var factors = new[] { 1.2, 1.375, 1.55, 1.725, 1.9 };
        int activityIndex = Array.IndexOf(factors, savedEstimate?.ActivityFactor ?? _profile?.ActivityFactor ?? 1.2);
        var activity = Choice(calculator, "Activity level", levels, levels[Math.Max(0, activityIndex)]);
        var result = Label("Enter your details to calculate.", 16, "Lime");
        calculator.Children.Add(result);
        calculator.Children.Add(Action("Calculate estimate", () =>
        {
            if (!Number(age.Text, out var years) || years < 18 || years > 120 || years != Math.Floor(years) ||
                !Number(weight.Text, out var displayKg) || StoredWeight(displayKg) <= 0 || StoredWeight(displayKg) > 700 ||
                !Number(feet.Text, out var ft) || ft < 0 || ft > 8 || ft != Math.Floor(ft) || !Number(inches.Text.Length == 0 ? "0" : inches.Text, out var inch) || inch < 0 || inch >= 12 || HeightCm(ft, inch) < 80 || HeightCm(ft, inch) > 260)
            { result.Text = "Check the age, weight, and height values."; return; }
            double kg = StoredWeight(displayKg), cm = HeightCm(ft, inch);
            double estimate = (10 * kg + 6.25 * cm - 5 * years + (sex.SelectedIndex == 0 ? 5 : -161)) * new[] { 1.2, 1.375, 1.55, 1.725, 1.9 }[activity.SelectedIndex];
            if (estimate <= 0) { result.Text = "These inputs do not produce a usable estimate."; return; }
            _estimateInput = new NutritionGoal { TdeeMethod = TdeeMethod.MifflinStJeor, Tdee = Math.Round(estimate), AgeYears = (int)years, WeightKg = kg, HeightCm = cm, IsMale = sex.SelectedIndex == 0, ActivityFactor = new[] { 1.2, 1.375, 1.55, 1.725, 1.9 }[activity.SelectedIndex] };
            tdee.Text = Format(Math.Round(estimate));
            result.Text = $"Estimated TDEE: {estimate:N0} kcal/day. Copied into the manual field above; save to apply.";
        }));
        UiButton? manualTab = null, calculatorTab = null;
        void ShowMode(bool estimate)
        {
            manualCard.Visibility = estimate ? Visibility.Collapsed : Visibility.Visible;
            calculatorCard.Visibility = estimate ? Visibility.Visible : Visibility.Collapsed;
            manualTab!.Style = (Style)Resources[estimate ? "Action" : "Primary"];
            calculatorTab!.Style = (Style)Resources[estimate ? "Primary" : "Action"];
        }
        manualTab = Action("Manual TDEE", () => ShowMode(false));
        calculatorTab = Action("Calculator estimate", () => ShowMode(true));
        modes.Children.Add(manualTab); modes.Children.Add(calculatorTab);
        calculator.Children.Add(Action("Review target before saving", () => ShowMode(false), true));
        ShowMode(false);
    }

    private void BuildProgress()
    {
        var tabs = new WrapPanel { Margin = new Thickness(0, 0, 0, 12) };
        foreach (string name in new[] { "Weight", "Intake", "Macros" })
            tabs.Children.Add(Action(name, () => { _progressTab = name; RefreshSection(); }, _progressTab == name));
        SectionContent.Children.Add(tabs);
        if (_progressTab == "Weight")
        {
            var panel = Section($"Body weight · {_weightUnit}");
            var values = _preferences.Weights.Where(x => x.Date <= DateOnly.FromDateTime(_day)).OrderBy(x => x.Date).TakeLast(30).ToList();
            if (values.Count > 0)
                panel.Children.Add(Label($"Latest: {DisplayWeight(values[^1].Kg):0.##} {_weightUnit}   ·   Change across shown records: {DisplayWeight(values[^1].Kg - values[0].Kg):+0.##;-0.##;0} {_weightUnit}", 18, "Lime"));
            AddChart(panel, values.Select(x => (x.Date.ToDateTime(TimeOnly.MinValue), (double?)DisplayWeight(x.Kg))).ToList(), _weightUnit);
            panel.Children.Add(Label("Up to 30 records through the selected date. One record per day; saving again updates that day.", 12, "Muted"));
            var existing = _preferences.Weights.FirstOrDefault(x => x.Date == DateOnly.FromDateTime(_day));
            var input = Field(panel, $"Weight for {_day:dd MMM yyyy} ({_weightUnit})", existing is null ? "" : Format(DisplayWeight(existing.Kg)));
            panel.Children.Add(Action("Save weight", () =>
            {
                if (!Number(input.Text, out var entered) || StoredWeight(entered) <= 0 || StoredWeight(entered) > 700) { StatusText.Text = "Enter a valid positive weight (up to 700 kg / 1,543 lb)."; return; }
                double kg = StoredWeight(entered);
                var previous = _preferences.Weights.ToList();
                _preferences.Weights = previous.Where(x => x.Date != DateOnly.FromDateTime(_day)).Append(new JournalWeightEntry { Date = DateOnly.FromDateTime(_day), Kg = kg }).OrderBy(x => x.Date).ToList();
                if (!SavePreferences()) { _preferences.Weights = previous; return; }
                RefreshSection(); StatusText.Text = "Weight saved locally.";
            }, true));
            if (existing is not null) panel.Children.Add(Action("Remove selected day's weight", () =>
            {
                var previous = _preferences.Weights.ToList();
                _preferences.Weights = previous.Where(x => x.Date != existing.Date).ToList();
                if (!SavePreferences()) { _preferences.Weights = previous; return; }
                RefreshSection(); StatusText.Text = "Weight record removed.";
            }));
        }
        else
        {
            var days = Period(30);
            foreach (string metric in _progressTab == "Intake" ? new[] { "Calories" } : new[] { "Protein", "Carbs", "Fat" })
            {
                var panel = Section(metric + " · Last 30 days");
                AddChart(panel, days.Select(x => (x.Date, x.EntryCount == 0 || x.UnknownCount > 0 ? (double?)null : metric switch
                { "Protein" => x.Protein, "Carbs" => x.Carbs, "Fat" => x.Fat, _ => x.Calories })).ToList(), metric == "Calories" ? "kcal" : "g");
            }
            SectionContent.Children.Add(Label("Gaps indicate unlogged days or incomplete nutrition. Dates end on the selected tracking date.", 12, "Muted"));
        }
    }

    private void AddChart(Panel parent, List<(DateTime Date, double? Value)> points, string unit, bool compact = false)
    {
        var recorded = points.Where(x => x.Value.HasValue).ToList();
        if (recorded.Count == 0 && unit is "kg" or "lb") { parent.Children.Add(Label("No records yet. Add entries to see your trend.", 16, "Muted")); return; }
        double canvasWidth = compact ? 480 : 900;
        var canvas = new Canvas { Width = canvasWidth, Height = 240, ClipToBounds = true };
        var viewbox = new Viewbox { Child = canvas, Stretch = Stretch.Uniform, MaxHeight = 300, HorizontalAlignment = HorizontalAlignment.Stretch };
        parent.Children.Add(viewbox);
        var range = ChartRange(recorded.Select(x => x.Value!.Value), unit);
        double min = range.Min, max = range.Max;
        double left = compact ? 56 : 72, top = 12, width = canvasWidth - left - 28, height = 180;
        double span = Math.Max(1, (points[^1].Date - points[0].Date).TotalDays);
        for (double value = min; value <= max + range.Step * .01; value += range.Step)
        {
            double y = top + height - (value - min) / (max - min) * height;
            canvas.Children.Add(new Line { X1 = left, X2 = left + width, Y1 = y, Y2 = y, Stroke = Palette("LineBrush"), StrokeThickness = 1 });
            var tick = Label(unit == "kcal" ? $"{value:N0}" : $"{value:0.#}", 11, "Muted");
            Canvas.SetLeft(tick, 2); Canvas.SetTop(tick, y - 7); canvas.Children.Add(tick);
        }
        Polyline? line = null; Polygon? area = null;
        var accentColor = ((SolidColorBrush)Palette("Lime")).Color;
        var areaBrush = new LinearGradientBrush(Color.FromArgb(65, accentColor.R, accentColor.G, accentColor.B), Colors.Transparent, new Point(0, 0), new Point(0, 1));
        foreach (var point in points)
        {
            if (!point.Value.HasValue) { line = null; area = null; continue; }
            double px = left + (point.Date - points[0].Date).TotalDays / span * width;
            double py = top + height - (point.Value.Value - min) / (max - min) * height;
            if (line is null)
            {
                area = new Polygon { Fill = areaBrush }; area.Points.Add(new Point(px, top + height)); area.Points.Add(new Point(px, top + height)); canvas.Children.Add(area);
                line = new Polyline { Stroke = Palette("Lime"), StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round }; canvas.Children.Add(line);
            }
            area!.Points.RemoveAt(area.Points.Count - 1); area.Points.Add(new Point(px, py)); area.Points.Add(new Point(px, top + height));
            line.Points.Add(new Point(px, py));
            var dot = new Ellipse { Width = 8, Height = 8, Fill = Palette("Lime"), Stroke = Palette("SurfaceRaised"), StrokeThickness = 1.5,
                ToolTip = $"{point.Date:dd MMM yyyy}: {point.Value:0.##} {unit}" };
            Canvas.SetLeft(dot, px - 4); Canvas.SetTop(dot, py - 4); canvas.Children.Add(dot);
        }
        foreach (int index in new[] { 0, points.Count / 2, points.Count - 1 }.Distinct())
        {
            var date = Label(points[index].Date.ToString("dd MMM"), 11, "Muted");
            Canvas.SetLeft(date, Math.Clamp(left + (points[index].Date - points[0].Date).TotalDays / span * width - 16, left, canvasWidth - 58));
            Canvas.SetTop(date, 205); canvas.Children.Add(date);
        }
    }

    internal static (double Min, double Max, double Step) ChartRange(IEnumerable<double> values, string unit)
    {
        var data = values.Where(double.IsFinite).ToList();
        double high = data.Count == 0 ? 0 : data.Max();
        if (unit == "kcal")
        {
            double top = Math.Max(3000, Math.Ceiling(high / 1000) * 1000);
            double step = top <= 8000 ? 1000 : Math.Ceiling(top / 6000) * 1000;
            return (0, Math.Ceiling(top / step) * step, step);
        }
        if (unit == "g") { double top = Math.Max(100, Math.Ceiling(high / 100) * 100); return (0, top, top / 4); }
        double low = data.Count == 0 ? 0 : data.Min();
        double padding = Math.Max((high - low) * .15, Math.Max(Math.Abs(high) * .02, 1));
        low = Math.Max(0, low - padding); high += padding;
        return (low, high, (high - low) / 4);
    }

    private void BuildSettings()
    {
        BuildThemeSettings();
        AddProfileSettings();
        var storage = Section("System & data");
        storage.Children.Add(Label("●  Offline mode · No account or internet required", 16, "Lime"));
        storage.Children.Add(Label("Food and meal data use your existing SQLite database. Each local profile has its own goals, theme, favorites, and weight records in SQLite.", 13, "Muted"));
        storage.Children.Add(Label("Weight display follows this profile’s pounds/kilograms preference. Height is entered in feet and inches. Food quantities use servings or the catalog's native measurement unit.", 13, "Muted"));
        storage.Children.Add(Action("Export this profile (JSON)", async () => await ExportSnapshotAsync()));
        storage.Children.Add(Label("Snapshot export includes catalog, meals, goals, and weight records. Automatic restore is not included in this UI package.", 12, "Muted"));
    }

    private async Task ExportSnapshotAsync()
    {
        if (_profile is null) return;
        try
        {
            using var db = _database();
            var ids = _entries.Select(x => x.FoodName).Distinct().ToList();
            var foods = await db.FoodItems.AsNoTracking().Where(x => ids.Contains(x.RecipeName)).ToListAsync();
            var snapshot = new { Format = "BulkCarnageForever.ProfileSnapshot", Version = 2, ExportedAt = DateTimeOffset.UtcNow,
                Profile = _profile, Preferences = _preferences, WeightUnit = _weightUnit, Goals = _goals, Foods = foods, MealEntries = _entries };
            ExportText("Profile snapshot (*.json)|*.json", $"bulk-carnage-{DateTime.Now:yyyy-MM-dd}.json", JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { StatusText.Text = $"Export failed: {ex.Message}"; }
    }

    private void ExportText(string filter, string fileName, string content)
    {
        var dialog = new SaveFileDialog { Filter = filter, FileName = fileName, AddExtension = true };
        if (dialog.ShowDialog(this) != true) return;
        try { File.WriteAllText(dialog.FileName, content, new UTF8Encoding(true)); StatusText.Text = $"Exported {Path.GetFileName(dialog.FileName)}."; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { StatusText.Text = $"Export failed: {ex.Message}"; }
    }
}

public sealed class JournalWeightEntry
{
    public DateOnly Date { get; set; }
    public double Kg { get; set; }
}
