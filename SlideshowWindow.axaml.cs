using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DirOpusReImagined.FileSystem.Preview;

namespace DirOpusReImagined
{
    /// <summary>
    /// A panel's pictures full screen, one after another, with keys for sorting photos as they
    /// go by: 1–7 set a colour label (0 clears it), Delete sends the picture to the trash (undo
    /// with Ctrl/Cmd+Z in a panel), and R turns the view (the file isn't changed).
    /// </summary>
    public partial class SlideshowWindow : Window
    {
        private readonly List<string> _paths = new();
        private int _index;
        private readonly DispatcherTimer _timer;
        private double _seconds = 4;
        private bool _playing = true;
        private int _turn;
        private int _loadId;
        private CancellationTokenSource? _cts;
        private readonly Dictionary<string, Bitmap> _cache = new();
        private readonly Func<string, Task<bool>>? _trash;
        private readonly Action<string, LabelColour>? _label;

        public SlideshowWindow()
        {
            InitializeComponent();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_seconds) };
            _timer.Tick += (_, _) => { if (!Advance(+1)) Pause(); };
        }

        /// <param name="trash">Moves a picture to the trash; true when it went.</param>
        /// <param name="label">Sets a picture's colour label (local files only).</param>
        public SlideshowWindow(IReadOnlyList<string> paths, int start, Func<string, Task<bool>>? trash, Action<string, LabelColour>? label) : this()
        {
            _paths.AddRange(paths);
            _index = Math.Clamp(start, 0, Math.Max(0, paths.Count - 1));
            _trash = trash;
            _label = label;
            KeyDown += OnKey;
            PointerPressed += (_, e) =>
            {
                var p = e.GetPosition(this);
                Advance(p.X < Bounds.Width / 3 ? -1 : +1);
            };
            Opened += (_, _) =>
            {
                Show(_index);
                _timer.Start();
                // The keys are shown for a few seconds, then H brings them back.
                DispatcherTimer.RunOnce(() => HelpBar.IsVisible = false, TimeSpan.FromSeconds(5));
            };
            Closed += (_, _) =>
            {
                _timer.Stop();
                _cts?.Cancel();
                foreach (var b in _cache.Values) b.Dispose();
                _cache.Clear();
            };
        }

        private void OnKey(object? sender, KeyEventArgs e)
        {
            e.Handled = true;
            switch (e.Key)
            {
                case Key.Escape: Close(); break;
                case Key.Space: if (_playing) Pause(); else Play(); break;
                case Key.Right or Key.Down or Key.PageDown or Key.Enter: Advance(+1); break;
                case Key.Left or Key.Up or Key.PageUp or Key.Back: Advance(-1); break;
                case Key.Home: Show(0); break;
                case Key.End: Show(_paths.Count - 1); break;
                case Key.OemPlus or Key.Add: SetSpeed(Math.Max(1, _seconds - 1)); break;
                case Key.OemMinus or Key.Subtract: SetSpeed(Math.Min(60, _seconds + 1)); break;
                case Key.R:
                    _turn = (_turn + (e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 270 : 90)) % 360;
                    Picture.RenderTransform = new RotateTransform(_turn);
                    break;
                case Key.I: InfoBar.IsVisible = !InfoBar.IsVisible; break;
                case Key.H: HelpBar.IsVisible = !HelpBar.IsVisible; break;
                case Key.Delete: _ = TrashCurrentAsync(); break;
                case >= Key.D0 and <= Key.D7: SetLabel(e.Key - Key.D0); break;
                case >= Key.NumPad0 and <= Key.NumPad7: SetLabel(e.Key - Key.NumPad0); break;
                default: e.Handled = false; break;
            }
        }

        private void SetLabel(int n)
        {
            if (_label == null || _paths.Count == 0) return;
            var colour = (LabelColour)n;   // 1 Red … 7 Grey, 0 none: the order of the Label menu
            _label(_paths[_index], colour);
            UpdateInfo();
        }

        private async Task TrashCurrentAsync()
        {
            if (_trash == null || _paths.Count == 0) return;
            bool was = _playing;
            Pause();
            string path = _paths[_index];
            if (!await _trash(path)) { if (was) Play(); return; }
            if (_cache.Remove(path, out var bmp)) bmp.Dispose();
            _paths.RemoveAt(_index);
            if (_paths.Count == 0) { Close(); return; }
            Show(Math.Min(_index, _paths.Count - 1));
            if (was) Play();
        }

        private void Play()
        {
            _playing = true;
            _timer.Start();
            UpdateInfo();
        }

        private void Pause()
        {
            _playing = false;
            _timer.Stop();
            UpdateInfo();
        }

        private void SetSpeed(double seconds)
        {
            _seconds = seconds;
            _timer.Interval = TimeSpan.FromSeconds(seconds);
            UpdateInfo();
        }

        private bool Advance(int delta)
        {
            int next = _index + delta;
            if (next < 0 || next >= _paths.Count) return false;
            Show(next);
            if (_playing) { _timer.Stop(); _timer.Start(); }   // a full turn for the picture just reached
            return true;
        }

        private async void Show(int index)
        {
            if (_paths.Count == 0) return;
            _index = index;
            _turn = 0;
            Picture.RenderTransform = null;
            int id = ++_loadId;
            UpdateInfo();
            string path = _paths[index];
            var bitmap = await LoadAsync(path);
            if (id != _loadId) return;
            Picture.Source = bitmap;
            LoadingText.Text = bitmap == null ? $"{FileViewer.LeafName(path)} couldn't be shown." : "";
            Prefetch(index + 1);
            Trim(index);
        }

        private async Task<Bitmap?> LoadAsync(string path)
        {
            if (_cache.TryGetValue(path, out var cached)) return cached;
            _cts ??= new CancellationTokenSource();
            try
            {
                var result = await PreviewRegistry.LoadAsync(path, FileViewer.LeafName(path), 256 * 1024, _cts.Token);
                if (result is PreviewResult.Image img && img.Bitmap is Bitmap b)
                {
                    _cache[path] = b;
                    return b;
                }
            }
            catch { }
            return null;
        }

        private async void Prefetch(int index)
        {
            if (index < 0 || index >= _paths.Count || _cache.ContainsKey(_paths[index])) return;
            await LoadAsync(_paths[index]);
        }

        /// <summary>Keeps only the pictures next to the one showing, so a long show doesn't fill memory.</summary>
        private void Trim(int index)
        {
            var keep = new HashSet<string>();
            for (int i = Math.Max(0, index - 1); i <= Math.Min(_paths.Count - 1, index + 1); i++) keep.Add(_paths[i]);
            foreach (var key in new List<string>(_cache.Keys))
            {
                if (keep.Contains(key)) continue;
                var b = _cache[key];
                _cache.Remove(key);
                if (!ReferenceEquals(Picture.Source, b)) b.Dispose();
            }
        }

        private void UpdateInfo()
        {
            if (_paths.Count == 0) return;
            string path = _paths[_index];
            var label = LabelStore.Current.Get(path);
            InfoText.Text = FileViewer.LeafName(path) + (label.IsEmpty ? "" : "   ·   " + label.Display);
            StateText.Text = $"{_index + 1} / {_paths.Count}   ·   " + (_playing ? $"playing, {_seconds:0} s each" : "paused");
        }
    }
}
