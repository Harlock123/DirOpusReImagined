using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Xml.Linq;
using Avalonia.Input;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>
    /// The command line under the panels (Ctrl/Cmd+Shift+L): a shell command run in the active
    /// panel's folder, with the button tokens (%AF%, %ASK%…) filled in, its output shown in the
    /// Command Output window. ↑ and ↓ go through earlier commands, which are remembered.
    /// </summary>
    public partial class MainWindow
    {
        private readonly CommandLineHistory _commandHistory = new();
        private CommandOutputWindow? _commandOutput;
        private TaiDataGrid? _commandPanel;

        private void InitCommandLine()
        {
            _commandHistory.Load((ConfigSettings.Load("CommandHistory", _configFilePath) ?? "")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries));
            CommandRunButton.Click += (_, _) => RunCommandLine();
            CommandLineBox.GotFocus += (_, _) => _commandPanel ??= _activeGrid ?? LPgrid;
            CommandLineBox.AddHandler(KeyDownEvent, (_, e) =>
            {
                switch (e.Key)
                {
                    case Key.Enter: RunCommandLine(); e.Handled = true; break;
                    case Key.Up when _commandHistory.Older() is { } older: SetCommandText(older); e.Handled = true; break;
                    case Key.Down when _commandHistory.Newer() is { } newer: SetCommandText(newer); e.Handled = true; break;
                    case Key.Escape: (_commandPanel ?? LPgrid).Focus(); _commandPanel = null; e.Handled = true; break;
                }
            }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            CommandLineBox.LostFocus += (_, _) => _commandPanel = null;
        }

        private void SetCommandText(string text)
        {
            CommandLineBox.Text = text;
            CommandLineBox.CaretIndex = text.Length;
        }

        private void FocusCommandLine(bool isLeft)
        {
            _commandPanel = isLeft ? LPgrid : RPgrid;
            CommandLineBox.Focus();
        }

        private async void RunCommandLine()
        {
            string command = (CommandLineBox.Text ?? "").Trim();
            if (command.Length == 0) return;
            var panel = _commandPanel ?? _activeGrid ?? LPgrid;
            bool isLeft = ReferenceEquals(panel, LPgrid);
            string folder = (isLeft ? LPpath.Text : RPpath.Text) ?? "";
            if (ProviderRegistry.For(folder) is not LocalFileProvider || !System.IO.Directory.Exists(folder))
            {
                await new MessageBox("Commands run in a folder on this computer. The active panel is showing cloud storage or an archive.",
                                     "Command line").ShowDialog(this);
                return;
            }

            // The button tokens, asked-for values first.
            string line = command;
            if (line.Contains('%'))
            {
                var questions = ButtonPrompts.Questions(line);
                if (questions.Count > 0)
                {
                    var answers = new System.Collections.Generic.Dictionary<string, string>();
                    foreach (var q in questions)
                    {
                        string? answer = await new TextPromptDialog("Command line", q) { AllowEmpty = true }.ShowDialog<string?>(this);
                        if (answer == null) return;
                        answers[q] = answer;
                    }
                    line = ButtonPrompts.Fill(line, answers);
                }
                line = ParseTheArgs(line);
                if (line == "%ERROR%") return;
            }

            _commandHistory.Add(command);
            ConfigSettings.Save("CommandHistory", string.Join("\n", _commandHistory.Items), _configFilePath ?? ConfigFile.Resolve());
            CommandLineBox.Text = "";

            if (_commandOutput == null)
            {
                _commandOutput = new CommandOutputWindow();
                _commandOutput.Closed += (_, _) => _commandOutput = null;
                _commandOutput.Show(this);
            }
            var output = _commandOutput;
            if (output.IsRunning)
            {
                await new MessageBox("A command is still running. Wait for it, or press Stop in the Command Output window.", "Command line").ShowDialog(this);
                return;
            }

            var psi = new ProcessStartInfo
            {
                WorkingDirectory = folder,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                // cmd.exe reads its own command line; passing it as one string keeps its quoting rules.
                psi.FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                psi.Arguments = "/d /s /c \"" + line + "\"";
            }
            else
            {
                var (shell, args) = CommandLineHistory.ShellFor(line);
                psi.FileName = shell;
                foreach (var a in args) psi.ArgumentList.Add(a);
            }

            Process p;
            try { p = Process.Start(psi)!; }
            catch (Exception ex)
            {
                output.Append($"❯ {line}\nCouldn't start it: {ex.Message}\n");
                return;
            }
            p.StandardInput.Close();   // nothing to type into it: a command waiting for input ends instead of hanging
            var clock = Stopwatch.StartNew();
            output.Started(p, line, folder);
            output.Activate();
            CommandLineBox.Focus();

            var outTask = Pump(p.StandardOutput, output);
            var errTask = Pump(p.StandardError, output);
            await Task.WhenAll(outTask, errTask, p.WaitForExitAsync());
            output.Finished(p.ExitCode, clock.Elapsed);
            p.Dispose();
            RefreshPanelsShowing(folder);
        }

        private static async Task Pump(System.IO.StreamReader reader, CommandOutputWindow output)
        {
            var buffer = new char[4096];
            int n;
            try
            {
                while ((n = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    output.Append(new string(buffer, 0, n));
            }
            catch { /* the process was stopped */ }
        }
    }
}
