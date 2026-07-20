using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace DirOpusReImagined;

/// <summary>
/// The theme options the user can choose. Light/Dark/System use Avalonia's built-in variants;
/// the named themes are custom palettes registered at startup (see <see cref="ThemeManager.Init"/>).
/// </summary>
public enum ThemeChoice
{
    Light,
    Dark,
    System,
    Dracula,
    Nord,
    SolarizedLight,
    SolarizedDark
}

/// <summary>
/// Central switch for the app's theme. Flipping <see cref="Application.RequestedThemeVariant"/>
/// re-resolves every {DynamicResource} token from App.axaml plus the canvas-grid palette, so all
/// theme-aware surfaces update live.
///
/// <para>Light/Dark live in App.axaml. The named themes (Dracula, Nord, Solarized) are registered
/// here as custom <see cref="ThemeVariant"/>s that inherit a light or dark base — so Avalonia's
/// Fluent controls fall back to that base's look while our semantic tokens carry the palette on the
/// app's distinctive surfaces (grids, chrome, status bar, compare colors).</para>
/// </summary>
public static class ThemeManager
{
    public static ThemeChoice Current { get; private set; } = ThemeChoice.Light;

    // Custom variants inherit a base so unlisted (Fluent) resources resolve against Light/Dark.
    public static readonly ThemeVariant DraculaVariant        = new("Dracula", ThemeVariant.Dark);
    public static readonly ThemeVariant NordVariant           = new("Nord", ThemeVariant.Dark);
    public static readonly ThemeVariant SolarizedLightVariant = new("SolarizedLight", ThemeVariant.Light);
    public static readonly ThemeVariant SolarizedDarkVariant  = new("SolarizedDark", ThemeVariant.Dark);

    /// <summary>
    /// Registers the named-theme palettes into the application's theme dictionaries. Call once at
    /// startup before the first <see cref="Apply"/>.
    /// </summary>
    public static void Init(Application app)
    {
        if (app?.Resources is not { } res) return;

        res.ThemeDictionaries[DraculaVariant]        = Build(Dracula);
        res.ThemeDictionaries[NordVariant]           = Build(Nord);
        res.ThemeDictionaries[SolarizedLightVariant] = Build(SolarizedLight);
        res.ThemeDictionaries[SolarizedDarkVariant]  = Build(SolarizedDark);
    }

    public static void Apply(ThemeChoice choice)
    {
        Current = choice;

        var app = Application.Current;
        if (app is null) return;

        app.RequestedThemeVariant = choice switch
        {
            ThemeChoice.Light          => ThemeVariant.Light,
            ThemeChoice.Dark           => ThemeVariant.Dark,
            ThemeChoice.System         => ThemeVariant.Default,   // follow the OS
            ThemeChoice.Dracula        => DraculaVariant,
            ThemeChoice.Nord           => NordVariant,
            ThemeChoice.SolarizedLight => SolarizedLightVariant,
            ThemeChoice.SolarizedDark  => SolarizedDarkVariant,
            _                          => ThemeVariant.Light
        };
    }

    private static ResourceDictionary Build((string Key, string Hex)[] tokens)
    {
        var d = new ResourceDictionary();
        foreach (var (key, hex) in tokens)
            d[key] = new SolidColorBrush(Color.Parse(hex));
        return d;
    }

    // ---- Palettes. Each defines all 26 semantic tokens. Row/compare/drop text stays legible
    //      automatically via the grid's WCAG contrast helper, so these are surface colors. ----

    private static readonly (string, string)[] Dracula =
    {
        ("WindowBackgroundBrush", "#282A36"), ("SurfaceBackgroundBrush", "#21222C"),
        ("PanelChromeBrush", "#BD93F9"), ("BorderSubtleBrush", "#44475A"), ("MutedTextBrush", "#6272A4"),
        ("StatusBarBackgroundBrush", "#21222C"), ("StatusBarTextBrush", "#F8F8F2"),
        ("ScrollBarBackgroundBrush", "#343746"),
        ("GridBackgroundBrush", "#282A36"), ("GridCellBrush", "#343746"), ("GridCellTextBrush", "#F8F8F2"),
        ("GridCellOutlineBrush", "#44475A"), ("GridHeaderBackgroundBrush", "#44475A"),
        ("GridHeaderTextBrush", "#8BE9FD"), ("GridTitleBackgroundBrush", "#6272A4"), ("GridTitleTextBrush", "#F8F8F2"),
        ("RowSelectedBrush", "#BD93F9"), ("RowHoverBrush", "#44475A"), ("RowCursorBrush", "#FF79C6"),
        ("DropTargetBrush", "#FFB86C"), ("ActivePanelFrameBrush", "#FF79C6"),
        ("CompareUniqueBrush", "#2E5A3D"), ("CompareNewerBrush", "#24506E"), ("CompareOlderBrush", "#44475A"),
        ("CompareDifferentBrush", "#5C531F"), ("CompareInaccessibleBrush", "#6E2B2B"),
    };

