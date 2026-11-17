using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace DirOpusReImagined
{
    /// <summary>Settings (right-click → File Tools): where they live, export and import, and the language. See <see cref="SettingsTransfer"/>.</summary>
    public partial class SettingsWindow : Window
    {
        private readonly Action? _closeApp;
        private bool _ready;

        public SettingsWindow() { InitializeComponent(); }

        /// <param name="closeApp">Closes DORI, so a move or import can be done once everything is saved.</param>
        public SettingsWindow(Action closeApp) : this()
        {
            _closeApp = closeApp;
            UpdateWhere();
            PortableButton.Click += async (_, _) => await MoveAsync(SettingsTransfer.ProgramFolder, "beside the program");
            UserButton.Click += async (_, _) => await MoveAsync(SettingsTransfer.UserFolder, "in your user folder");
            ExportButton.Click += async (_, _) => await ExportAsync();
            ImportButton.Click += async (_, _) => await ImportAsync();
            CloseButton.Click += (_, _) => Close();
            LanguageBox.ItemsSource = Localization.Languages.Select(l => l.Name).ToList();
            LanguageBox.SelectedIndex = Math.Max(0, Array.FindIndex(Localization.Languages, l => l.Code == Localization.Current));
            LanguageBox.SelectionChanged += async (_, _) => await LanguageChangedAsync();
            _ready = true;
        }

        private void UpdateWhere()
        {
            bool portable = SettingsTransfer.IsPortable;
            WhereText.Text = $"In {SettingsTransfer.CurrentFolder}" +
                             (portable ? " — beside the program, so a copy of DORI on a USB stick carries its own settings."
                                       : " — your user folder.");
            PortableButton.IsEnabled = !portable;
            UserButton.IsEnabled = portable || !string.Equals(Path.GetFullPath(SettingsTransfer.CurrentFolder).TrimEnd('/', '\\'),
                                                               Path.GetFullPath(SettingsTransfer.UserFolder).TrimEnd('/', '\\'));
            if (!SettingsTransfer.CanWrite(SettingsTransfer.ProgramFolder))
            {
                PortableButton.IsEnabled = false;
                ToolTip.SetTip(PortableButton, "DORI can't write in the folder it runs from.");
            }
        }

        private async Task MoveAsync(string to, string where)
        {
            string from = SettingsTransfer.CurrentFolder;
            if (!await new MessageBox($"Keep the settings {where} ({to})? DORI closes, moves them, and starts again.", true, "Move Them", "Cancel", "Settings")
                    .ShowDialog<bool>(this)) return;
            SettingsTransfer.AfterExit(() => SettingsTransfer.Move(from, to));
            Close();
            _closeApp?.Invoke();
        }

        private static readonly FilePickerFileType ZipType = new("DORI settings") { Patterns = new[] { "*.zip" } };

        private async Task ExportAsync()
        {
            var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = "Export DORI's settings", SuggestedFileName = $"DORI settings {DateTime.Now:yyyy-MM-dd}.zip",
                DefaultExtension = "zip", FileTypeChoices = new[] { ZipType },
            });
            if (file?.TryGetLocalPath() is not { } path) return;
            try
            {
                int n = await Task.Run(() => SettingsTransfer.Export(path, SettingsTransfer.CurrentFolder, SettingsTransfer.ProgramFolder, CataloguesBox.IsChecked == true));
                StatusText.Text = $"Exported {n} file(s) to {path}.";
            }
            catch (Exception ex) { StatusText.Text = "Not exported: " + ex.Message; }
        }

        private async Task ImportAsync()
        {
            var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "Import DORI settings", FileTypeFilter = new[] { ZipType } });
            if (files.FirstOrDefault()?.TryGetLocalPath() is not { } path) return;
            var (entries, problem) = SettingsTransfer.Inspect(path);
            if (problem != null) { StatusText.Text = problem; return; }
            if (!await new MessageBox($"Use the {entries.Count} settings file(s) in {Path.GetFileName(path)} in place of the current ones? The current ones are backed up first. DORI closes and starts again.",
                                      true, "Import", "Cancel", "Settings").ShowDialog<bool>(this)) return;
            string folder = SettingsTransfer.CurrentFolder, program = SettingsTransfer.ProgramFolder;
            SettingsTransfer.AfterExit(() => SettingsTransfer.Import(path, folder, program));
            Close();
            _closeApp?.Invoke();
        }

        private async Task LanguageChangedAsync()
        {
            if (!_ready || LanguageBox.SelectedIndex < 0) return;
            var (code, name) = Localization.Languages[LanguageBox.SelectedIndex];
            if (code == Localization.Current) return;
            ConfigSettings.Save(Localization.Setting, code);
            if (await new MessageBox($"DORI will be in {name} when it starts again. Start it again now?", true, "Start Again", "Later", "Language").ShowDialog<bool>(this))
            {
                SettingsTransfer.AfterExit(() => { });
                Close();
                _closeApp?.Invoke();
            }
            else StatusText.Text = $"{name} is used from the next start.";
        }
    }
}
