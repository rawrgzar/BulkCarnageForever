using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BulkCarnageForever.V10.Core.Model;
using BulkCarnageForever.V10.Core.Simulation;
using BulkCarnageForever.V10.DAL;

namespace BulkCarnageForever.V10.Desktop;

public partial class MainWindow
{
    private Task _ghostComparisonTask = Task.CompletedTask;
    private int _ghostComparisonRequest;
    private string _ghostComparisonRange = "Weekly", _ghostComparisonMetric = "Calories";

    private void BuildGhostComparison()
    {
        if (_profile is null) return;
        var card = Card(SectionContent, "Actual vs Ghost");
        AutomationProperties.SetName(card, "Ghost nutrition comparison");
        var content = (StackPanel)card.Child;
        content.Children.Add(Label("Opening recorded and saved nutrition…", 13, "Muted"));
        _ghostComparisonTask = LoadGhostComparisonAsync(content, _profile.Id, DateOnly.FromDateTime(_day), ++_ghostComparisonRequest);
    }

    private bool ComparisonCurrent(long owner, int request) => _ready && _profile?.Id == owner && _page == "Dashboard" && request == _ghostComparisonRequest;

    private async Task LoadGhostComparisonAsync(StackPanel panel, long owner, DateOnly day, int request)
    {
        try
        {
            List<GhostProfile> ghosts;
            await using (var db = _database()) ghosts = await new GhostProfileStore(db).ListAsync(owner);
            if (!ComparisonCurrent(owner, request)) return;
            var selected = _ghostSelections.TryGetValue(owner, out var id) ? ghosts.FirstOrDefault(x => x.Id == id) : null;
            selected ??= ghosts.FirstOrDefault(x => x.IsDefault) ?? ghosts.FirstOrDefault();
            if (selected is null) throw new InvalidOperationException("No default Ghost is available. Restart the app to initialize it.");
            _ghostSelections[owner] = selected.Id;
            var from = day.AddDays(-((int)day.DayOfWeek + 6) % 7);
            var through = from.AddDays(Math.Min(6, DateOnly.MaxValue.DayNumber - from.DayNumber));
            var days = await new GhostMealStore(_database).CompareAsync(owner, selected.Id, from, through);
            if (!ComparisonCurrent(owner, request)) return;
            panel.Children.RemoveRange(1, panel.Children.Count - 1);
            var selection = new WrapPanel(); panel.Children.Add(selection);
            var selector = new ComboBox { Style = (Style)Resources["DarkComboBox"], Width = 320, HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 12, 10) };
            AutomationProperties.SetName(selector, "Compare Ghost");
            foreach (var ghost in ghosts)
            {
                var item = new ComboBoxItem { Content = ghost.Name + (ghost.IsDefault ? " · Default" : ""), Tag = ghost, ToolTip = ghost.Name };
                selector.Items.Add(item); if (ghost.Id == selected.Id) selector.SelectedItem = item;
            }
            selector.SelectionChanged += (_, _) =>
            {
                if (_busy || _profile?.Id != owner || selector.SelectedItem is not ComboBoxItem { Tag: GhostProfile ghost }) return;
                _ghostSelections[owner] = ghost.Id; RefreshSection();
            };
            selection.Children.Add(selector); selection.Children.Add(Action("Manage Ghosts", () => Navigate("Ghost")));
            var selectedDay = days.Single(x => x.Date == day);
            panel.Children.Add(Label($"Daily totals · {day:ddd, dd MMM yyyy}", 13, "Muted"));
            BuildGhostNutritionTable(panel, selectedDay.Totals);
            var status = Label(ComparisonStatus(selectedDay.Totals), 12,
                selectedDay.Totals.ActualComplete && selectedDay.Totals.SimulatedComplete ? "Muted" : "Amber");
            AutomationProperties.SetName(status, "Ghost comparison status"); panel.Children.Add(status);
            panel.Children.Add(Label("Recorded nutrition uses the current catalog. Ghost nutrition uses its saved calculation basis, including serving overrides.", 12, "Muted"));
            var controls = new WrapPanel(); panel.Children.Add(controls);
            foreach (string range in new[] { "Daily", "Weekly" })
                controls.Children.Add(Action(range, () => { _ghostComparisonRange = range; RefreshSection(); }, _ghostComparisonRange == range));
            foreach (string metric in new[] { "Calories", "Protein", "Carbs", "Fat" })
                controls.Children.Add(Action(metric, () => { _ghostComparisonMetric = metric; RefreshSection(); }, _ghostComparisonMetric == metric));
            string unit = _ghostComparisonMetric == "Calories" ? "kcal" : "g";
            panel.Children.Add(Label(_ghostComparisonRange == "Daily" ? $"Daily · {day:dd MMM yyyy} · {_ghostComparisonMetric} ({unit}) by meal" :
                $"Weekly · {from:dd MMM} – {through:dd MMM yyyy} · {_ghostComparisonMetric} ({unit})", 13));
            var points = _ghostComparisonRange == "Daily"
                ? selectedDay.Meals.Select(x => ChartPoint(x.MealType.ToString(), x.Totals)).ToList()
                : days.Select(x => ChartPoint(x.Date.ToString("ddd\ndd MMM", CultureInfo.CurrentCulture), x.Totals)).ToList();
            AddGhostComparisonChart(panel, points, unit, _ghostComparisonRange == "Daily");
        }
        catch (Exception ex)
        {
            if (!ComparisonCurrent(owner, request)) return;
            panel.Children.RemoveRange(1, panel.Children.Count - 1);
            panel.Children.Add(Label("Could not open nutrition comparison: " + ex.Message, 13, "Amber"));
            panel.Children.Add(Action("Retry comparison", RefreshSection));
        }
    }

    private void BuildGhostNutritionTable(Panel parent, GhostNutritionSummary totals)
    {
        var table = new Grid { Margin = new Thickness(0, 0, 0, 8) }; parent.Children.Add(table);
        table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.2, GridUnitType.Star) });
        for (int i = 0; i < 3; i++) table.ColumnDefinitions.Add(new ColumnDefinition());
        for (int row = 0; row < 5; row++) table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        string[] headings = ["Nutrient", "Actual · recorded", "Ghost · simulated", "Ghost − Actual"];
        for (int column = 0; column < 4; column++) Cell(headings[column], 0, column, "", true);
        string[] metrics = ["Calories", "Protein", "Carbs", "Fat"];
        for (int i = 0; i < metrics.Length; i++)
        {
            string metric = metrics[i]; double actual = ComparisonValue(totals.Actual, metric), ghost = ComparisonValue(totals.Simulated, metric);
            Cell(metric + (metric == "Calories" ? " (kcal)" : " (g)"), i + 1, 0, "");
            Cell(ComparisonTotal(actual, totals.ActualEntries - totals.UnknownActualEntries, totals.ActualComplete), i + 1, 1, "Actual " + metric);
            Cell(ComparisonTotal(ghost, totals.SimulatedEntries - totals.UnknownSimulatedEntries, totals.SimulatedComplete), i + 1, 2, "Ghost " + metric);
            Cell(totals.ActualComplete && totals.SimulatedComplete ? ComparisonNumber(ghost - actual, true) : "—", i + 1, 3, "Difference " + metric);
        }
        void Cell(string value, int row, int column, string name, bool heading = false)
        {
            var label = Label(value, heading ? 11 : 13, heading ? "Muted" : "PrimaryText");
            label.Margin = new Thickness(0, 0, 10, 6); label.TextAlignment = column == 0 ? TextAlignment.Left : TextAlignment.Right;
            if (name.Length > 0) AutomationProperties.SetName(label, name);
            Grid.SetRow(label, row); Grid.SetColumn(label, column); table.Children.Add(label);
        }
    }

    private static string ComparisonTotal(double value, int usableEntries, bool complete) => usableEntries == 0 ? "—" :
        ComparisonNumber(value) + (complete ? "" : " · partial");

    private static string ComparisonStatus(GhostNutritionSummary summary)
    {
        var messages = new List<string>();
        if (summary.ActualEntries == 0) messages.Add("No recorded meals for this date");
        if (summary.MissingSimulations > 0) messages.Add($"Missing simulations: {summary.MissingSimulations}");
        if (summary.StaleSimulations > 0) messages.Add($"Needs recalculation: {summary.StaleSimulations}");
        if (summary.UnknownActualEntries > 0) messages.Add($"Recorded nutrition incomplete: {summary.UnknownActualEntries}");
        if (summary.UnknownSimulatedEntries > 0) messages.Add($"Saved nutrition incomplete: {summary.UnknownSimulatedEntries}");
        return messages.Count == 0 ? "Complete recorded and saved totals." : string.Join(" · ", messages);
    }

    private GhostChartPoint ChartPoint(string label, GhostNutritionSummary summary) => new(label,
        summary.ActualComplete ? ComparisonValue(summary.Actual, _ghostComparisonMetric) : null,
        summary.SimulatedComplete ? ComparisonValue(summary.Simulated, _ghostComparisonMetric) : null,
        ComparisonStatus(summary));

    private static double ComparisonValue(NutritionTotals totals, string metric) => metric switch
    { "Protein" => totals.Protein, "Carbs" => totals.Carbs, "Fat" => totals.Fats, _ => totals.Calories };

    private static string ComparisonNumber(double value, bool signed = false) => Math.Abs(value) >= 1e7
        ? (signed && value > 0 ? "+" : "") + value.ToString("0.###E+0", CultureInfo.CurrentCulture)
        : value.ToString(signed ? "+0.#;-0.#;0" : "0.#", CultureInfo.CurrentCulture);

    internal static double GhostComparisonMaximum(IEnumerable<double> values, string unit)
    {
        double high = values.Where(x => double.IsFinite(x) && x >= 0).DefaultIfEmpty(0).Max();
        double maximum = ChartRange(new[] { high }, unit).Max;
        return double.IsFinite(maximum) && maximum > 0 ? maximum : high > 0 ? high : unit == "kcal" ? 3000 : 100;
    }

    private void AddGhostComparisonChart(Panel parent, List<GhostChartPoint> points, string unit, bool daily)
    {
        var canvas = new Canvas { Width = 900, Height = 230, ClipToBounds = true };
        AutomationProperties.SetName(canvas, "Actual and Ghost nutrition chart");
        AutomationProperties.SetHelpText(canvas, $"{_ghostComparisonMetric} ({unit}). Actual is solid; Ghost is dashed or outlined. Incomplete totals are gaps.");
        var legend = new WrapPanel(); parent.Children.Add(legend);
        var recorded = Label("● Actual · recorded · solid", 12, "Teal"); recorded.Margin = new Thickness(0, 0, 22, 6); legend.Children.Add(recorded);
        legend.Children.Add(Label("□ Ghost · simulated · dashed / outlined", 12, "Lime"));
        parent.Children.Add(new Viewbox { Child = canvas, Stretch = Stretch.Uniform, MaxHeight = 260, HorizontalAlignment = HorizontalAlignment.Stretch });
        double maximum = GhostComparisonMaximum(points.SelectMany(p => new[] { p.Actual, p.Ghost }).Where(x => x.HasValue).Select(x => x!.Value), unit);
        const double left = 80, top = 14, width = 800, height = 152;
        for (int i = 0; i <= 4; i++)
        {
            double fraction = i / 4d, y = top + height * (1 - fraction);
            canvas.Children.Add(new Line { X1 = left, X2 = left + width, Y1 = y, Y2 = y, Stroke = Palette("LineBrush"), StrokeThickness = 1 });
            var tick = Label(ComparisonNumber(maximum * fraction), 12, "Muted"); tick.Width = 72; tick.TextAlignment = TextAlignment.Right;
            tick.TextTrimming = TextTrimming.CharacterEllipsis; tick.ToolTip = ComparisonNumber(maximum * fraction) + " " + unit;
            Canvas.SetLeft(tick, 0); Canvas.SetTop(tick, y - 7); canvas.Children.Add(tick);
        }
        double spacing = width / Math.Max(1, points.Count);
        for (int index = 0; index < points.Count; index++)
        {
            var tick = Label(points[index].Label, 12, "Muted"); tick.Width = spacing; tick.TextAlignment = TextAlignment.Center;
            tick.ToolTip = points[index].Status; Canvas.SetLeft(tick, left + spacing * index); Canvas.SetTop(tick, 181); canvas.Children.Add(tick);
        }
        foreach (bool ghost in new[] { false, true })
        {
            Polyline? line = null;
            var accent = Palette(ghost ? "Lime" : "Teal");
            for (int index = 0; index < points.Count; index++)
            {
                double? value = ghost ? points[index].Ghost : points[index].Actual;
                if (!value.HasValue) { line = null; continue; }
                double center = left + spacing * (index + .5), y = top + height * (1 - value.Value / maximum);
                string tooltip = $"{(ghost ? "Ghost · saved, simulated" : "Actual · recorded")} · {points[index].Label.Replace('\n', ' ')}: {ComparisonNumber(value.Value)} {unit}";
                if (daily)
                {
                    double barWidth = Math.Min(35, spacing * .25), x = center + (ghost ? 5 : -barWidth - 5);
                    var bar = new Rectangle { Width = barWidth, Height = Math.Max(1, top + height - y), Fill = ghost ? Brushes.Transparent : accent,
                        Stroke = accent, StrokeThickness = 2, StrokeDashArray = ghost ? new DoubleCollection { 3, 2 } : new DoubleCollection(), ToolTip = tooltip };
                    Canvas.SetLeft(bar, x); Canvas.SetTop(bar, Math.Min(y, top + height - 1)); canvas.Children.Add(bar);
                }
                else
                {
                    if (line is null)
                    {
                        line = new Polyline { Stroke = accent, StrokeThickness = 2.5, StrokeLineJoin = PenLineJoin.Round,
                            StrokeDashArray = ghost ? new DoubleCollection { 4, 3 } : new DoubleCollection() };
                        canvas.Children.Add(line);
                    }
                    line.Points.Add(new Point(center, y));
                    Shape marker = ghost ? new Rectangle() : new Ellipse();
                    marker.Width = marker.Height = 7; marker.Fill = ghost ? Palette("SurfaceRaised") : accent; marker.Stroke = accent; marker.StrokeThickness = 2; marker.ToolTip = tooltip;
                    Canvas.SetLeft(marker, center - 3.5); Canvas.SetTop(marker, y - 3.5); canvas.Children.Add(marker);
                }
            }
        }
        if (points.All(x => !x.Actual.HasValue && !x.Ghost.HasValue))
        {
            var empty = Label("No complete recorded or saved totals in this view.", 15, "Muted");
            Canvas.SetLeft(empty, left + 140); Canvas.SetTop(empty, top + 55); canvas.Children.Add(empty);
        }
        parent.Children.Add(Label("Gaps mark unlogged dates, incomplete nutrition, or missing/stale simulations. Partial saved totals appear in the table only.", 12, "Muted"));
    }

    private sealed record GhostChartPoint(string Label, double? Actual, double? Ghost, string Status);
}
