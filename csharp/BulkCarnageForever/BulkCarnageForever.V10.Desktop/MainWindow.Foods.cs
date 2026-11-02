using BulkCarnageForever.V10.Core.Model;
using BulkCarnageForever.V10.DAL;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace BulkCarnageForever.V10.Desktop;

public partial class MainWindow
{
    private int _foodCount, _libraryPage, _foodRequest, _libraryRequest;
    private const int FoodPageSize = 24;
    private DispatcherTimer? _searchTimer;
    private JournalLibraryView _libraryView = new();

    private IQueryable<FoodItem> FoodQuery(AppDbContext db, string term, bool filtered)
    {
        var query = db.FoodItems.AsNoTracking();
        if (filtered && CategoryInput.SelectedItem is string category && category != "All categories")
            query = query.Where(x => (x.GroupName ?? "Uncategorized") == category);
        if (filtered && BrandInput.SelectedItem is string brand && brand != "All brands")
            query = query.Where(x => (x.BrandType ?? "Generic") == brand);
        if (term.Length > 0)
        {
            term = term.ToLowerInvariant();
            query = query.Where(x => x.RecipeName.ToLower().Contains(term) || (x.BrandType ?? "").ToLower().Contains(term) || (x.GroupName ?? "").ToLower().Contains(term));
        }
        if (FavoritesOnly.IsChecked == true)
        {
            var favorites = _preferences.Favorites;
            query = query.Where(x => favorites.Contains(x.RecipeName));
        }
        return query;
    }

    private async Task CacheEntryFoodsAsync(AppDbContext db, List<MealEntry> entries)
    {
        var names = entries.Select(x => x.FoodName).Distinct().Where(x => !_catalog.ContainsKey(x)).ToList();
        if (names.Count == 0) return;
        foreach (var food in await db.FoodItems.AsNoTracking().Where(x => names.Contains(x.RecipeName)).ToListAsync()) _catalog[food.RecipeName] = food;
    }

    private async Task LoadBrandsAsync()
    {
        await using var db = _database();
        var query = db.FoodItems.AsNoTracking();
        if (CategoryInput.SelectedItem is string category && category != "All categories") query = query.Where(x => (x.GroupName ?? "Uncategorized") == category);
        BrandInput.ItemsSource = new[] { "All brands" }.Concat(await query.Select(x => x.BrandType ?? "Generic").Distinct().OrderBy(x => x).ToListAsync()).ToList();
        BrandInput.SelectedIndex = 0;
    }

