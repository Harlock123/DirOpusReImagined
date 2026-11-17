using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace DirOpusReImagined
{
    /// <summary>Says whether a newer DORI is on GitHub, and turns the daily check on or off. See <see cref="UpdateCheck"/>.</summary>
    public partial class UpdateWindow : Window
    {
        private string _url = UpdateCheck.ReleasesPage;

        public UpdateWindow() : this(null) { }

        /// <param name="known">A release already fetched (by the start-up check), so it isn't asked for again.</param>
        public UpdateWindow(ReleaseInfo? known)
        {
            InitializeComponent();
            AutoBox.IsChecked = UpdateCheck.AutoEnabled;
            AutoBox.IsCheckedChanged += (_, _) => UpdateCheck.AutoEnabled = AutoBox.IsChecked == true;
            OpenButton.Click += (_, _) => OpenUrl(_url);
            AgainButton.Click += async (_, _) => await CheckAsync();
            CloseButton.Click += (_, _) => Close();
            if (known != null) ShowRelease(known);
            else Opened += async (_, _) => await CheckAsync();
        }

        private async Task CheckAsync()
        {
            HeadText.Text = "Checking…";
            DetailText.Text = $"This is DORI {UpdateCheck.Current}.";
            AgainButton.IsEnabled = false;
            try
            {
                var latest = await UpdateCheck.GetLatestAsync();
                UpdateCheck.MarkChecked(DateTime.Now);
                if (latest == null) { HeadText.Text = "GitHub didn't say which release is the latest."; return; }
                ShowRelease(latest);
            }
            catch (Exception ex)
            {
                HeadText.Text = "Couldn't reach GitHub.";
                DetailText.Text = ex.Message + $"\nThis is DORI {UpdateCheck.Current}. Releases are listed at {UpdateCheck.ReleasesPage}.";
                OpenButton.IsVisible = true;
                _url = UpdateCheck.ReleasesPage;
            }
            finally { AgainButton.IsEnabled = true; }
        }

        private void ShowRelease(ReleaseInfo latest)
        {
            bool newer = UpdateCheck.IsNewer(latest.Version, UpdateCheck.Current);
            HeadText.Text = newer ? $"DORI {latest.Version} is available." : "DORI is up to date.";
            DetailText.Text = $"This is DORI {UpdateCheck.Current}. The latest release is {latest.Name}" +
                              (latest.Published is { } when ? $", from {when.ToLocalTime():d}." : ".") +
                              (newer ? " Download the zip for your system from the release page and put it in place of this one." : "");
            NotesBox.Text = latest.Notes;
            NotesBox.IsVisible = newer && latest.Notes.Length > 0;
            OpenButton.IsVisible = newer;
            _url = latest.Url;
        }

        private static void OpenUrl(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
            catch { /* no browser: the address is on screen */ }
        }
    }
}
