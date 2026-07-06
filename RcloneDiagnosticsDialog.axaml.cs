using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using DirOpusReImagined.FileSystem.Rclone;

namespace DirOpusReImagined;

public partial class RcloneDiagnosticsDialog : Window
{
    public RcloneDiagnosticsDialog()
    {
        InitializeComponent();
        Opened += async (_, _) => await RefreshAsync();
        RefreshButton.Click   += async (_, _) => await RefreshAsync();
        InstallButton.Click   += async (_, _) => await OnInstallClicked();
        AddRemoteButton.Click += async (_, _) => await OnAddRemoteClicked();
        CloseButton.Click     += (_, _) => Close();
    }

    private async Task OnAddRemoteClicked()
    {
        if (!RcloneService.IsInstalled())
        {
            LogBox.Text = "Install rclone first before adding remotes.";
            return;
        }

        var dlg = new RcloneAddRemoteDialog();
        await dlg.ShowDialog(this);
        if (dlg.RemoteAdded) await RefreshAsync();
    }

    private async Task OnDeleteRemoteClicked(string name)
    {
        var confirm = new MessageBox(
            $"Delete remote '{name}'? This only removes it from rclone.conf — files on the cloud are not affected.",
            showCancel: true, okText: "Delete", title: "Delete Remote");
        if (!await confirm.ShowDialog<bool>(this))
            return;

        try
        {
            await RcloneRemoteManager.DeleteAsync(name);
            await RefreshAsync();
        }
        catch (Exception ex)
        {
            await new MessageBox($"Delete failed: {ex.Message}", "Error").ShowDialog(this);
        }
    }

    private async Task RefreshAsync()
    {
        SetStatus(new (string, string)[]
        {
            ("Binary",   "(checking…)"),
            ("Running",  "—"),
            ("Endpoint", "—"),
            ("Config",   RcloneConfigPath()),
        });
        SetRemotes(null, null);
        LogBox.Text = "";

        if (!RcloneService.IsInstalled())
        {
            SetStatus(new (string, string)[]
            {
                ("Binary",   "(not installed)"),
                ("Running",  "no"),
                ("Endpoint", "—"),
                ("Config",   RcloneConfigPath()),
            });
            SetRemotes(null,
                $"rclone is not installed yet. Click Install rclone (pinned version {RcloneBinaryManager.PinnedVersion}, ~17 MB).");
            InstallButton.IsVisible = true;
            return;
        }

        InstallButton.IsVisible = false;

        List<string>? remotes = null;
        string? remotesError = null;
        try
        {
            var client = await RcloneService.GetClientAsync();
            using var doc = await client.PostAsync("config/listremotes");
            if (doc.RootElement.TryGetProperty("remotes", out var arr)
                && arr.ValueKind == JsonValueKind.Array)
            {
                remotes = new List<string>();
                foreach (var r in arr.EnumerateArray())
                    if (r.GetString() is string name) remotes.Add(name);
            }
        }
        catch (Exception ex)
        {
            remotesError = ex.Message;
        }

        var d = RcloneService.Daemon;
        SetStatus(new (string, string)[]
        {
            ("Binary",   RcloneService.BinaryPath ?? "(on PATH)"),
            ("Running",  RcloneService.IsRunning ? "yes" : "no"),
            ("Endpoint", d?.BaseUrl ?? "—"),
            ("Config",   RcloneConfigPath()),
        });

        SetRemotes(remotes, remotesError);

        var logSb = new StringBuilder();
        var requestLog = RcloneClient.RecentRequests;
        if (requestLog.Length > 0)
        {
            logSb.AppendLine("=== Recent rc requests ===");
            foreach (var line in requestLog) logSb.AppendLine(line);
            logSb.AppendLine();
        }
        if (d is not null)
        {
            logSb.AppendLine("=== rclone daemon log ===");
            foreach (var line in d.RecentLog) logSb.AppendLine(line);
        }
        LogBox.Text = logSb.ToString();

        LogScroller.ScrollToEnd();
    }

    private async Task OnInstallClicked()
    {
        InstallButton.IsEnabled = false;
        RefreshButton.IsEnabled = false;
        CloseButton.IsEnabled = false;

        var progress = new Progress<double>(pct =>
        {
            LogBox.Text = $"Downloading rclone {RcloneBinaryManager.PinnedVersion}... {(int)(pct * 100)}%";
            LogScroller.ScrollToEnd();
        });

        try
        {
            await RcloneService.InstallAsync(progress);
            LogBox.Text = $"rclone {RcloneBinaryManager.PinnedVersion} installed.";
        }
        catch (Exception ex)
        {
            LogBox.Text = $"Install failed:\n{ex}";
        }

        InstallButton.IsEnabled = true;
        RefreshButton.IsEnabled = true;
        CloseButton.IsEnabled = true;
        await RefreshAsync();
    }

    private void SetStatus((string label, string value)[] rows)
    {
        StatusPanel.Children.Clear();
        foreach (var (label, value) in rows)
        {
            var line = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 8 };
            line.Children.Add(new TextBlock { Text = $"{label}:", Width = 80, FontWeight = FontWeight.Bold });
            line.Children.Add(new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap });
            StatusPanel.Children.Add(line);
        }
    }

    private void SetRemotes(List<string>? remotes, string? hintOrError)
    {
        RemotesPanel.Children.Clear();

        var header = new TextBlock { FontWeight = FontWeight.Bold };
        if (remotes is null)
            header.Text = "Configured remotes";
        else if (remotes.Count == 0)
            header.Text = "Configured remotes: none";
        else
            header.Text = $"Configured remotes ({remotes.Count}):";
        RemotesPanel.Children.Add(header);

        if (remotes is { Count: > 0 })
        {
            foreach (var r in remotes)
            {
                var row = new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    Spacing = 8,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                };
                row.Children.Add(new TextBlock
                {
                    Text = $"  • {r}    →    cloud://{r}/",
                    FontFamily = "Menlo,Consolas,monospace",
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                });
                var deleteBtn = new Button
                {
                    Content = "Delete",
                    Padding = new Avalonia.Thickness(6, 2),
                    FontSize = 11,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                };
                var name = r;
                deleteBtn.Click += async (_, _) => await OnDeleteRemoteClicked(name);
                row.Children.Add(deleteBtn);
                RemotesPanel.Children.Add(row);
            }
        }

        if (!string.IsNullOrEmpty(hintOrError))
        {
            RemotesPanel.Children.Add(new TextBlock
            {
                Text = hintOrError,
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brushes.Gray,
                Margin = new Avalonia.Thickness(0, 6, 0, 0),
            });
        }

        RemotesPanel.Children.Add(new TextBlock
        {
            Text = "To add a new remote, run in a terminal:\n    rclone config",
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brushes.Gray,
            FontFamily = "Menlo,Consolas,monospace",
            Margin = new Avalonia.Thickness(0, 6, 0, 0),
        });
    }

    private static string RcloneConfigPath()
    {
        var env = Environment.GetEnvironmentVariable("RCLONE_CONFIG");
        if (!string.IsNullOrEmpty(env)) return env;

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "rclone", "rclone.conf");
        }

        var xdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrEmpty(xdg))
        {
            xdg = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config");
        }
        return Path.Combine(xdg, "rclone", "rclone.conf");
    }
}
