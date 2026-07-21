using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace DirOpusReImagined;

/// <summary>
/// App-wide screenshot capture. Pressing the capture chord (Ctrl+Shift+P, or Cmd+Shift+P on
/// macOS) on ANY window or dialog renders that window's visual tree to a PNG and prompts for a
/// filename. Intended as a documentation aid for building the user manual.
///
/// The hotkey is wired once via a tunnelling class handler on <see cref="Window"/>, so every
/// window the app creates — including modal dialogs — gets it for free without per-window code.
/// Tunnelling means we see the key before any child control can swallow it, which is why it works
/// even inside the grid, text boxes, and dialogs that have their own keyboard handling.
/// </summary>
public static class ScreenshotService
{
    private static bool _registered;
    private static bool _busy;

    /// <summary>Call once at application startup (after the framework is initialized).</summary>
    public static void Register()
    {
        if (_registered) return;
        _registered = true;

        // Tunnel + handledEventsToo so the chord fires no matter which control would otherwise
        // handle the key first (grid, text boxes, dialog buttons, etc.).
        InputElement.KeyDownEvent.AddClassHandler<Window>(
            OnWindowKeyDown,
            RoutingStrategies.Tunnel,
            handledEventsToo: true);
    }

    private static void OnWindowKeyDown(Window window, KeyEventArgs e)
    {
        bool ctrlOrCmd = e.KeyModifiers.HasFlag(KeyModifiers.Control) ||
                         e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        if (e.Key == Key.P && ctrlOrCmd && shift)
        {
            e.Handled = true;
            _ = CaptureAndSaveAsync(window);
        }
    }

    private static async Task CaptureAndSaveAsync(Window window)
    {
        if (_busy) return;
        _busy = true;
        try
        {
            // Render the window's visual tree to a bitmap on the UI thread, up front, before any
            // dialog steals focus. This captures the app content (not OS window chrome), which is
            // exactly what a manual needs.
            RenderTargetBitmap bitmap;
            try
            {
                bitmap = RenderWindow(window);
            }
            catch (Exception ex)
            {
                await new MessageBox($"Could not capture the screen:\n{ex.Message}",
                    "Screenshot Failed").ShowDialog(window);
                return;
            }

            using (bitmap)
            {
                var file = await window.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Save Screenshot",
                    SuggestedFileName = DefaultName(window),
                    DefaultExtension = "png",
                    ShowOverwritePrompt = true,
                    FileTypeChoices = new[]
                    {
                        new FilePickerFileType("PNG Image") { Patterns = new[] { "*.png" } }
                    }
                });

                if (file is null) return; // user cancelled

                string? path = file.TryGetLocalPath();
                if (string.IsNullOrEmpty(path))
                {
                    await new MessageBox("That location is not a local file and cannot be saved to.",
                        "Screenshot Failed").ShowDialog(window);
                    return;
                }

                if (!path.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                    path += ".png";

                try
                {
                    using var fs = File.Create(path);
                    bitmap.Save(fs);
                }
                catch (Exception ex)
                {
                    await new MessageBox($"Could not save the screenshot:\n{ex.Message}",
                        "Screenshot Failed").ShowDialog(window);
                    return;
                }

                await new MessageBox($"Screenshot saved:\n{path}", "Screenshot Saved")
                    .ShowDialog(window);
            }
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>
    /// Renders a window's visual tree to a high-DPI-aware bitmap. Caller owns the returned bitmap
    /// (dispose it). Runs on the UI thread.
    /// </summary>
    internal static RenderTargetBitmap RenderWindow(Window window)
    {
        double scaling = window.RenderScaling <= 0 ? 1.0 : window.RenderScaling;
        var client = window.ClientSize;
        int pw = Math.Max(1, (int)Math.Ceiling(client.Width * scaling));
        int ph = Math.Max(1, (int)Math.Ceiling(client.Height * scaling));

        var bitmap = new RenderTargetBitmap(
            new Avalonia.PixelSize(pw, ph),
            new Avalonia.Vector(96 * scaling, 96 * scaling));
        bitmap.Render(window);
        return bitmap;
    }

    /// <summary>
    /// Builds a spaces-free, filesystem-safe default filename (max 40 chars) describing the active
    /// window, derived from its title. Spaces and separators become underscores.
    /// </summary>
    internal static string DefaultName(Window window)
    {
        // The main window's title is set dynamically at runtime (drive label + version, e.g.
        // "DORI 128 GB 0.1.16.0"), which is noisy and version-specific — not a description of the
        // screen. Always give it a stable, descriptive default.
        if (window is MainWindow)
            return "DORI_Main_Screen";

        string title = window.Title ?? "";
        if (string.IsNullOrWhiteSpace(title))
            title = window.GetType().Name;

        var sb = new StringBuilder(title.Length);
        foreach (char c in title.Trim())
        {
            if (char.IsLetterOrDigit(c)) sb.Append(c);
            else if (c == '_' || c == '-' || c == '.') sb.Append(c);
            else sb.Append('_'); // spaces, dashes like — –, punctuation, etc.
        }

        string name = sb.ToString();
        while (name.Contains("__")) name = name.Replace("__", "_");
        name = name.Trim('_', '-', '.');

        if (name.Length > 40)
            name = name.Substring(0, 40).Trim('_', '-', '.');

        return name.Length == 0 ? "Screenshot" : name;
    }
}
