using System;
using System.Linq;

namespace DirOpusReImagined
{
    /// <summary>Folder rules (right-click → File Tools → Folder Rules…), run by <see cref="FolderRuleRunner"/> while DORI is open.</summary>
    public partial class MainWindow
    {
        private FolderRuleRunner? _ruleRunner;
        private FolderRulesWindow? _rulesWindow;

        private void InitFolderRules()
        {
            _ruleRunner = new FolderRuleRunner();
            _ruleRunner.FoldersChanged += folders =>
            {
                foreach (var f in folders) RefreshPanelsShowing(f);
            };
            _ruleRunner.Logged += line =>
            {
                // A short note in the status bar when a rule did something by itself.
                if (_rulesWindow == null && line.Contains(" file")) LeftStatusText.Text = "Folder rule — " + line.Substring(10);
            };
            try { _ruleRunner.Start(); }
            catch (Exception ex) { CrashLog.Write("Folder rules", ex); }
            Closing += (_, _) => _ruleRunner?.Dispose();
        }

        private void OpenFolderRules(bool isLeft)
        {
            if (_ruleRunner == null) return;
            if (_rulesWindow != null) { _rulesWindow.Activate(); return; }
            string? Local(string? p) => !string.IsNullOrEmpty(p) && System.IO.Path.IsPathFullyQualified(p) && System.IO.Directory.Exists(p) ? p : null;
            _rulesWindow = new FolderRulesWindow(_ruleRunner,
                () => Local(isLeft ? LPpath.Text : RPpath.Text),
                () => Local(isLeft ? RPpath.Text : LPpath.Text));
            _rulesWindow.Closed += (_, _) => _rulesWindow = null;
            _rulesWindow.Show(this);
        }
    }
}
