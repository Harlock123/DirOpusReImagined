using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;

namespace DirOpusReImagined
{
    /// <summary>Lists, edits and runs the folder rules. Changes are kept when Save Rule is pressed.</summary>
    public partial class FolderRulesWindow : Window
    {
        private readonly FolderRuleRunner _runner;
        private readonly Func<string?> _panelFolder, _otherFolder;
        private readonly List<FolderRule> _rules;
        private int _current = -1;
        private bool _loading;

        public FolderRulesWindow() : this(new FolderRuleRunner(), () => null, () => null) { }

        public FolderRulesWindow(FolderRuleRunner runner, Func<string?> panelFolder, Func<string?> otherFolder)
        {
            InitializeComponent();
            _runner = runner;
            _panelFolder = panelFolder;
            _otherFolder = otherFolder;
            _rules = runner.Rules.Select(r => r.Clone()).ToList();

            NewButton.Click += (_, _) => AddRule(new FolderRule { Folder = _panelFolder() ?? "", Target = _otherFolder() ?? "" });
            DuplicateButton.Click += (_, _) => { if (Current is { } r) { var c = r.Clone(); c.Name += " (copy)"; AddRule(c); } };
            DeleteButton.Click += (_, _) => DeleteRule();
            SaveButton.Click += (_, _) => SaveRule();
            RunButton.Click += (_, _) => RunRule();
            RuleList.SelectionChanged += (_, _) => Show(RuleList.SelectedIndex);
            ActionBox.SelectionChanged += (_, _) => UpdateTargetEditor();
            FolderFromPanelButton.Click += (_, _) => { if (_panelFolder() is { } f) FolderBox.Text = f; };
            TargetFromPanelButton.Click += (_, _) => { if (_otherFolder() is { } f) TargetBox.Text = f; };

            LogBox.Text = string.Join("\n", runner.Log);
            Action<string> onLog = line =>
            {
                LogBox.Text = string.IsNullOrEmpty(LogBox.Text) ? line : LogBox.Text + "\n" + line;
                LogBox.CaretIndex = LogBox.Text.Length;
            };
            runner.Logged += onLog;
            Closed += (_, _) => runner.Logged -= onLog;

            RefreshList();
            Show(_rules.Count > 0 ? 0 : -1);
        }

        private FolderRule? Current => _current >= 0 && _current < _rules.Count ? _rules[_current] : null;

        private void RefreshList()
        {
            _loading = true;
            RuleList.ItemsSource = _rules.Select(r => (r.Enabled ? (r.Watch ? "◉ " : "● ") : "○ ") + r.Name).ToList();
            RuleList.SelectedIndex = _current;
            _loading = false;
        }

        private void Show(int index)
        {
            if (_loading) return;
            _current = index;
            var r = Current;
            Editor.IsEnabled = r != null;
            DuplicateButton.IsEnabled = DeleteButton.IsEnabled = r != null;
            ProblemText.Text = r == null ? "Press New to make a rule." : "";
            r ??= new FolderRule { Name = "" };
            NameBox.Text = r.Name;
            FolderBox.Text = r.Folder;
            MatchBox.Text = r.Match;
            AgeBox.Text = r.OlderThanDays > 0 ? r.OlderThanDays.ToString("0.##", CultureInfo.CurrentCulture) : "";
            ActionBox.SelectedIndex = (int)r.Action;
            TargetBox.Text = r.Action is RuleAction.MoveTo or RuleAction.CopyTo or RuleAction.AddTag ? r.Target : "";
            ColourBox.SelectedIndex = Enum.TryParse(r.Target, true, out LabelColour c) && c != LabelColour.None ? (int)c - 1 : 0;
            EnabledBox.IsChecked = r.Enabled;
            WatchBox.IsChecked = r.Watch;
            UpdateTargetEditor();
        }

