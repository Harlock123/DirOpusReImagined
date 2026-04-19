using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace DirOpusReImagined;

public partial class DriveInfoDialog : Window
{
    public DriveInfoDialog()
    {
        InitializeComponent();
        CloseButton.Click += (_, _) => Close();
        PopulateDriveInfo();
    }

    private void PopulateDriveInfo()
    {
        try
        {
            var drives = DriveInfo.GetDrives();

            foreach (var drive in drives)
            {
                try
                {
                    if (!drive.IsReady) continue;

                    var row = CreateDriveRow(drive);
                    DriveListPanel.Children.Add(row);

                    // Add a separator
                    DriveListPanel.Children.Add(new Border
                    {
                        Height = 1,
                        Background = new SolidColorBrush(Colors.LightGray),
                        Margin = new Thickness(0, 4, 0, 4)
                    });
                }
                catch
                {
                    // Skip drives that throw (e.g. unmounted, restricted)
                }
            }
        }
        catch (Exception ex)
        {
            DriveListPanel.Children.Add(new TextBlock
            {
                Text = $"Error reading drive info: {ex.Message}",
                Foreground = Brushes.Red
            });
        }
    }

    private Control CreateDriveRow(DriveInfo drive)
    {
        var panel = new Grid
        {
            Margin = new Thickness(0, 2)
        };

        panel.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
        panel.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        panel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        panel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        panel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        panel.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        // Drive name / mount point
        var nameLabel = new TextBlock
        {
            Text = drive.Name,
            FontWeight = FontWeight.Bold,
            FontSize = 14,
            Margin = new Thickness(0, 0, 12, 0)
        };
        Grid.SetRow(nameLabel, 0);
        Grid.SetColumn(nameLabel, 0);
        Grid.SetColumnSpan(nameLabel, 2);
        panel.Children.Add(nameLabel);

        // Volume label and format
        var volumeText = "";
        if (!string.IsNullOrEmpty(drive.VolumeLabel))
            volumeText = drive.VolumeLabel;
        volumeText += $"  ({drive.DriveFormat})  -  {drive.DriveType}";

        var volumeLabel = new TextBlock
        {
            Text = volumeText,
            Foreground = new SolidColorBrush(Colors.Gray),
            FontSize = 11,
            Margin = new Thickness(0, 0, 0, 4)
        };
        Grid.SetRow(volumeLabel, 1);
        Grid.SetColumn(volumeLabel, 0);
        Grid.SetColumnSpan(volumeLabel, 2);
        panel.Children.Add(volumeLabel);

        // Size info
        long totalSize = drive.TotalSize;
        long freeSpace = drive.AvailableFreeSpace;
        long usedSpace = totalSize - freeSpace;
        double usedPercent = totalSize > 0 ? (double)usedSpace / totalSize * 100 : 0;

        var sizeInfo = new TextBlock
        {
            Text = $"Total: {FormatSize(totalSize)}    Used: {FormatSize(usedSpace)} ({usedPercent:0.#}%)    Free: {FormatSize(freeSpace)}",
            FontSize = 12,
            Margin = new Thickness(0, 0, 0, 4)
        };
        Grid.SetRow(sizeInfo, 2);
        Grid.SetColumn(sizeInfo, 0);
        Grid.SetColumnSpan(sizeInfo, 2);
        panel.Children.Add(sizeInfo);

        // Usage bar
        var barContainer = new Grid
        {
            Height = 16,
            Margin = new Thickness(0, 0, 0, 2)
        };

        var barBackground = new Border
        {
            Background = new SolidColorBrush(Colors.LightGray),
            CornerRadius = new CornerRadius(3)
        };
        barContainer.Children.Add(barBackground);

        var barColor = usedPercent > 90 ? Colors.Red
                     : usedPercent > 75 ? Colors.Orange
                     : Colors.DodgerBlue;

        var barFill = new Border
        {
            Background = new SolidColorBrush(barColor),
            CornerRadius = new CornerRadius(3),
            HorizontalAlignment = HorizontalAlignment.Left
        };

        // Bind width as percentage after layout
        barContainer.LayoutUpdated += (_, _) =>
        {
            if (barContainer.Bounds.Width > 0)
                barFill.Width = barContainer.Bounds.Width * usedPercent / 100;
        };

        barContainer.Children.Add(barFill);

        Grid.SetRow(barContainer, 3);
        Grid.SetColumn(barContainer, 0);
        Grid.SetColumnSpan(barContainer, 2);
        panel.Children.Add(barContainer);

        return panel;
    }

    private static string FormatSize(long bytes)
    {
        string[] units = { "b", "Kb", "Mb", "Gb", "Tb" };
        double val = bytes;
        int unit = 0;
        while (val >= 1024 && unit < units.Length - 1)
        {
            val /= 1024;
            unit++;
        }
        return string.Format("{0:0.##} {1}", val, units[unit]);
    }
}
