using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Markup.Xaml;
using DirOpusReImagined.Bookmarks;

namespace DirOpusReImagined;

/// <summary>
/// Lists the persisted folder bookmarks and lets the user send a chosen bookmark to the
/// Left or Right panel. Bookmarks can be added from either panel's current folder and
/// removed here; every change is written straight back to BOOKMARKS.MD.
///
/// Colors follow the active Light/Dark theme: the chrome uses {DynamicResource} tokens in
/// XAML, and the dynamically built rows resolve the same tokens for the current theme.
/// </summary>
public partial class BookmarksDialog : Window
{
    private readonly string _leftPath;
    private readonly string _rightPath;
    private readonly Action<string> _onLeft;
    private readonly Action<string> _onRight;

    private List<Bookmark> _bookmarks = new();

    /// <summary>Design-time / XAML parameterless constructor.</summary>
    public BookmarksDialog() : this(string.Empty, string.Empty, _ => { }, _ => { })
    {
    }

    /// <param name="leftPath">The Left panel's current folder (used by "Add Left").</param>
    /// <param name="rightPath">The Right panel's current folder (used by "Add Right").</param>
    /// <param name="onLeft">Invoked with the chosen bookmark's path to load it into the Left panel.</param>
    /// <param name="onRight">Invoked with the chosen bookmark's path to load it into the Right panel.</param>
    public BookmarksDialog(string leftPath, string rightPath, Action<string> onLeft, Action<string> onRight)
    {
        InitializeComponent();

        _leftPath = leftPath ?? string.Empty;
        _rightPath = rightPath ?? string.Empty;
        _onLeft = onLeft ?? (_ => { });
        _onRight = onRight ?? (_ => { });

        CloseButton.Click += (_, _) => Close();
        AddLeftButton.Click += (_, _) => AddFromPanel(_leftPath);
        AddRightButton.Click += (_, _) => AddFromPanel(_rightPath);

        _bookmarks = BookmarkStore.Load();
        RebuildList();
    }

    /// <summary>Resolves a themed brush by resource key for the dialog's current theme variant.</summary>
    private IBrush ThemeBrush(string key, Color fallback)
        => this.TryFindResource(key, ActualThemeVariant, out var value) && value is IBrush brush
            ? brush
            : new SolidColorBrush(fallback);

    private void RebuildList()
    {
        BookmarksListPanel.Children.Clear();

        if (_bookmarks.Count == 0)
        {
            BookmarksListPanel.Children.Add(new TextBlock
            {
                Text = "No bookmarks yet. Add one from a panel's current folder below.",
                Foreground = ThemeBrush("MutedTextBrush", Colors.Gray),
                Margin = new Thickness(4, 8, 4, 8),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        foreach (var bookmark in _bookmarks)
        {
            BookmarksListPanel.Children.Add(CreateRow(bookmark));
        }
    }

    private Control CreateRow(Bookmark bookmark)
    {
        var textBrush = ThemeBrush("GridCellTextBrush", Colors.Black);
        var mutedBrush = ThemeBrush("MutedTextBrush", Colors.Gray);

        var grid = new Grid { Margin = new Thickness(2, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));   // name + path
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));   // ◀ Left
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));   // Right ▶
        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));   // remove

        // Name + path stacked in the first column.
        var info = new StackPanel { Orientation = Orientation.Vertical, VerticalAlignment = VerticalAlignment.Center };
        info.Children.Add(new TextBlock
        {
            Text = bookmark.Name,
            FontWeight = FontWeight.Bold,
            FontSize = 13,
            Foreground = textBrush
        });
        info.Children.Add(new TextBlock
        {
            Text = bookmark.Path,
            FontSize = 11,
            Foreground = mutedBrush,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(info, 0);
        grid.Children.Add(info);

        var leftButton = new Button
        {
            Content = "◀ Left",
            FontWeight = FontWeight.Bold,
            Padding = new Thickness(10, 3),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(leftButton, "Open this bookmark in the Left panel");
        leftButton.Click += (_, _) => { _onLeft(bookmark.Path); Close(); };
        Grid.SetColumn(leftButton, 1);
        grid.Children.Add(leftButton);

        var rightButton = new Button
        {
            Content = "Right ▶",
            FontWeight = FontWeight.Bold,
            Padding = new Thickness(10, 3),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(rightButton, "Open this bookmark in the Right panel");
        rightButton.Click += (_, _) => { _onRight(bookmark.Path); Close(); };
        Grid.SetColumn(rightButton, 2);
        grid.Children.Add(rightButton);

        var removeButton = new Button
        {
            Content = "✕",
            Padding = new Thickness(8, 3),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        ToolTip.SetTip(removeButton, "Remove this bookmark");
        removeButton.Click += (_, _) => RemoveBookmark(bookmark);
        Grid.SetColumn(removeButton, 3);
        grid.Children.Add(removeButton);

        return grid;
    }

    /// <summary>Bookmarks <paramref name="path"/>, naming it from the typed name or the folder's leaf.</summary>
    private void AddFromPanel(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return;

        var typed = NewNameBox.Text?.Trim();
        var name = string.IsNullOrEmpty(typed) ? LeafName(path) : typed;

        _bookmarks.Add(new Bookmark(name, path.Trim()));
        BookmarkStore.Save(_bookmarks);

        NewNameBox.Text = string.Empty;
        RebuildList();
    }

    private void RemoveBookmark(Bookmark bookmark)
    {
        _bookmarks.Remove(bookmark);
        BookmarkStore.Save(_bookmarks);
        RebuildList();
    }

    /// <summary>Best-effort display name from a path's last segment (handles cloud:// URIs too).</summary>
    private static string LeafName(string path)
    {
        var trimmed = path.TrimEnd('/', '\\');
        if (trimmed.Length == 0) return path;

        int slash = trimmed.LastIndexOfAny(new[] { '/', '\\' });
        var leaf = slash >= 0 ? trimmed[(slash + 1)..] : trimmed;
        return leaf.Length == 0 ? trimmed : leaf;
    }
}