        private void UpdateTargetEditor()
        {
            var action = (RuleAction)Math.Max(0, ActionBox.SelectedIndex);
            bool folder = action is RuleAction.MoveTo or RuleAction.CopyTo;
            TargetLabel.IsVisible = action != RuleAction.Trash;
            TargetLabel.Content = action switch
            {
                RuleAction.MoveTo => "Move them into this folder",
                RuleAction.CopyTo => "Copy them into this folder (a same-size copy already there is left alone)",
                RuleAction.SetColour => "Colour",
                RuleAction.AddTag => "Tag",
                _ => "",
            };
            TargetPanel.IsVisible = folder || action == RuleAction.AddTag;
            TargetFromPanelButton.IsVisible = folder;
            TargetBox.Watermark = folder ? "e.g. /home/you/Documents/PDFs" : "e.g. invoices";
            ColourBox.IsVisible = action == RuleAction.SetColour;
        }

        /// <summary>The editor's fields as a rule (not yet stored).</summary>
        private FolderRule FromEditor()
        {
            var action = (RuleAction)Math.Max(0, ActionBox.SelectedIndex);
            double.TryParse(AgeBox.Text, NumberStyles.Float, CultureInfo.CurrentCulture, out double age);
            return new FolderRule
            {
                Name = string.IsNullOrWhiteSpace(NameBox.Text) ? "Rule" : NameBox.Text.Trim(),
                Folder = (FolderBox.Text ?? "").Trim(),
                Match = string.IsNullOrWhiteSpace(MatchBox.Text) ? "*" : MatchBox.Text.Trim(),
                OlderThanDays = Math.Max(0, age),
                Action = action,
                Target = action switch
                {
                    RuleAction.SetColour => ((LabelColour)(ColourBox.SelectedIndex + 1)).ToString(),
                    RuleAction.Trash => "",
                    _ => (TargetBox.Text ?? "").Trim(),
                },
                Enabled = EnabledBox.IsChecked == true,
                Watch = WatchBox.IsChecked == true,
            };
        }

        private void AddRule(FolderRule rule)
        {
            _rules.Add(rule);
            _current = _rules.Count - 1;
            RefreshList();
            Show(_current);
            NameBox.Focus();
            ProblemText.Text = "Fill in the rule, then press Save Rule.";
        }

        private async void DeleteRule()
        {
            if (Current is not { } r) return;
            bool sure = await new MessageBox($"Delete the rule \"{r.Name}\"?", true, "Delete", "Cancel", "Folder Rules").ShowDialog<bool>(this);
            if (!sure) return;
            _rules.RemoveAt(_current);
            _runner.Replace(_rules.Select(x => x.Clone()).ToList());
            _current = Math.Min(_current, _rules.Count - 1);
            RefreshList();
            Show(_current);
        }

        /// <summary>Checks the editor's rule and keeps it. Returns it, or null when it has a problem.</summary>
        private FolderRule? SaveRule()
        {
            if (Current == null) return null;
            var rule = FromEditor();
            if (rule.Problem() is { } problem)
            {
                ProblemText.Text = problem;
                return null;
            }
            if (!System.IO.Directory.Exists(rule.Folder))
            {
                ProblemText.Text = $"There is no folder {rule.Folder}.";
                return null;
            }
            _rules[_current] = rule;
            try { _runner.Replace(_rules.Select(x => x.Clone()).ToList()); }
            catch (Exception ex)
            {
                ProblemText.Text = "The rules couldn't be saved: " + ex.Message;
                return null;
            }
            ProblemText.Text = "Saved. " + rule.Summary() + (rule.Enabled && rule.Watch ? " (watching)" : "");
            RefreshList();
            return rule;
        }

        private async void RunRule()
        {
            var rule = SaveRule();
            if (rule == null) return;
            var files = await _runner.PreviewAsync(rule);
            if (files.Count == 0)
            {
                ProblemText.Text = "Nothing to do: no file in the folder matches the rule right now.";
                return;
            }
            string list = string.Join("\n", files.Take(12).Select(System.IO.Path.GetFileName));
            if (files.Count > 12) list += $"\n… and {files.Count - 12} more";
            bool go = await new MessageBox($"{rule.Summary()}\n\nThis takes {files.Count} file{(files.Count == 1 ? "" : "s")}:\n{list}",
                                           true, "Do It", "Cancel", "Run " + rule.Name).ShowDialog<bool>(this);
            if (!go) return;
            int done = await _runner.RunAsync(rule, "run by hand", files);
            ProblemText.Text = $"Done: {done} file{(done == 1 ? "" : "s")}.";
        }
    }
}
