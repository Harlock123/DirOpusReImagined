using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform.Storage;

namespace DirOpusReImagined
{
    /// <summary>Run Script (right-click → File Tools): type or open a DORI script and run it. See <see cref="DoriScript"/>.</summary>
    public partial class RunScriptWindow : Window
    {
        private readonly Func<string, Action<string>, CancellationToken, Task<bool>>? _run;
        private CancellationTokenSource? _cts;
        private const string Example = "# An example: select the pictures in the active panel and copy them to the other one.\nselect *.jpg *.png\nif selected\n  copy\n  wait\nelse\n  message There are no pictures here.\nend\n";

        public RunScriptWindow() { InitializeComponent(); }

        public RunScriptWindow(Func<string, Action<string>, CancellationToken, Task<bool>> run) : this()
        {
            _run = run;
            ScriptBox.Text = ConfigSettings.Load("LastScript") is { Length: > 0 } last ? last : Example;
            RunButton.Click += async (_, _) => await RunAsync();
            StopButton.Click += (_, _) => _cts?.Cancel();
            OpenButton.Click += async (_, _) => await OpenAsync();
            SaveButton.Click += async (_, _) => await SaveAsync();
            CloseButton.Click += (_, _) => Close();
            ScriptBox.AddHandler(KeyDownEvent, async (_, e) =>
            {
                if (e.Key == Key.Enter && (e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta)))
                {
                    e.Handled = true;
                    await RunAsync();
                }
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            Closed += (_, _) => _cts?.Cancel();
        }

        private void Log(string line) => LogBox.Text = (LogBox.Text ?? "") + line + "\n";

        private async Task RunAsync()
        {
            if (_run == null || _cts != null) return;
            string script = ScriptBox.Text ?? "";
            ConfigSettings.Save("LastScript", script);
            _cts = new CancellationTokenSource();
            RunButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            LogBox.Text = "";
            Log($"— started {DateTime.Now:T}");
            bool ok = await _run(script, Log, _cts.Token);
            Log(ok ? "— finished" : "— stopped");
            _cts = null;
            RunButton.IsEnabled = true;
            StopButton.IsEnabled = false;
        }

        private static readonly FilePickerFileType ScriptType = new("DORI script") { Patterns = new[] { "*.dori", "*.txt" } };

        private async Task OpenAsync()
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Open a script", FileTypeFilter = new[] { ScriptType, FilePickerFileTypes.All } });
            if (files.FirstOrDefault()?.TryGetLocalPath() is { } path) ScriptBox.Text = await File.ReadAllTextAsync(path);
        }

        private async Task SaveAsync()
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Save the script", SuggestedFileName = "script.dori", DefaultExtension = "dori", FileTypeChoices = new[] { ScriptType },
            });
            if (file?.TryGetLocalPath() is { } path)
            {
                await File.WriteAllTextAsync(path, ScriptBox.Text ?? "");
                Log($"saved to {path}");
            }
        }
    }
}
