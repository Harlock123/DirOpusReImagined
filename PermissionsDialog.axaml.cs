using System;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DirOpusReImagined;

public partial class PermissionsDialog : Window
{
    private readonly string _filePath;
    private bool _updatingUI;

    public PermissionsDialog()
    {
        InitializeComponent();
    }

    public PermissionsDialog(string filePath)
    {
        InitializeComponent();

        _filePath = filePath;
        FileNameLabel.Text = Path.GetFileName(filePath);

        ApplyButton.Click += ApplyButton_Click;
        CancelButton.Click += CancelButton_Click;

        // Wire up checkbox click events
        ChkOwnerRead.Click += OnCheckboxChanged;
        ChkOwnerWrite.Click += OnCheckboxChanged;
        ChkOwnerExecute.Click += OnCheckboxChanged;
        ChkGroupRead.Click += OnCheckboxChanged;
        ChkGroupWrite.Click += OnCheckboxChanged;
        ChkGroupExecute.Click += OnCheckboxChanged;
        ChkOtherRead.Click += OnCheckboxChanged;
        ChkOtherWrite.Click += OnCheckboxChanged;
        ChkOtherExecute.Click += OnCheckboxChanged;

        OctalTextBox.TextChanged += OctalTextBox_TextChanged;

        LoadPermissions();
    }

    private void LoadPermissions()
    {
        try
        {
            var mode = File.GetUnixFileMode(_filePath);
            _updatingUI = true;

            ChkOwnerRead.IsChecked = mode.HasFlag(UnixFileMode.UserRead);
            ChkOwnerWrite.IsChecked = mode.HasFlag(UnixFileMode.UserWrite);
            ChkOwnerExecute.IsChecked = mode.HasFlag(UnixFileMode.UserExecute);
            ChkGroupRead.IsChecked = mode.HasFlag(UnixFileMode.GroupRead);
            ChkGroupWrite.IsChecked = mode.HasFlag(UnixFileMode.GroupWrite);
            ChkGroupExecute.IsChecked = mode.HasFlag(UnixFileMode.GroupExecute);
            ChkOtherRead.IsChecked = mode.HasFlag(UnixFileMode.OtherRead);
            ChkOtherWrite.IsChecked = mode.HasFlag(UnixFileMode.OtherWrite);
            ChkOtherExecute.IsChecked = mode.HasFlag(UnixFileMode.OtherExecute);

            OctalTextBox.Text = ModeToOctal(mode);

            _updatingUI = false;
        }
        catch (Exception ex)
        {
            FileNameLabel.Text = $"Error reading permissions: {ex.Message}";
        }
    }

    private void OnCheckboxChanged(object? sender, RoutedEventArgs e)
    {
        if (_updatingUI) return;

        _updatingUI = true;
        OctalTextBox.Text = ModeToOctal(BuildModeFromCheckboxes());
        _updatingUI = false;
    }

    private void OctalTextBox_TextChanged(object? sender, TextChangedEventArgs e)
    {
        if (_updatingUI) return;
        if (string.IsNullOrEmpty(OctalTextBox.Text)) return;

        if (int.TryParse(OctalTextBox.Text, System.Globalization.NumberStyles.None,
                null, out int octalValue) && octalValue >= 0 && octalValue <= 777)
        {
            // Validate each digit is 0-7
            foreach (char c in OctalTextBox.Text)
            {
                if (c < '0' || c > '7') return;
            }

            var mode = OctalToMode(OctalTextBox.Text.PadLeft(3, '0'));
            _updatingUI = true;

            ChkOwnerRead.IsChecked = mode.HasFlag(UnixFileMode.UserRead);
            ChkOwnerWrite.IsChecked = mode.HasFlag(UnixFileMode.UserWrite);
            ChkOwnerExecute.IsChecked = mode.HasFlag(UnixFileMode.UserExecute);
            ChkGroupRead.IsChecked = mode.HasFlag(UnixFileMode.GroupRead);
            ChkGroupWrite.IsChecked = mode.HasFlag(UnixFileMode.GroupWrite);
            ChkGroupExecute.IsChecked = mode.HasFlag(UnixFileMode.GroupExecute);
            ChkOtherRead.IsChecked = mode.HasFlag(UnixFileMode.OtherRead);
            ChkOtherWrite.IsChecked = mode.HasFlag(UnixFileMode.OtherWrite);
            ChkOtherExecute.IsChecked = mode.HasFlag(UnixFileMode.OtherExecute);

            _updatingUI = false;
        }
    }

