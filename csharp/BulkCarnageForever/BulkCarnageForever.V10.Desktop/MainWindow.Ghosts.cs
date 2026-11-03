using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using BulkCarnageForever.V10.Core.Model;
using BulkCarnageForever.V10.Core.Simulation;
using BulkCarnageForever.V10.DAL;
using Microsoft.EntityFrameworkCore;
using UiButton = Wpf.Ui.Controls.Button;
using UiTextBox = Wpf.Ui.Controls.TextBox;

namespace BulkCarnageForever.V10.Desktop;

public partial class MainWindow
{
    private readonly Dictionary<long, Guid> _ghostSelections = new();
    private Task _ghostPageTask = Task.CompletedTask, _ghostActionTask = Task.CompletedTask;
    private long _ghostPageOwner;
    private DateOnly? _ghostPortionDay;
    private int _ghostRequest, _ghostPortionPage;
    private const int GhostPageSize = 8;
    private static readonly DayOfWeek[] GhostWeek = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday,
        DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday];

    private void BuildGhosts()
    {
        if (_profile is null) return;
        var day = DateOnly.FromDateTime(_day);
        if (_ghostPageOwner != _profile.Id || _ghostPortionDay != day)
        { _ghostPageOwner = _profile.Id; _ghostPortionDay = day; _ghostPortionPage = 0; }
        var host = new StackPanel(); SectionContent.Children.Add(host);
        host.Children.Add(Label("Opening your saved Ghosts…", 14, "Muted"));
        _ghostPageTask = LoadGhostPageAsync(host, _profile.Id, day, ++_ghostRequest);
    }

    private bool GhostPageCurrent(long owner, int request) => _ready && _profile?.Id == owner && _page == "Ghost" && request == _ghostRequest;

    private async Task LoadGhostPageAsync(StackPanel host, long owner, DateOnly day, int request)
    {
        try
        {
            await using var db = _database();
            var ghosts = await new GhostProfileStore(db).ListAsync(owner);
            if (!GhostPageCurrent(owner, request)) return;
            var selected = _ghostSelections.TryGetValue(owner, out var id) ? ghosts.FirstOrDefault(x => x.Id == id) : null;
            selected ??= ghosts.FirstOrDefault(x => x.IsDefault) ?? ghosts.FirstOrDefault();
            if (selected is null) throw new InvalidOperationException("No default Ghost is available. Restart the app to initialize it.");
            _ghostSelections[owner] = selected.Id;
            var comparisons = await new GhostMealStore(_database).ListAsync(owner, selected.Id, day, day);
            var savedIds = comparisons.Select(x => x.Simulation.MealEntryId).ToList();
            var actual = await db.MealEntries.AsNoTracking().Where(x => x.UserProfileId == owner &&
                (x.Date == day || savedIds.Contains(x.Id))).OrderBy(x => x.MealType).ThenBy(x => x.Id).ToListAsync();
            var names = actual.Select(x => x.FoodName).Distinct().ToList();
            var foods = await db.FoodItems.AsNoTracking().Where(x => names.Contains(x.RecipeName)).ToDictionaryAsync(x => x.RecipeName);
            if (!GhostPageCurrent(owner, request)) return;
            host.Children.Clear();
            BuildGhostProfiles(host, owner, ghosts, selected);
            BuildGhostWeek(host, owner, selected);
            BuildGhostPortions(host, owner, selected, day, comparisons, actual, foods);
            BuildGhostRecalculation(host, owner, selected, day);
        }
        catch (Exception ex)
        {
            if (!GhostPageCurrent(owner, request)) return;
            host.Children.Clear(); host.Children.Add(Label("Could not open Ghost: " + ex.Message, 14, "Amber"));
            host.Children.Add(Action("Retry", RefreshSection));
        }
    }

    private void BuildGhostProfiles(Panel host, long owner, List<GhostProfile> ghosts, GhostProfile selected)
    {
        var panel = (StackPanel)Card(host, "Your Ghosts").Child;
        panel.Children.Add(Label("Virtual portions of your own logged foods. Your recorded diary stays separate.", 13, "Muted"));
        var selector = new ComboBox { Style = (Style)Resources["DarkComboBox"], Width = 420,
            HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 10) };
        AutomationProperties.SetName(selector, "Select Ghost");
        foreach (var ghost in ghosts)
        {
            var item = new ComboBoxItem { Content = ghost.Name + (ghost.IsDefault ? " · Default" : ""), Tag = ghost, ToolTip = ghost.Name };
            selector.Items.Add(item); if (ghost.Id == selected.Id) selector.SelectedItem = item;
        }
        selector.SelectionChanged += (_, _) =>
        {
            if (_busy || _profile?.Id != owner || selector.SelectedItem is not ComboBoxItem { Tag: GhostProfile ghost }) return;
            _ghostSelections[owner] = ghost.Id; _ghostPortionPage = 0; RefreshSection();
        };
        panel.Children.Add(selector);
        var rename = new WrapPanel(); panel.Children.Add(rename);
        var namePanel = new StackPanel { Width = 280, Margin = new Thickness(0, 0, 12, 0) }; rename.Children.Add(namePanel);
        var name = GhostField(namePanel, "Ghost name", selected.Name); name.MaxLength = 80;
        rename.Children.Add(GhostAction("Rename Ghost", owner, async () =>
        {
            await using var db = _database(); await new GhostProfileStore(db).RenameAsync(owner, selected.Id, name.Text);
            return "Ghost name saved.";
        }));
        var create = new WrapPanel(); panel.Children.Add(create);
        var newPanel = new StackPanel { Width = 280, Margin = new Thickness(0, 0, 12, 0) }; create.Children.Add(newPanel);
        var newName = GhostField(newPanel, "New Ghost name", ""); newName.MaxLength = 80;
        create.Children.Add(GhostAction("Create Ghost", owner, async () =>
        {
            await using var db = _database(); var ghost = await new GhostProfileStore(db).CreateAsync(owner, newName.Text);
            _ghostSelections[owner] = ghost.Id; _ghostPortionPage = 0; return "Ghost created with seven 100% days.";
        }));
        create.Children.Add(GhostAction("Duplicate Ghost", owner, async () =>
        {
            await using var db = _database(); var ghost = await new GhostProfileStore(db).DuplicateAsync(owner, selected.Id, newName.Text);
            _ghostSelections[owner] = ghost.Id; _ghostPortionPage = 0; return "Weekly settings copied to the new Ghost. Saved history was not copied.";
        }));
        panel.Children.Add(Label("Enter a new name to create a Ghost or duplicate the selected weekly settings.", 12, "Muted"));
        var confirmation = new StackPanel();
        var delete = Action("Delete Ghost", () =>
        {
            confirmation.Children.Clear();
            confirmation.Children.Add(Label($"Delete ‘{selected.Name}’ and its saved simulations? Recorded food logs will stay in your diary.", 13, "Amber"));
            var choices = new WrapPanel(); confirmation.Children.Add(choices);
            choices.Children.Add(GhostAction("Confirm deletion", owner, async () =>
            {
                await using var db = _database(); await new GhostProfileStore(db).DeleteAsync(owner, selected.Id);
                _ghostSelections.Remove(owner); _ghostPortionPage = 0; return "Ghost deleted. Your recorded meals are unchanged.";
            }));
            choices.Children.Add(Action("Cancel deletion", () => confirmation.Children.Clear()));
        });
        delete.IsEnabled = !selected.IsDefault; panel.Children.Add(delete); panel.Children.Add(confirmation);
    }

    private void BuildGhostWeek(Panel host, long owner, GhostProfile selected)
    {
        var panel = (StackPanel)Card(host, "Weekly portions").Child;
        panel.Children.Add(Label("100% matches your logged portion. These settings apply to new logs; saved history changes only when you recalculate.", 13, "Muted"));
        var week = new WrapPanel(); panel.Children.Add(week);
        var inputs = new List<UiTextBox>();
        var displayed = GhostWeek.Select(day => GhostNumber(selected.GetMultiplier(day) * 100)).ToArray();
        foreach (var day in GhostWeek)
        {
            var cell = new StackPanel { Width = 112, Margin = new Thickness(0, 0, 12, 0) }; week.Children.Add(cell);
            inputs.Add(GhostField(cell, day + " (%)", displayed[inputs.Count]));
        }
        panel.Children.Add(GhostAction("Save weekly percentages", owner, async () =>
        {
            // Friendly display precision must not alter an untouched saved ratio.
            var values = inputs.Select((input, index) => input.Text == displayed[index] ? selected.GetMultiplier(GhostWeek[index]) :
                Number(input.Text, out var value) && value >= 0 ? value / 100 :
                throw new ArgumentException("Every percentage must be finite and nonnegative.")).ToArray();
            var settings = new GhostProfile
            {
                MondayMultiplier = values[0], TuesdayMultiplier = values[1], WednesdayMultiplier = values[2],
                ThursdayMultiplier = values[3], FridayMultiplier = values[4], SaturdayMultiplier = values[5], SundayMultiplier = values[6],
                VisualIdentity = selected.VisualIdentity
            };
            await using var db = _database(); await new GhostProfileStore(db).UpdateAsync(owner, selected.Id, settings);
            return "Weekly percentages saved for new logs. Saved simulations are unchanged.";
        }, true));
    }

    private void BuildGhostPortions(Panel host, long owner, GhostProfile selected, DateOnly day,
        List<GhostSimulationComparison> saved, List<MealEntry> actual, Dictionary<string, FoodItem> foods)
    {
        var panel = (StackPanel)Card(host, $"Saved portions · {day:dd MMM yyyy} · Simulated").Child;
        panel.Children.Add(Label("Use the tracking date above to review saved servings. An override uses the saved food nutrition; reset restores its saved baseline.", 13, "Muted"));
        if (actual.Count == 0) { panel.Children.Add(Label("No foods logged for this day. Ghosts only simulate foods you record.", 14, "Muted")); return; }
        var byMeal = saved.ToDictionary(x => x.Simulation.MealEntryId);
        int pages = (actual.Count + GhostPageSize - 1) / GhostPageSize;
        _ghostPortionPage = Math.Clamp(_ghostPortionPage, 0, pages - 1);
        foreach (var meal in actual.Skip(_ghostPortionPage * GhostPageSize).Take(GhostPageSize))
        {
            var row = new StackPanel { Margin = new Thickness(0, 8, 0, 14) }; panel.Children.Add(row);
            row.Children.Add(Label(meal.FoodName + " · " + meal.MealType, 16));
            string unit = foods.TryGetValue(meal.FoodName, out var food) ? food.MeasurementType ?? "catalog units" : "catalog units";
            row.Children.Add(Label($"Recorded: {meal.AmountEaten:0.####} {unit} · {meal.Date:dd MMM yyyy}", 12, "Muted"));
            if (!byMeal.TryGetValue(meal.Id, out var comparison))
            {
                row.Children.Add(Label("No saved simulation. Review a recalculation to include this logged food.", 13, "Muted"));
                continue;
            }
            var simulation = comparison.Simulation;
            row.Children.Add(Label($"SIMULATED · {simulation.SimulatedServings:0.####} servings · {simulation.Calories:0.#} kcal" +
                (simulation.OverrideServings.HasValue ? " · Manual override" : " · Weekly baseline"), 13, "Teal"));
            if (comparison.NeedsRecalculation)
                row.Children.Add(Label($"Needs recalculation · Recorded meal changed. Saved scenario: {simulation.FoodName}, {simulation.Date:dd MMM yyyy}.", 13, "Amber"));
            var controls = new WrapPanel(); row.Children.Add(controls);
            var amount = new StackPanel { Width = 180, Margin = new Thickness(0, 0, 12, 0) }; controls.Children.Add(amount);
            string displayedServings = GhostNumber(simulation.SimulatedServings);
            var servings = GhostField(amount, "Ghost servings", displayedServings);
            controls.Children.Add(GhostAction("Save serving override", owner, async () =>
            {
                double value = simulation.SimulatedServings;
                if (servings.Text != displayedServings && (!Number(servings.Text, out value) || value < 0))
                    throw new ArgumentException("Servings must be finite and nonnegative.");
                await new GhostMealStore(_database).SetServingOverrideAsync(owner, simulation.Id, value);
                return "Ghost serving override saved. Your recorded portion is unchanged.";
            }));
            var reset = GhostAction("Reset to saved baseline", owner, async () =>
            {
                await new GhostMealStore(_database).SetServingOverrideAsync(owner, simulation.Id, null);
                return "Saved weekly baseline restored. Current weekly settings were not applied to history.";
            });
            reset.IsEnabled = simulation.OverrideServings.HasValue; controls.Children.Add(reset);
        }
        if (pages > 1)
        {
            var pager = new WrapPanel(); panel.Children.Add(pager);
            var previous = Action("Previous portions", () => { _ghostPortionPage--; RefreshSection(); }); previous.IsEnabled = _ghostPortionPage > 0;
            var next = Action("Next portions", () => { _ghostPortionPage++; RefreshSection(); }); next.IsEnabled = _ghostPortionPage + 1 < pages;
            pager.Children.Add(previous); pager.Children.Add(Label($"Page {_ghostPortionPage + 1} of {pages}", 12, "Muted")); pager.Children.Add(next);
        }
    }

    private void BuildGhostRecalculation(Panel host, long owner, GhostProfile selected, DateOnly day)
    {
        var panel = (StackPanel)Card(host, "Historical scenarios").Child;
        var expand = new Expander { Header = "Recalculate saved history", Foreground = Palette("PrimaryText") };
        AutomationProperties.SetName(expand, "Recalculate saved history"); panel.Children.Add(expand);
        var form = new StackPanel { Margin = new Thickness(0, 14, 0, 0) }; expand.Content = form;
        form.Children.Add(Label("Uses your current logged foods, weekly percentages, and food nutrition. Includes meals whose recorded or saved date falls in the range. Recorded diary entries are unchanged.", 13, "Muted"));
        var dates = new WrapPanel(); form.Children.Add(dates);
        var from = GhostDate(dates, "Recalculate from", day);
        var through = GhostDate(dates, "Recalculate through", day);
        var keep = new CheckBox { Content = "Keep manual serving overrides", IsChecked = true, Foreground = Palette("PrimaryText"), Margin = new Thickness(0, 8, 0, 12) };
        AutomationProperties.SetName(keep, "Keep manual serving overrides"); form.Children.Add(keep);
        form.Children.Add(Label("Kept overrides retain their serving count, even if a logged food changed. Clearing this option resets overrides only in the selected range.", 12, "Muted"));
        var review = new StackPanel();
        form.Children.Add(Action("Review recalculation", () =>
        {
            review.Children.Clear();
            if (from.SelectedDate is not DateTime start || through.SelectedDate is not DateTime end || start.Date > end.Date)
            { StatusText.Text = "Choose a valid date range with the end on or after the start."; return; }
            bool preserve = keep.IsChecked == true;
            review.Children.Add(Label($"Recalculate ‘{selected.Name}’ · {start:dd MMM yyyy} – {end:dd MMM yyyy}\n" +
                (preserve ? "Manual serving counts will be kept." : "Manual overrides in this range will be cleared."), 14, "Amber"));
            review.Children.Add(GhostAction("Apply recalculation", owner, async () =>
            {
                int count = await new GhostMealStore(_database).RecalculateAsync(owner, selected.Id,
                    DateOnly.FromDateTime(start), DateOnly.FromDateTime(end), preserve);
                return $"Recalculated {count} logged meals for {selected.Name}. Your actual diary is unchanged.";
            }, true));
        }));
        form.Children.Add(review);
        from.SelectedDateChanged += (_, _) => review.Children.Clear(); through.SelectedDateChanged += (_, _) => review.Children.Clear();
        keep.Checked += (_, _) => review.Children.Clear(); keep.Unchecked += (_, _) => review.Children.Clear();
    }

    private UiTextBox GhostField(Panel parent, string label, string value)
    {
        var field = Field(parent, label, value); field.Width = double.NaN; field.MaxWidth = 420;
        field.HorizontalAlignment = HorizontalAlignment.Stretch; return field;
    }

    private DatePicker GhostDate(Panel parent, string label, DateOnly value)
    {
        var group = new StackPanel { Width = 190, Margin = new Thickness(0, 0, 16, 0) }; parent.Children.Add(group);
        group.Children.Add(Label(label, 12, "Muted"));
        var picker = new DatePicker { SelectedDate = value.ToDateTime(TimeOnly.MinValue), SelectedDateFormat = DatePickerFormat.Short,
            Background = Palette("InputBackground"), Foreground = Palette("PrimaryText") };
        AutomationProperties.SetName(picker, label); group.Children.Add(picker); return picker;
    }

    private UiButton GhostAction(string text, long owner, Func<Task<string>> operation, bool primary = false)
    {
        var button = new UiButton { Content = text, Style = (Style)Resources[primary ? "Primary" : "Action"],
            Margin = new Thickness(0, 4, 10, 4), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left };
        button.Click += (_, _) => _ghostActionTask = RunGhostActionAsync(owner, operation);
        return button;
    }

    private async Task RunGhostActionAsync(long owner, Func<Task<string>> operation)
    {
        if (_busy || _switching || !_ready || _profile?.Id != owner) return;
        _busy = true; ApplicationShell.IsEnabled = false;
        string? success = null;
        try { success = await operation(); }
        catch (Exception ex) { if (_profile?.Id == owner) StatusText.Text = "Ghost: " + ex.Message; }
        finally { _busy = false; ApplicationShell.IsEnabled = _ready && _profile is not null && !_switching; }
        if (success is null || !_ready || _profile?.Id != owner) return;
        if (_page == "Ghost") { RefreshSection(); await _ghostPageTask; }
        StatusText.Text = success;
    }

    private static string GhostNumber(double value) => value.ToString("G15", CultureInfo.CurrentCulture);
}
