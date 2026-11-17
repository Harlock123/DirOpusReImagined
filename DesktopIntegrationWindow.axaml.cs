using System;
using Avalonia.Controls;

namespace DirOpusReImagined
{
    /// <summary>Adds or removes the ways other programs open folders in DORI. See <see cref="DesktopIntegration"/>.</summary>
    public partial class DesktopIntegrationWindow : Window
    {
        public DesktopIntegrationWindow()
        {
            InitializeComponent();
            ExplainText.Text = OperatingSystem.IsWindows()
                ? "Adds \"Open in DORI\" to Explorer's right-click menu for folders, folder backgrounds and drives. It is added for you only; nothing needs administrator rights."
                : OperatingSystem.IsLinux()
                    ? "Adds DORI to your applications as a program that opens folders (" + DesktopIntegration.DesktopFileName +
                      " in " + DesktopIntegration.ApplicationsFolder() + "), so it is offered under \"Open With\" in other file managers and apps."
                    : "On macOS, folders can be opened in DORI by dropping them on its Dock icon or with 'open -a'. There is nothing to add here.";
            DefaultBox.IsVisible = OperatingSystem.IsLinux();
            AddButton.IsEnabled = RemoveButton.IsEnabled = DesktopIntegration.Supported;
            AddButton.Click += (_, _) =>
            {
                try
                {
                    DesktopIntegration.Install(DefaultBox.IsChecked == true);
                    StatusText.Text = "Added." + (DesktopIntegration.IsDefault() ? " DORI is now the default for folders." : "");
                }
                catch (Exception ex) { StatusText.Text = "Couldn't add it: " + ex.Message; }
                UpdateState(keepStatus: true);
            };
            RemoveButton.Click += (_, _) =>
            {
                try
                {
                    DesktopIntegration.Remove();
                    StatusText.Text = "Removed.";
                }
                catch (Exception ex) { StatusText.Text = "Couldn't remove it: " + ex.Message; }
                UpdateState(keepStatus: true);
            };
            CloseButton.Click += (_, _) => Close();
            UpdateState(keepStatus: false);
        }

        private void UpdateState(bool keepStatus)
        {
            bool installed = DesktopIntegration.Supported && DesktopIntegration.IsInstalled();
            bool isDefault = DesktopIntegration.IsDefault();
            DefaultBox.IsChecked = isDefault;
            RemoveButton.IsEnabled = installed;
            AddButton.Content = installed ? "Update" : "Add";
            if (!keepStatus && DesktopIntegration.Supported)
                StatusText.Text = installed ? "It is added" + (isDefault ? ", and DORI is the default for folders." : ".") : "It isn't added yet.";
        }
    }
}
