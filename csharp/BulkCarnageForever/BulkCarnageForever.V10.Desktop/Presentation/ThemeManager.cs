using BulkCarnageForever.V10.Core.Model;
using System.Collections.Concurrent;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace BulkCarnageForever.V10.Desktop.Presentation;

public static class ThemeManager
{
    public const string DefaultTheme = "CarnagePro";
    public static readonly ThemeOption[] Themes =
    {
        new("CarnagePro", "Carnage Pro", "Charcoal. Crimson. Built for focus.", "carnage-pro", false),
        new("NeonNights", "Neon Nights", "Violet surfaces and electric pink light.", "neon-nights", false),
        new("AfterDark", "After Dark (18+)", "Tasteful vintage glamour. Always opt-in.", "after-dark", true)
    };

    public static string Normalize(string? value, bool matureArtworkEnabled = false) => value switch
    {
        "Neon" or "NeonNights" or "Neon Nights" => "NeonNights",
        "AfterDark" or "After Dark (18+)" when matureArtworkEnabled => "AfterDark",
        _ => DefaultTheme
    };

    public static Uri ResourceUri(string relativePath) => new($"pack://application:,,,/{typeof(ThemeManager).Assembly.GetName().Name};component/{relativePath}", UriKind.Absolute);
    private static readonly ConcurrentDictionary<string, ImageSource> Images = new();
    public static ImageSource Artwork(string name) => Images.GetOrAdd(name, LoadArtwork);
    private static ImageSource LoadArtwork(string name)
    {
        var image = new BitmapImage();
        image.BeginInit(); image.UriSource = ResourceUri($"Assets/Artwork/{name}.png"); image.CacheOption = BitmapCacheOption.OnLoad; image.EndInit();
        image.Freeze(); return image;
    }

    public static ImageSource MealArtwork(MealType meal)
    {
        var atlas = (BitmapSource)Artwork("meal-atlas");
        int index = Math.Clamp((int)meal, 0, 3), width = atlas.PixelWidth / 2, height = atlas.PixelHeight / 2;
        var image = new CroppedBitmap(atlas, new Int32Rect(index % 2 * width, index / 2 * height, width, height)); image.Freeze(); return image;
    }

    public static string Apply(ResourceDictionary resources, string? requestedTheme, bool matureArtworkEnabled, string backgroundPreset)
    {
        string theme = Normalize(requestedTheme, matureArtworkEnabled);
        var palette = new ResourceDictionary { Source = ResourceUri($"Themes/Palettes/{theme}.xaml") };
        var current = resources.MergedDictionaries.FirstOrDefault(x => x.Source?.OriginalString.Contains("Palettes/", StringComparison.Ordinal) == true);
        if (current is null) resources.MergedDictionaries.Insert(0, palette);
        else resources.MergedDictionaries[resources.MergedDictionaries.IndexOf(current)] = palette;
        string asset = backgroundPreset switch
        {
            "Food" => "food-spread", "City" => "neon-nights", "None" => "",
            _ => Themes.Single(x => x.Id == theme).Artwork
        };
        // The mature asset is never resolved, decoded, or displayed without explicit consent.
        resources["PageHeroImage"] = asset.Length == 0 ? new DrawingImage() : Artwork(asset);
        resources["HeaderArtworkBrush"] = new ImageBrush((ImageSource)resources["PageHeroImage"])
        {
            Stretch = Stretch.UniformToFill,
            ViewboxUnits = BrushMappingMode.RelativeToBoundingBox,
            Viewbox = asset == "after-dark" ? new Rect(0, 0, 1, .55) : new Rect(0, 0, 1, 1)
        };
        resources["FoodStudioImage"] = Artwork("food-spread");
        resources["ActiveThemeName"] = Themes.Single(x => x.Id == theme).Name;
        return theme;
    }
}

public sealed record ThemeOption(string Id, string Name, string Description, string Artwork, bool RequiresMatureOptIn);
