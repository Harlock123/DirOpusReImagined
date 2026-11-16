using System;
using System.Diagnostics;
using System.Text;
using Avalonia.Controls;
using Avalonia.Threading;

namespace DirOpusReImagined
{
    /// <summary>Shows what commands from the command line print, as they print it, with Stop for the running one.</summary>
    public partial class CommandOutputWindow : Window
    {
        private Process? _running;
        private readonly StringBuilder _text = new();
        private bool _flushQueued;
        private const int MaxChars = 400_000;

        public CommandOutputWindow()
        {
            InitializeComponent();
            StopButton.Click += (_, _) =>
            {
                try { _running?.Kill(entireProcessTree: true); } catch { }
            };
            ClearButton.Click += (_, _) => { _text.Clear(); OutputBox.Text = ""; };
            CloseButton.Click += (_, _) => Close();
        }

        public bool IsRunning => _running is { HasExited: false };

        public void Started(Process p, string command, string folder)
        {
            _running = p;
            StopButton.IsEnabled = true;
            Append($"{(_text.Length > 0 ? "\n" : "")}❯ {command}    ({folder})\n");
            StateText.Text = "Running…";
        }

        public void Finished(int exitCode, TimeSpan took)
        {
            _running = null;
            StopButton.IsEnabled = false;
            StateText.Text = exitCode == 0 ? $"Finished in {took.TotalSeconds:0.0} s" : $"Ended with code {exitCode} after {took.TotalSeconds:0.0} s";
            Append(exitCode == 0 ? "" : $"[exit code {exitCode}]\n");
        }

        /// <summary>Adds text from any thread; the box is updated at most every tenth of a second.</summary>
        public void Append(string s)
        {
            if (s.Length == 0) return;
            lock (_text)
            {
                _text.Append(s);
                if (_text.Length > MaxChars) _text.Remove(0, _text.Length - MaxChars);
                if (_flushQueued) return;
                _flushQueued = true;
            }
            DispatcherTimer.RunOnce(() =>
            {
                lock (_text)
                {
                    OutputBox.Text = _text.ToString();
                    _flushQueued = false;
                }
                OutputBox.CaretIndex = OutputBox.Text?.Length ?? 0;
            }, TimeSpan.FromMilliseconds(100));
        }
    }
}
