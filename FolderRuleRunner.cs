using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;

namespace DirOpusReImagined
{
    /// <summary>
    /// Runs the folder rules while DORI is open: watches the folders of watching rules (a burst of
    /// arrivals is handled once, a few seconds after it settles), looks again every hour for rules
    /// with an age test, and runs any rule on request. Runs are one at a time. What each run does
    /// goes to <see cref="Log"/>.
    /// </summary>
    public sealed class FolderRuleRunner : IDisposable
    {
        private readonly List<FileSystemWatcher> _watchers = new();
        private readonly Dictionary<FolderRule, Timer> _pending = new();
        private readonly SemaphoreSlim _oneAtATime = new(1, 1);
        private readonly List<string> _log = new();
        private Timer? _hourly;
        private bool _disposed;

        public List<FolderRule> Rules { get; private set; } = new();

        /// <summary>The newest 300 lines of what the rules did.</summary>
        public IReadOnlyList<string> Log { get { lock (_log) return _log.ToList(); } }

        /// <summary>Raised (on the UI thread) with a new log line.</summary>
        public event Action<string>? Logged;

        /// <summary>Raised (on the UI thread) with the folders a run changed, so panels showing them can refresh.</summary>
        public event Action<IReadOnlyCollection<string>>? FoldersChanged;

        public void Start()
        {
            Rules = RuleStore.Load();
            Rewatch();
            _hourly = new Timer(_ => RunWatching("hourly look"), null, TimeSpan.FromSeconds(20), TimeSpan.FromHours(1));
        }

        /// <summary>Replaces the rules, saves them, and watches the folders of the watching ones.</summary>
        public void Replace(List<FolderRule> rules)
        {
            Rules = rules;
            RuleStore.Save(rules);
            Rewatch();
        }

        private void Rewatch()
        {
            foreach (var w in _watchers) w.Dispose();
            _watchers.Clear();
            lock (_pending)
            {
                foreach (var t in _pending.Values) t.Dispose();
                _pending.Clear();
            }
            foreach (var rule in Rules.Where(r => r.Enabled && r.Watch && r.Problem() == null))
            {
                if (!Directory.Exists(rule.Folder)) continue;
                try
                {
                    var w = new FileSystemWatcher(rule.Folder)
                    {
                        IncludeSubdirectories = false,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    };
                    var r = rule;
                    w.Created += (_, _) => Soon(r);
                    w.Renamed += (_, _) => Soon(r);
                    w.Changed += (_, _) => Soon(r);
                    w.EnableRaisingEvents = true;
                    _watchers.Add(w);
                }
                catch (Exception ex) { Write($"{rule.Name}: can't watch {rule.Folder} ({ex.Message})"); }
            }
        }

        /// <summary>Runs the rule once things have been quiet for a moment past the settle time.</summary>
        private void Soon(FolderRule rule, TimeSpan? after = null)
        {
            if (_disposed) return;
            var delay = after ?? FolderRuleEngine.SettleTime + TimeSpan.FromSeconds(1);
            lock (_pending)
            {
                if (_pending.TryGetValue(rule, out var t)) { t.Change(delay, Timeout.InfiniteTimeSpan); return; }
                _pending[rule] = new Timer(_ =>
                {
                    lock (_pending) { if (_pending.Remove(rule, out var done)) done.Dispose(); }
                    _ = RunAsync(rule, "files arrived");
                }, null, delay, Timeout.InfiniteTimeSpan);
            }
        }

        private void RunWatching(string why)
        {
            foreach (var rule in Rules.Where(r => r.Enabled && r.Watch).ToList())
                _ = RunAsync(rule, why);
        }

        /// <summary>The files the rule would take now.</summary>
        public async Task<List<string>> PreviewAsync(FolderRule rule)
        {
            var files = await Task.Run(() => FolderRuleEngine.FilesIn(rule.Folder));
            // Labels are read on the UI thread, where they're changed.
            return await Dispatcher.UIThread.InvokeAsync(() =>
                FolderRuleEngine.Candidates(rule, files, DateTime.Now, LabelStore.Current.Get));
        }

        /// <summary>Runs the rule on the files it takes now (or just <paramref name="only"/>). Returns how many it changed.</summary>
        public async Task<int> RunAsync(FolderRule rule, string why, IReadOnlyCollection<string>? only = null)
        {
            if (_disposed || rule.Problem() != null) return 0;
            await _oneAtATime.WaitAsync();
            try
            {
                if (!Directory.Exists(rule.Folder)) return 0;
                var candidates = only?.ToList() ?? await PreviewAsync(rule);
                if (candidates.Count == 0) return 0;

                int done = 0;
                var changed = new HashSet<string>(StringComparer.Ordinal) { rule.Folder };
                if (rule.Action is RuleAction.MoveTo or RuleAction.CopyTo) changed.Add(rule.Target);
                var labels = LabelStore.Current;
                await Task.Run(() =>
                {
                    foreach (var path in candidates)
                    {
                        if (_disposed) break;
                        try
                        {
                            if (!File.Exists(path)) continue;
                            string? what = FolderRuleEngine.Apply(rule, path, labels, a => Dispatcher.UIThread.Invoke(a));
                            if (what == null) continue;
                            done++;
                            Write($"{rule.Name}: {what}");
                        }
                        catch (Exception ex)
                        {
                            Write($"{rule.Name}: {Path.GetFileName(path)} — {ex.Message}");
                        }
                    }
                });
                if (done > 0)
                {
                    Write($"{rule.Name}: {done} file{(done == 1 ? "" : "s")} ({why})");
                    Dispatcher.UIThread.Post(() => FoldersChanged?.Invoke(changed));
                }
                return done;
            }
            finally { _oneAtATime.Release(); }
        }

        private void Write(string line)
        {
            string stamped = $"{DateTime.Now:HH:mm:ss}  {line}";
            lock (_log)
            {
                _log.Add(stamped);
                if (_log.Count > 300) _log.RemoveRange(0, _log.Count - 300);
            }
            Dispatcher.UIThread.Post(() => Logged?.Invoke(stamped));
        }

        public void Dispose()
        {
            _disposed = true;
            _hourly?.Dispose();
            foreach (var w in _watchers) w.Dispose();
            _watchers.Clear();
            lock (_pending)
            {
                foreach (var t in _pending.Values) t.Dispose();
                _pending.Clear();
            }
        }
    }
}
