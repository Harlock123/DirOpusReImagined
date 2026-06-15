using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace DirOpusReImagined;

/// <summary>
/// Confirmation + options for a sync. Await <c>ShowDialog&lt;bool&gt;(owner)</c> (true = Sync,
/// false = Cancel), then read <see cref="DeleteExtras"/> for whether destination-only items should be
/// deleted (mirror). Pass a <paramref name="deleteCount"/> of 0 to hide the mirror option entirely
/// (e.g. for two-way sync, which never deletes).
/// </summary>
public partial class SyncOptionsDialog : Window
{
    public bool DeleteExtras { get; private set; }

    public SyncOptionsDialog()
    {
        InitializeComponent();
    }

    public SyncOptionsDialog(string summary, int deleteCount)
    {
        InitializeComponent();

        summaryText.Text = summary;

        if (deleteCount > 0)
        {
            deleteCheck.Content =
                $"Also delete {deleteCount} item(s) that exist only in the destination (mirror)";
            deleteCheck.IsChecked = false;
            deleteCheck.IsCheckedChanged += (_, _) =>
                warnText.Text = deleteCheck.IsChecked == true
                    ? "Warning: deletion is permanent and cannot be undone."
                    : "";
        }
        else
        {
            deleteCheck.IsVisible = false;
        }

        okButton.Click += (_, _) => { DeleteExtras = deleteCheck.IsChecked == true; Close(true); };
        cancelButton.Click += (_, _) => Close(false);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