    private async void Category_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_ready || _updating || _switching) return;
        _updating = true;
        try { await LoadBrandsAsync(); }
        catch (Exception ex) { StatusText.Text = $"Could not load brands: {ex.Message}"; }
        finally { _updating = false; }
        _libraryPage = 0; RefreshSearch(); await RefreshLibraryAsync();
    }
    private async void Brand_Changed(object sender, SelectionChangedEventArgs e)
    { if (_ready && !_updating && !_switching) { _libraryPage = 0; RefreshSearch(); await RefreshLibraryAsync(); } }

    private void Search_Changed(object sender, TextChangedEventArgs e)
    {
        if (!_ready || _updating) return;
        _selected = null; UpdatePreview();
        _foodRequest++; _libraryRequest++;
        _searchTimer ??= MakeSearchTimer();
        _searchTimer.Stop(); _searchTimer.Start();
    }
    private DispatcherTimer MakeSearchTimer()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        timer.Tick += async (_, _) => { timer.Stop(); _libraryPage = 0; await RefreshSearchAsync(); await RefreshLibraryAsync(); };
        return timer;
    }

    private async void RefreshSearch() => await RefreshSearchAsync();
    internal async Task RefreshSearchAsync()
    {
        if (!_ready || _updating || _profile is null) return;
        int request = ++_foodRequest; long profileId = _profile.Id;
        string term = FoodSearch.Text.Trim();
        try
        {
            await using var db = _database();
            var favorites = _preferences.Favorites.ToList();
            var recent = _entries.OrderByDescending(x => x.Id).Select(x => x.FoodName).Distinct().Take(40).ToList();
            var foods = await FoodQuery(db, term, _library).OrderByDescending(x => favorites.Contains(x.RecipeName))
                .ThenByDescending(x => recent.Contains(x.RecipeName)).ThenBy(x => x.RecipeName).Take(40).ToListAsync();
            if (request != _foodRequest || profileId != _profile?.Id) return;
            foreach (var food in foods) _catalog[food.RecipeName] = food;
            var results = foods.Select(x => new JournalFoodResult(x, (_preferences.Favorites.Contains(x.RecipeName) ? "★ " : "") +
                $"{x.RecipeName} · {x.CaloriesPerServing:0.#} kcal · P {x.Protein:0.#} / C {x.Carbs:0.#} / F {x.Fats:0.#}g")).ToList();
            _updating = true; SearchResults.ItemsSource = results; SearchResults.SelectedIndex = -1; _updating = false;
            ResultsLabel.Text = results.Count == 0 ? "No matching foods" : term.Length == 0 ? "Favorites & recents" : $"{results.Count} matches · max 40";
        }
        catch (Exception ex) { if (request == _foodRequest) StatusText.Text = $"Food search failed: {ex.Message}"; }
    }

    internal async Task RefreshLibraryAsync()
    {
        if (!_ready || !_library || _profile is null) return;
        int request = ++_libraryRequest; long profileId = _profile.Id;
        int page = _libraryPage;
        try
        {
            await using var db = _database();
            var query = FoodQuery(db, FoodSearch.Text.Trim(), true);
            int count = await query.CountAsync();
            page = Math.Clamp(page, 0, Math.Max(0, (count - 1) / FoodPageSize));
            var foods = await query.OrderBy(x => x.RecipeName).Skip(page * FoodPageSize).Take(FoodPageSize).ToListAsync();
            if (request != _libraryRequest || profileId != _profile?.Id || !_library) return;
            _libraryPage = page;
            foreach (var food in foods) _catalog[food.RecipeName] = food;
            _libraryView = new JournalLibraryView { Items = foods, CanPrevious = page > 0, CanNext = (page + 1) * FoodPageSize < count,
                Summary = count == 0 ? "No matching foods" : $"{page * FoodPageSize + 1:N0}–{page * FoodPageSize + foods.Count:N0} of {count:N0} foods · Page {page + 1}" };
            MainContent.Content = _libraryView; PeriodSummary.Text = _libraryView.Summary;
            await Dispatcher.InvokeAsync(() =>
            {
                if (request == _libraryRequest && _library) FindFoodScroller(MainContent)?.ScrollToTop();
            }, DispatcherPriority.Loaded);
        }
        catch (Exception ex) { if (request == _libraryRequest) StatusText.Text = $"Could not load food page: {ex.Message}"; }
    }
    private async void PreviousFoods_Click(object sender, RoutedEventArgs e) { if (_libraryPage > 0) { _libraryPage--; await RefreshLibraryAsync(); } }
    private async void NextFoods_Click(object sender, RoutedEventArgs e) { if (_libraryView.CanNext) { _libraryPage++; await RefreshLibraryAsync(); } }
    private static ScrollViewer? FindFoodScroller(DependencyObject parent)
    {
        if (parent is ScrollViewer scroll) return scroll;
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var result = FindFoodScroller(System.Windows.Media.VisualTreeHelper.GetChild(parent, i));
            if (result is not null) return result;
        }
        return null;
    }
}

public sealed class JournalLibraryView
{
    public List<FoodItem> Items { get; init; } = new();
    public string Summary { get; init; } = "Loading foods…";
    public bool CanPrevious { get; init; }
    public bool CanNext { get; init; }
}
