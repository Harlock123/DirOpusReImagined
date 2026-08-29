using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;

namespace DirOpusReImagined;

/// <summary>
/// One row of the command-button editor: a fixed slot number, and the <see cref="ButtonSettings"/>
/// occupying it (or null while the slot is unused).
///
/// This exists so the editor's list can be data-bound. The slots used to be 36 Buttons written out
/// by hand in the XAML, addressed from the code-behind by rebuilding their control names as
/// "LPB" + i inside three separate loops.
/// </summary>
public sealed class ButtonSlot : INotifyPropertyChanged
{
    /// <summary>Placeholder text the form shows for a slot nothing has been saved into yet.</summary>
    public const string UnsetContent = "{What Will Show}";

    private ButtonSettings? _settings;

    public ButtonSlot(int index) => Index = index;

    /// <summary>1-based slot number; matches the digits in the name written to the config file.</summary>
    public int Index { get; }

    /// <summary>
    /// The name persisted to Configuration.xml. Canonical "LpButton" casing on write; the reader
    /// tolerates either, and <c>MainWindow.ApplyButtonSettingsFromXml</c> normalises "LP" to "Lp".
    /// </summary>
    public string Name => "LpButton" + Index;

    public ButtonSettings? Settings
    {
        get => _settings;
        set { _settings = value; RefreshDisplay(); }
    }

    /// <summary>Defaults shown for an unused slot. Not stored until the user actually edits it.</summary>
    public static ButtonSettings Placeholder(string name) => new()
    {
        Name = name,
        Content = UnsetContent,
        Action = "{Action}",
        Args = "{Arguments}",
        Background = "LightGray",
        Foreground = "Black",
        HorizontalAlignment = "Center",
        VerticalAlignment = "Center",
        ShellExecute = "False",
        ShowWindow = "False",
        ToolTip = "{ToolTip}",
    };

    // ---- display-only properties, bound by the DataTemplate --------------------------------

    public string Number => Index.ToString();

    public bool IsConfigured => _settings != null;

    /// <summary>The button's label, or an explicit marker so empty slots read as empty.</summary>
    public string Label =>
        string.IsNullOrWhiteSpace(_settings?.Content) ? "(unused)" : _settings!.Content;

    /// <summary>The command underneath the label, so the list says what each button does.</summary>
    public string Detail => _settings?.Action ?? "";

    public FontStyle LabelStyle => IsConfigured ? FontStyle.Normal : FontStyle.Italic;

    public double LabelOpacity => IsConfigured ? 1.0 : 0.5;

    /// <summary>The configured background, previewed as a swatch. Falls back rather than throwing
    /// on a colour name Avalonia does not recognise.</summary>
    public IBrush Swatch
    {
        get
        {
            if (Color.TryParse(_settings?.Background ?? "", out var c)) return new SolidColorBrush(c);
            return Brushes.LightGray;
        }
    }

    /// <summary>Re-raises every bound property; call after mutating the held settings in place.</summary>
    public void RefreshDisplay()
    {
        OnPropertyChanged(nameof(IsConfigured));
        OnPropertyChanged(nameof(Label));
        OnPropertyChanged(nameof(Detail));
        OnPropertyChanged(nameof(LabelStyle));
        OnPropertyChanged(nameof(LabelOpacity));
        OnPropertyChanged(nameof(Swatch));
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