    private static readonly (string, string)[] Nord =
    {
        ("WindowBackgroundBrush", "#2E3440"), ("SurfaceBackgroundBrush", "#292E39"),
        ("PanelChromeBrush", "#5E81AC"), ("BorderSubtleBrush", "#434C5E"), ("MutedTextBrush", "#8790A0"),
        ("StatusBarBackgroundBrush", "#292E39"), ("StatusBarTextBrush", "#D8DEE9"),
        ("ScrollBarBackgroundBrush", "#3B4252"),
        ("GridBackgroundBrush", "#2E3440"), ("GridCellBrush", "#3B4252"), ("GridCellTextBrush", "#ECEFF4"),
        ("GridCellOutlineBrush", "#434C5E"), ("GridHeaderBackgroundBrush", "#434C5E"),
        ("GridHeaderTextBrush", "#88C0D0"), ("GridTitleBackgroundBrush", "#4C566A"), ("GridTitleTextBrush", "#ECEFF4"),
        ("RowSelectedBrush", "#5E81AC"), ("RowHoverBrush", "#434C5E"), ("RowCursorBrush", "#88C0D0"),
        ("DropTargetBrush", "#EBCB8B"), ("ActivePanelFrameBrush", "#88C0D0"),
        ("CompareUniqueBrush", "#3B5A3B"), ("CompareNewerBrush", "#3A5068"), ("CompareOlderBrush", "#3B4252"),
        ("CompareDifferentBrush", "#5C5330"), ("CompareInaccessibleBrush", "#6E3B40"),
    };

    private static readonly (string, string)[] SolarizedDark =
    {
        ("WindowBackgroundBrush", "#002B36"), ("SurfaceBackgroundBrush", "#073642"),
        ("PanelChromeBrush", "#268BD2"), ("BorderSubtleBrush", "#586E75"), ("MutedTextBrush", "#839496"),
        ("StatusBarBackgroundBrush", "#073642"), ("StatusBarTextBrush", "#93A1A1"),
        ("ScrollBarBackgroundBrush", "#073642"),
        ("GridBackgroundBrush", "#002B36"), ("GridCellBrush", "#073642"), ("GridCellTextBrush", "#93A1A1"),
        ("GridCellOutlineBrush", "#586E75"), ("GridHeaderBackgroundBrush", "#073642"),
        ("GridHeaderTextBrush", "#2AA198"), ("GridTitleBackgroundBrush", "#586E75"), ("GridTitleTextBrush", "#FDF6E3"),
        ("RowSelectedBrush", "#268BD2"), ("RowHoverBrush", "#073642"), ("RowCursorBrush", "#2AA198"),
        ("DropTargetBrush", "#B58900"), ("ActivePanelFrameBrush", "#2AA198"),
        ("CompareUniqueBrush", "#2A4B2A"), ("CompareNewerBrush", "#0E4A5A"), ("CompareOlderBrush", "#073642"),
        ("CompareDifferentBrush", "#5A4A00"), ("CompareInaccessibleBrush", "#5A1F1E"),
    };

    private static readonly (string, string)[] SolarizedLight =
    {
        ("WindowBackgroundBrush", "#FDF6E3"), ("SurfaceBackgroundBrush", "#EEE8D5"),
        ("PanelChromeBrush", "#B58900"), ("BorderSubtleBrush", "#93A1A1"), ("MutedTextBrush", "#657B83"),
        ("StatusBarBackgroundBrush", "#586E75"), ("StatusBarTextBrush", "#FDF6E3"),
        ("ScrollBarBackgroundBrush", "#EEE8D5"),
        ("GridBackgroundBrush", "#FDF6E3"), ("GridCellBrush", "#EEE8D5"), ("GridCellTextBrush", "#073642"),
        ("GridCellOutlineBrush", "#93A1A1"), ("GridHeaderBackgroundBrush", "#93A1A1"),
        ("GridHeaderTextBrush", "#073642"), ("GridTitleBackgroundBrush", "#268BD2"), ("GridTitleTextBrush", "#FDF6E3"),
        ("RowSelectedBrush", "#CFE6F0"), ("RowHoverBrush", "#EEE8D5"), ("RowCursorBrush", "#268BD2"),
        ("DropTargetBrush", "#B58900"), ("ActivePanelFrameBrush", "#268BD2"),
        ("CompareUniqueBrush", "#D5E8C0"), ("CompareNewerBrush", "#CFE6F0"), ("CompareOlderBrush", "#E0DDD0"),
        ("CompareDifferentBrush", "#F0E6B0"), ("CompareInaccessibleBrush", "#F0C8C0"),
    };
}
