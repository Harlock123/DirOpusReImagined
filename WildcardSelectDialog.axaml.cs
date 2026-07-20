using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace DirOpusReImagined;

/// <summary>
/// Prompts for a wildcard/substring pattern to select or deselect matching entries. The dialog only
/// collects input; the caller (MainWindow) applies it to the active panel via the grid engine.
/// <see cref="ShowDialog{bool}"/> returns true on OK. Read <see cref="Pattern"/> and
/// <see cref="FilesOnly"/> afterward.
/// </summary>
public partial class WildcardSelectDialog : Window
{
    public string Pattern { get; private set; } = "";
    public bool FilesOnly { get; private set; }

    public WildcardSelectDialog()
    {
        InitializeComponent();
    }

    /// <param name="title">Window title, e.g. "Select by Pattern" or "Deselect by Pattern".</param>
    /// <param name="prompt">Instruction line shown above the input.</param>
    public WildcardSelectDialog(string title, string prompt) : this()
    {
        Title = title;
        PromptText.Text = prompt;
        Opened += (_, _) =>
        {
            PatternBox.Focus();
            PatternBox.SelectAll();
        };
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e) => Confirm();

    private void CancelButton_Click(object? sender, RoutedEventArgs e) => Close(false);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Enter || e.Key == Key.Return) { Confirm(); e.Handled = true; return; }
        if (e.Key == Key.Escape) { Close(false); e.Handled = true; return; }
        base.OnKeyDown(e);
    }

    private void Confirm()
    {
        Pattern = PatternBox.Text ?? "";
        FilesOnly = FilesOnlyCheck.IsChecked == true;
        if (string.IsNullOrWhiteSpace(Pattern)) return;   // require a pattern
        Close(true);
    }
}
