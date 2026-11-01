using BulkCarnageForever.V10.Core.Model;
using BulkCarnageForever.V10.DAL;
using Microsoft.EntityFrameworkCore;
using System.Windows;
using System.Windows.Controls;

namespace BulkCarnageForever.V10.Desktop
{

    public partial class MainWindow : Window
    {
        private const string AllCategories = "All Categories";
        private const string AllBrands = "All Brands";

        private List<FoodItem> _foods = [];
        private bool _loaded;

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void Window_Loaded(
            object sender, RoutedEventArgs e)
        {
            try
            {
                await using var db = new AppDbContext();

                // Create and seed database if needed
                await db.InitializeAsync();

                // Load the catalog into memory
                _foods = await db.FoodItems
                    .AsNoTracking()
                    .OrderBy(x => x.RecipeName)
                    .ToListAsync();

                // Populate categories
                CategoryComboBox.ItemsSource = new[]
                {
                AllCategories
            }
                .Concat(
                    _foods
                        .Select(x => x.GroupName ?? "Uncategorized")
                        .Distinct()
                        .OrderBy(x => x))
                .ToList();

                _loaded = true;
                CategoryComboBox.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "Database Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void CategoryComboBox_SelectionChanged(
            object sender, SelectionChangedEventArgs e)
        {
            if (!_loaded)
                return;

            PopulateBrands();
        }

        private void PopulateBrands()
        {
            var category = CategoryComboBox.SelectedItem as string;

            var query = _foods.AsEnumerable();

            if (category != AllCategories)
            {
                query = query.Where(x =>
                    (x.GroupName ?? "Uncategorized") == category);
            }

            BrandComboBox.ItemsSource = new[]
            {
            AllBrands
        }
            .Concat(
                query
                    .Select(x => x.BrandType ?? "Unspecified")
                    .Distinct()
                    .OrderBy(x => x))
            .ToList();

            BrandComboBox.SelectedIndex = 0;

            ApplyFilters();
        }

        private void BrandComboBox_SelectionChanged(
            object sender, SelectionChangedEventArgs e)
        {
            if (_loaded)
                ApplyFilters();
        }

        private void SearchTextBox_TextChanged(
            object sender, TextChangedEventArgs e)
        {
            if (_loaded)
                ApplyFilters();
        }

        private void ApplyFilters()
        {
            var category = CategoryComboBox.SelectedItem as string;
            var brand = BrandComboBox.SelectedItem as string;
            var search = SearchTextBox.Text.Trim();

            var query = _foods.AsEnumerable();

            // Category filter
            if (!string.IsNullOrEmpty(category) &&
                category != AllCategories)
            {
                query = query.Where(x =>
                    (x.GroupName ?? "Uncategorized") == category);
            }

            // Brand filter
            if (!string.IsNullOrEmpty(brand) &&
                brand != AllBrands)
            {
                query = query.Where(x =>
                    (x.BrandType ?? "Unspecified") == brand);
            }

            // Search filter
            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(x =>
                    x.RecipeName.Contains(
                        search,
                        StringComparison.OrdinalIgnoreCase));
            }

            var results = query.ToList();

            FoodGrid.ItemsSource = results;

            CountLabel.Text =
                $"Showing {results.Count:N0} of {_foods.Count:N0} foods";
        }

        private void OpenMeals_Click(
    object sender, RoutedEventArgs e)
        {
            PageFrame.Navigate(new MealTrackerPage());
            PageFrame.Visibility = Visibility.Visible;
        }

        public void ShowCatalog()
        {
            PageFrame.Visibility = Visibility.Collapsed;
            PageFrame.Content = null;
        }
    }
}
