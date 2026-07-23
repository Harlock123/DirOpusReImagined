using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace DirOpusReImagined;

public partial class RenameFileInterface : Window
{
    private bool FrmCanceled;
    private readonly TaiDataGrid theGrid = null!;
    private readonly string thePath = "";
    private readonly bool _ShowHidden = true;

    private readonly TaiDataGrid theOtherGrid = null!;
    private readonly string theOtherPath = "";

    // The selection to rename, and the names of everything else already in the folder (for clash
    // detection). Captured once at construction.
    private readonly List<(string Name, bool IsDir)> _selection = new();
    private readonly List<string> _existingOtherNames = new();

    // Windows/macOS treat names case-insensitively for collision purposes; Linux does not.
    private static readonly bool CaseInsensitiveFs =
        RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    // Tints that read on both light and dark themes (semi-transparent overlays).
    private static readonly IBrush ConflictBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xE0, 0x55, 0x55));
    private static readonly IBrush UnchangedBrush = Brushes.Transparent;

    public bool Canceled => FrmCanceled;

    public RenameFileInterface()
    {
        InitializeComponent();
        OKButton.Click += OKButton_Click;
        CANCELButton.Click += CANCELButton_Click;
    }

    public RenameFileInterface(TaiDataGrid Thegrid, string ThePath, TaiDataGrid TheOtherGrid, string TheOtherPath, bool ShowHidden)
    {
        InitializeComponent();
        OKButton.Click += OKButton_Click;
        CANCELButton.Click += CANCELButton_Click;

        theGrid = Thegrid;
        // Raw panel path (NOT MakePathENVSafe): apply joins with the scheme-aware JoinPanelPath so
        // cloud:// and UNC paths are handled correctly.
        thePath = ThePath;
        theOtherGrid = TheOtherGrid;
        theOtherPath = TheOtherPath;
        _ShowHidden = ShowHidden;

        foreach (var o in theGrid.SelectedItems)
            if (o is AFileEntry af) _selection.Add((af.Name, af.Typ));

        var selectedNames = new HashSet<string>(_selection.Select(s => s.Name),
            CaseInsensitiveFs ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        foreach (var o in theGrid.Items)
            if (o is AFileEntry af && !selectedNames.Contains(af.Name))
                _existingOtherNames.Add(af.Name);

        // Live preview: any input change rebuilds it.
        foreach (var tb in new[] { PrefixTextBox, BasenameTextBox, SuffixTextBox, FindTextBox,
                                   ReplaceTextBox, NumStartBox, NumStepBox, NumWidthBox })
            tb.TextChanged += (_, _) => RebuildPreview();
        foreach (var cb in new[] { chkRegex, chkIgnoreCase, chkKeepExt })
        {
            cb.IsCheckedChanged += (_, _) => RebuildPreview();
        }
        cmbCase.SelectionChanged += (_, _) => RebuildPreview();

        // Seed with the current name so an untouched dialog is a visible no-op.
        BasenameTextBox.Text = RenameEngine.TokenName;

        Opened += (_, _) => RebuildPreview();
    }

    /// <summary>Reads the dialog's controls into a <see cref="RenameOptions"/>.</summary>
    private RenameOptions ReadOptions()
    {
        int start = ParseIntOr(NumStartBox.Text, 1);
        int step = ParseIntOr(NumStepBox.Text, 1);
        int width = Math.Max(0, ParseIntOr(NumWidthBox.Text, 0));
        var kase = (CaseTransform)Math.Clamp(cmbCase.SelectedIndex, 0, 3);

        return new RenameOptions(
            Prefix: PrefixTextBox.Text ?? "",
            Basename: BasenameTextBox.Text ?? "",
            Suffix: SuffixTextBox.Text ?? "",
            Find: FindTextBox.Text ?? "",
            Replace: ReplaceTextBox.Text ?? "",
            UseRegex: chkRegex.IsChecked == true,
            IgnoreCase: chkIgnoreCase.IsChecked == true,
            Case: kase,
            NumberStart: start,
            NumberStep: step,
            NumberWidth: width,
            KeepExtension: chkKeepExt.IsChecked == true);
    }

    private static int ParseIntOr(string? s, int fallback) =>
        int.TryParse(s, out var v) ? v : fallback;

    private IReadOnlyList<RenamePlanRow> BuildCurrentPlan() =>
        RenameEngine.BuildPlan(_selection, ReadOptions(), _existingOtherNames, CaseInsensitiveFs);

    private void RebuildPreview()
    {
        var plan = BuildCurrentPlan();

        // Surface a regex error once, above the preview.
        var regexErr = plan.FirstOrDefault(r => r.Status == RenameStatus.RegexError)?.Message;
        RegexErrorText.Text = regexErr ?? "";
        RegexErrorText.IsVisible = regexErr != null;

        PreviewPanel.Children.Clear();
        foreach (var r in plan)
            PreviewPanel.Children.Add(BuildPreviewRow(r));

        int willRename = plan.Count(r => r.WillRename);
        int blocked = plan.Count(r => r.Status is RenameStatus.DuplicateTarget or RenameStatus.ExistsOnDisk
                                                  or RenameStatus.Invalid or RenameStatus.RegexError);
        int unchanged = plan.Count(r => r.Status == RenameStatus.Unchanged);

        SummaryText.Text = blocked > 0
            ? $"{willRename} will be renamed · {blocked} skipped (conflicts) · {unchanged} unchanged"
            : $"{willRename} will be renamed · {unchanged} unchanged";

        OKButton.IsEnabled = willRename > 0;
    }

    private Control BuildPreviewRow(RenamePlanRow r)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,150"),
            Background = r.Status switch
            {
                RenameStatus.DuplicateTarget or RenameStatus.ExistsOnDisk
                    or RenameStatus.Invalid or RenameStatus.RegexError => ConflictBrush,
                _ => UnchangedBrush,
            },
        };

        // Leave Foreground unset for the normal case — assigning null yields an invisible brush
        // instead of inheriting the theme foreground. Only override to gray where we want it muted.
        var old = new TextBlock { Text = r.OldName, Margin = new Avalonia.Thickness(6, 3), TextTrimming = TextTrimming.CharacterEllipsis };
        var neu = new TextBlock
        {
            Text = r.Status == RenameStatus.Unchanged ? "—" : r.NewName,
            Margin = new Avalonia.Thickness(6, 3),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        if (r.Status == RenameStatus.Unchanged) neu.Foreground = Brushes.Gray;

        var status = new TextBlock
        {
            Text = StatusLabel(r.Status),
            Margin = new Avalonia.Thickness(6, 3),
        };
        if (r.Status != RenameStatus.Change) status.Foreground = Brushes.Gray;
        if (r.Message != null) ToolTip.SetTip(status, r.Message);

        Grid.SetColumn(old, 0);
        Grid.SetColumn(neu, 1);
        Grid.SetColumn(status, 2);
        grid.Children.Add(old);
        grid.Children.Add(neu);
        grid.Children.Add(status);
        return grid;
    }

    private static string StatusLabel(RenameStatus s) => s switch
    {
        RenameStatus.Change => "rename",
        RenameStatus.Unchanged => "unchanged",
        RenameStatus.DuplicateTarget => "duplicate",
        RenameStatus.ExistsOnDisk => "exists",
        RenameStatus.Invalid => "invalid",
        RenameStatus.RegexError => "regex error",
        _ => "",
    };

    // async void: guard the whole body so a fault is always reported, never a silent crash.
    private async void OKButton_Click(object? sender, RoutedEventArgs e)
    {
        try { await ApplyAsync(); }
        catch (Exception ex)
        {
            CrashLog.Write("Rename", ex);
            OKButton.IsEnabled = true;
            CANCELButton.IsEnabled = true;
            try
            {
                await new MessageBox(
                    $"The rename could not be completed:\n\n{ex.GetType().Name}: {ex.Message}\n\n" +
                    $"Details were written to:\n{CrashLog.LogPath}",
                    "Rename failed").ShowDialog(this);
            }
            catch { }
            Close();
        }
    }

    private async Task ApplyAsync()
    {
        FrmCanceled = false;
        if (theGrid == null) { Close(); return; }

        var plan = BuildCurrentPlan();
        if (plan.Count(r => r.WillRename) == 0) { Close(); return; }

        OKButton.IsEnabled = false;
        CANCELButton.IsEnabled = false;

        var errors = await FileUtility.ApplyRenamePlanAsync(thePath, plan);

        FileUtility.PopulateFilePanel(theGrid, thePath, _ShowHidden);
        if (theOtherPath == thePath)
            FileUtility.PopulateFilePanel(theOtherGrid, theOtherPath, _ShowHidden);

        if (errors.Count > 0)
        {
            const int maxShown = 10;
            string detail = string.Join("\n", errors.Take(maxShown));
            if (errors.Count > maxShown) detail += $"\n… and {errors.Count - maxShown} more.";
            await new MessageBox(
                $"{errors.Count} item(s) could not be renamed:\n\n{detail}",
                "Rename failed").ShowDialog(this);
        }

        Close();
    }

    private void CANCELButton_Click(object? sender, RoutedEventArgs e)
    {
        FrmCanceled = true;
        Close();
    }
}
