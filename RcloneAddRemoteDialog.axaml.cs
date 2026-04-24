using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using DirOpusReImagined.FileSystem.Rclone;

namespace DirOpusReImagined;

public partial class RcloneAddRemoteDialog : Window
{
    private List<ProviderInfo> _providers = new();
    private readonly Dictionary<string, Control> _fieldControls = new();
    private ProviderInfo? _currentProvider;

    public bool RemoteAdded { get; private set; }

    public RcloneAddRemoteDialog()
    {
        InitializeComponent();
        AddButton.Click += async (_, _) => await OnSubmit();
        CancelButton.Click += (_, _) => Close();
        TypeBox.SelectionChanged += (_, _) => RenderFieldsForSelection();
        Opened += async (_, _) => await LoadProviders();
    }

    private async Task LoadProviders()
    {
        SetStatus("Loading providers…", Brushes.Gray);
        try
        {
            _providers = await RcloneRemoteManager.GetProvidersAsync();
            TypeBox.Items = _providers
                .Select(p => $"{p.Description}  ({p.Name})")
                .ToList();
            if (_providers.Count > 0) TypeBox.SelectedIndex = 0;
            SetStatus("", Brushes.Gray);
        }
        catch (Exception ex)
        {
            SetStatus($"Could not load providers: {ex.Message}", Brushes.OrangeRed);
            AddButton.IsEnabled = false;
        }
    }

    private void RenderFieldsForSelection()
    {
        FieldsPanel.Children.Clear();
        _fieldControls.Clear();
        _currentProvider = null;

        var idx = TypeBox.SelectedIndex;
        if (idx < 0 || idx >= _providers.Count) return;
        var provider = _providers[idx];
        _currentProvider = provider;

        // Show only non-advanced, non-hidden options that the user can fill in.
        var visible = provider.Options
            .Where(o => (o.Hide & 1) == 0 && !o.Advanced)
            .ToList();

        if (visible.Count == 0)
        {
            FieldsPanel.Children.Add(new TextBlock
            {
                Text = RcloneRemoteManager.IsOAuthProvider(provider)
                    ? "This provider uses OAuth — no fields to fill.\nClick Add to open your browser for authorization."
                    : "No basic fields for this provider.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.Gray,
            });
            return;
        }

        foreach (var opt in visible)
        {
            var labelText = opt.Name + (opt.Required ? "  *" : "");
            var label = new TextBlock
            {
                Text = labelText,
                FontWeight = FontWeight.Bold,
            };
            var help = new TextBlock
            {
                Text = opt.Help,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                FontSize = 11,
            };

            Control input;
            if (opt.Examples.Count > 0)
            {
                var combo = new ComboBox
                {
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                    Items = opt.Examples.Select(e => string.IsNullOrEmpty(e.Help) ? e.Value : $"{e.Value} — {e.Help}").ToList(),
                };
                input = combo;
            }
            else if (opt.Type == "bool")
            {
                var cb = new CheckBox { Content = "" };
                if (string.Equals(opt.DefaultStr, "true", StringComparison.OrdinalIgnoreCase))
                    cb.IsChecked = true;
                input = cb;
            }
            else
            {
                var tb = new TextBox
                {
                    Watermark = string.IsNullOrEmpty(opt.DefaultStr) ? "" : $"default: {opt.DefaultStr}",
                    PasswordChar = opt.Sensitive ? '●' : '\0',
                };
                input = tb;
            }

            _fieldControls[opt.Name] = input;
            FieldsPanel.Children.Add(label);
            FieldsPanel.Children.Add(help);
            FieldsPanel.Children.Add(input);
            FieldsPanel.Children.Add(new Border { Height = 4 });
        }
    }

    private async Task OnSubmit()
    {
        var name = NameBox.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(name))
        {
            SetStatus("Enter a remote name.", Brushes.OrangeRed);
            return;
        }
        if (_currentProvider is null)
        {
            SetStatus("Pick a provider type.", Brushes.OrangeRed);
            return;
        }

        AddButton.IsEnabled = false;
        CancelButton.IsEnabled = false;

        var parameters = new Dictionary<string, string>();
        foreach (var opt in _currentProvider.Options.Where(o => (o.Hide & 1) == 0 && !o.Advanced))
        {
            if (!_fieldControls.TryGetValue(opt.Name, out var ctl)) continue;
            var value = ReadFieldValue(ctl, opt);
            if (!string.IsNullOrEmpty(value)) parameters[opt.Name] = value;
        }

        try
        {
            if (RcloneRemoteManager.IsOAuthProvider(_currentProvider))
            {
                SetStatus(
                    $"Opening browser for {_currentProvider.Description} authorization…\n" +
                    "Complete sign-in in your browser. This window will update when done.",
                    Brushes.DodgerBlue);

                var type = _currentProvider.Name;
                var token = await Task.Run(() => RcloneRemoteManager.AuthorizeAsync(type));
                parameters["token"] = token;

                SetStatus("Creating remote…", Brushes.DodgerBlue);
            }
            else
            {
                SetStatus("Creating remote…", Brushes.DodgerBlue);
            }

            await RcloneRemoteManager.CreateAsync(name, _currentProvider.Name, parameters);

            RemoteAdded = true;
            Close();
        }
        catch (Exception ex)
        {
            SetStatus($"Failed: {ex.Message}", Brushes.OrangeRed);
            AddButton.IsEnabled = true;
            CancelButton.IsEnabled = true;
        }
    }

    private static string ReadFieldValue(Control ctl, ProviderOption opt)
    {
        return ctl switch
        {
            TextBox tb  => tb.Text ?? "",
            CheckBox cb => cb.IsChecked == true ? "true" : "false",
            ComboBox cb => ExtractValueFromExample(cb.SelectedItem as string, opt),
            _ => "",
        };
    }

    private static string ExtractValueFromExample(string? selected, ProviderOption opt)
    {
        if (string.IsNullOrEmpty(selected)) return "";
        // The combo shows "<value> — <help>"; strip to just the value.
        var dashIdx = selected.IndexOf(" — ", StringComparison.Ordinal);
        return dashIdx > 0 ? selected.Substring(0, dashIdx) : selected;
    }

    private void SetStatus(string text, IBrush color)
    {
        StatusText.Text = text;
        StatusText.Foreground = color;
    }
}
