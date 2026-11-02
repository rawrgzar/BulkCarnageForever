using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using BulkCarnageForever.V10.Desktop.Presentation;

namespace BulkCarnageForever.V10.Desktop;

public partial class MainWindow
{
    internal bool SetVisualTheme(string theme)
    {
        string requested = ThemeManager.Normalize(theme, true);
        if (requested == "AfterDark" && !_preferences.MatureArtworkEnabled)
        { StatusText.Text = "After Dark artwork requires this profile’s explicit 18+ opt-in in Settings."; return false; }
        string previous = _preferences.Theme; _preferences.Theme = requested;
        if (!SavePreferences()) { _preferences.Theme = previous; return false; }
        ApplyTheme(requested); Render(); StatusText.Text = $"{ThemeManager.Themes.Single(x => x.Id == requested).Name} saved for {_profile!.DisplayName}.";
        return true;
    }

    internal bool SetMatureArtworkEnabled(bool enabled)
    {
        var previous = (_preferences.MatureArtworkEnabled, _preferences.Theme);
        _preferences.MatureArtworkEnabled = enabled;
        if (!enabled) _preferences.Theme = ThemeManager.Normalize(_preferences.Theme);
        if (!SavePreferences()) { (_preferences.MatureArtworkEnabled, _preferences.Theme) = previous; return false; }
        ApplyTheme(_preferences.Theme); Render();
        StatusText.Text = enabled ? "Mature artwork is available for this profile. Select After Dark to use it." : "Mature artwork is disabled for this profile.";
        return true;
    }

    internal bool SetBackgroundPreset(string preset)
    {
        if (preset is not ("Theme" or "Food" or "City" or "None")) return false;
        string previous = _preferences.BackgroundPreset; _preferences.BackgroundPreset = preset;
        if (!SavePreferences()) { _preferences.BackgroundPreset = previous; return false; }
        ApplyTheme(_preferences.Theme); Render(); StatusText.Text = "Background saved for this profile."; return true;
    }

    private void BuildThemeSettings()
    {
        var panel = Section("Choose your atmosphere");
        panel.Children.Add(Label("Your theme travels with your local profile. All artwork is bundled and works offline.", 13, "Muted"));
        var cards = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
        foreach (var theme in ThemeManager.Themes)
        {
            bool locked = theme.RequiresMatureOptIn && !_preferences.MatureArtworkEnabled;
            var content = new StackPanel();
            if (locked)
            {
                var gate = new Border { Height = 108, Background = Palette("AccentSurface"), CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 0, 0, 12) };
                var lockText = Label("18+ · Optional artwork", 16); lockText.VerticalAlignment = VerticalAlignment.Center; lockText.HorizontalAlignment = HorizontalAlignment.Center;
                gate.Child = lockText; content.Children.Add(gate);
            }
            else content.Children.Add(new Image { Source = ThemeManager.Artwork(theme.Artwork), Height = 108, Stretch = System.Windows.Media.Stretch.UniformToFill,
                ClipToBounds = true, Margin = new Thickness(0, 0, 0, 12) });
            var name = Label(theme.Name, 16); name.FontWeight = FontWeights.SemiBold; content.Children.Add(name);
            content.Children.Add(Label(theme.Description, 12, "Muted"));
            var button = Action(theme.Name + (_preferences.Theme == theme.Id ? "  ✓" : ""), () => SetVisualTheme(theme.Id), _preferences.Theme == theme.Id);
            button.IsEnabled = !locked; content.Children.Add(button);
            cards.Children.Add(new Border { Style = (Style)Resources["Surface"], Padding = new Thickness(14), Margin = new Thickness(0, 0, 10, 12), Child = content });
        }
        panel.Children.Add(cards);
        var consent = new CheckBox { Content = "I am 18 or older and choose to enable tasteful mature artwork for this profile.",
            IsChecked = _preferences.MatureArtworkEnabled, Margin = new Thickness(0, 10, 0, 16), Name = "MatureArtworkOptIn" };
        System.Windows.Automation.AutomationProperties.SetName(consent, "Mature artwork explicit opt-in");
        consent.Checked += (_, _) => SetMatureArtworkEnabled(true);
        consent.Unchecked += (_, _) => SetMatureArtworkEnabled(false);
        panel.Children.Add(consent);
        panel.Children.Add(Label("Enabling the option does not select After Dark. You choose that theme separately, and can disable the artwork at any time.", 12, "Muted"));
        string[] names = { "Theme artwork", "Food studio", "City lights", "No imagery" };
        string[] ids = { "Theme", "Food", "City", "None" };
        int index = Array.IndexOf(ids, _preferences.BackgroundPreset);
        var background = Choice(panel, "Header background", names, names[Math.Max(0, index)]);
        background.SelectionChanged += (_, _) => { if (background.SelectedIndex >= 0) SetBackgroundPreset(ids[background.SelectedIndex]); };
    }
}
