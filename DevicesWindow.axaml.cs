using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using DirOpusReImagined.FileSystem;

namespace DirOpusReImagined
{
    /// <summary>Drives and Devices (drive button → Drives and Devices…): space on each drive, opening one, and ejecting it. See <see cref="Devices"/>.</summary>
    public partial class DevicesWindow : Window
    {
        public sealed class Row
        {
            public DeviceInfo Device { get; init; } = null!;
            public string Title => (Device.CanEject ? "⏏ " : "") + Device.Display;
            public string Detail => Device.Kind + (Device.Total > 0 ? $" · {ProgressFormat.Bytes(Device.Free)} free of {ProgressFormat.Bytes(Device.Total)}" : "") +
                                    (Device.Device != null ? $" · {Device.Device}" : "");
            public double UsedFraction => Device.UsedFraction;
            public string Tip => $"{Device.Root}\n{Device.UsedFraction:P0} used";
        }

        private readonly Action<bool, string>? _open;
        private readonly Func<DeviceInfo, Task<string?>>? _eject;

        public DevicesWindow()
        {
            InitializeComponent();
        }

        /// <param name="open">Opens a folder in the left (true) or right panel.</param>
        /// <param name="eject">Ejects a drive (moving panels off it first); returns null or what went wrong.</param>
        public DevicesWindow(Action<bool, string>? open, Func<DeviceInfo, Task<string?>>? eject) : this()
        {
            _open = open;
            _eject = eject;
            AskBox.IsChecked = DriveWatcher.AskOnPlugIn;
            AskBox.IsCheckedChanged += (_, _) => DriveWatcher.AskOnPlugIn = AskBox.IsChecked == true;
            DeviceList.SelectionChanged += (_, _) => UpdateButtons();
            DeviceList.DoubleTapped += (_, _) => OpenSelected(true);
            LeftButton.Click += (_, _) => OpenSelected(true);
            RightButton.Click += (_, _) => OpenSelected(false);
            EjectButton.Click += async (_, _) => await EjectSelectedAsync();
            RefreshButton.Click += async (_, _) => await LoadAsync();
            CloseButton.Click += (_, _) => Close();
            Opened += async (_, _) => await LoadAsync();
        }

        private DeviceInfo? Selected => (DeviceList.SelectedItem as Row)?.Device;

        private async Task LoadAsync()
        {
            var list = await Task.Run(Devices.List);
            DeviceList.ItemsSource = list.Select(d => new Row { Device = d }).ToList();
            StatusText.Text = list.Count == 0 ? "No drives were found." : "";
            UpdateButtons();
        }

        private void UpdateButtons()
        {
            var d = Selected;
            LeftButton.IsEnabled = RightButton.IsEnabled = d != null;
            EjectButton.IsEnabled = d is { CanEject: true } && _eject != null;
        }

        private void OpenSelected(bool left)
        {
            if (Selected is { } d) _open?.Invoke(left, d.Root);
        }

        private async Task EjectSelectedAsync()
        {
            if (Selected is not { } d || _eject == null) return;
            EjectButton.IsEnabled = false;
            StatusText.Text = $"Ejecting {d.Display}…";
            string? problem = await _eject(d);
            StatusText.Text = problem == null ? $"{d.Display} was ejected. It's safe to take out." : $"{d.Display} wasn't ejected: {problem}";
            await LoadAsync();
        }
    }
}