    private UnixFileMode BuildModeFromCheckboxes()
    {
        UnixFileMode mode = 0;

        if (ChkOwnerRead.IsChecked == true) mode |= UnixFileMode.UserRead;
        if (ChkOwnerWrite.IsChecked == true) mode |= UnixFileMode.UserWrite;
        if (ChkOwnerExecute.IsChecked == true) mode |= UnixFileMode.UserExecute;
        if (ChkGroupRead.IsChecked == true) mode |= UnixFileMode.GroupRead;
        if (ChkGroupWrite.IsChecked == true) mode |= UnixFileMode.GroupWrite;
        if (ChkGroupExecute.IsChecked == true) mode |= UnixFileMode.GroupExecute;
        if (ChkOtherRead.IsChecked == true) mode |= UnixFileMode.OtherRead;
        if (ChkOtherWrite.IsChecked == true) mode |= UnixFileMode.OtherWrite;
        if (ChkOtherExecute.IsChecked == true) mode |= UnixFileMode.OtherExecute;

        return mode;
    }

    private static string ModeToOctal(UnixFileMode mode)
    {
        int owner = (mode.HasFlag(UnixFileMode.UserRead) ? 4 : 0)
                   + (mode.HasFlag(UnixFileMode.UserWrite) ? 2 : 0)
                   + (mode.HasFlag(UnixFileMode.UserExecute) ? 1 : 0);

        int group = (mode.HasFlag(UnixFileMode.GroupRead) ? 4 : 0)
                   + (mode.HasFlag(UnixFileMode.GroupWrite) ? 2 : 0)
                   + (mode.HasFlag(UnixFileMode.GroupExecute) ? 1 : 0);

        int other = (mode.HasFlag(UnixFileMode.OtherRead) ? 4 : 0)
                   + (mode.HasFlag(UnixFileMode.OtherWrite) ? 2 : 0)
                   + (mode.HasFlag(UnixFileMode.OtherExecute) ? 1 : 0);

        return $"{owner}{group}{other}";
    }

    private static UnixFileMode OctalToMode(string octal)
    {
        UnixFileMode mode = 0;
        if (octal.Length < 3) return mode;

        int owner = octal[octal.Length - 3] - '0';
        int group = octal[octal.Length - 2] - '0';
        int other = octal[octal.Length - 1] - '0';

        if ((owner & 4) != 0) mode |= UnixFileMode.UserRead;
        if ((owner & 2) != 0) mode |= UnixFileMode.UserWrite;
        if ((owner & 1) != 0) mode |= UnixFileMode.UserExecute;
        if ((group & 4) != 0) mode |= UnixFileMode.GroupRead;
        if ((group & 2) != 0) mode |= UnixFileMode.GroupWrite;
        if ((group & 1) != 0) mode |= UnixFileMode.GroupExecute;
        if ((other & 4) != 0) mode |= UnixFileMode.OtherRead;
        if ((other & 2) != 0) mode |= UnixFileMode.OtherWrite;
        if ((other & 1) != 0) mode |= UnixFileMode.OtherExecute;

        return mode;
    }

    private void ApplyButton_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var newMode = BuildModeFromCheckboxes();
            File.SetUnixFileMode(_filePath, newMode);
            this.Close();
        }
        catch (Exception ex)
        {
            MessageBox mb = new MessageBox($"Failed to set permissions: {ex.Message}");
            mb.ShowDialog(this);
        }
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        this.Close();
    }
}
