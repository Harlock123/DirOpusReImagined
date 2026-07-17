using Avalonia;
using Avalonia.Styling;

namespace DirOpusReImagined;

/// <summary>
/// The three theme options the user can choose.
/// </summary>
public enum ThemeChoice
{
    Light,
    Dark,
    System
}

/// <summary>
/// Central switch for the app's Light/Dark theme. Flipping the application's
/// <see cref="Application.RequestedThemeVariant"/> re-resolves every {DynamicResource} token
/// defined in App.axaml (and Fluent's own resources), so all theme-aware controls update live.
///
/// <para><b>System</b> maps to <see cref="ThemeVariant.Default"/>, which makes Avalonia follow the
/// operating system's theme and track OS changes automatically — no manual polling needed.</para>
/// </summary>
public static class ThemeManager
{
    /// <summary>The currently selected choice (Light/Dark/System).</summary>
    public static ThemeChoice Current { get; private set; } = ThemeChoice.Light;

    /// <summary>Applies <paramref name="choice"/> to the running application.</summary>
    public static void Apply(ThemeChoice choice)
    {
        Current = choice;

        var app = Application.Current;
        if (app is null) return;

        app.RequestedThemeVariant = choice switch
        {
            ThemeChoice.Light  => ThemeVariant.Light,
            ThemeChoice.Dark   => ThemeVariant.Dark,
            // Default = follow the OS, and keep following it as the OS theme changes.
            ThemeChoice.System => ThemeVariant.Default,
            _                  => ThemeVariant.Light
        };
    }
}
