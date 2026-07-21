using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace DirOpusReImagined
{
    public partial class App : Application
    {
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            // Register the named-theme palettes (Dracula/Nord/Solarized) before the window loads
            // and restores its saved theme.
            ThemeManager.Init(this);

            // App-wide screenshot hotkey (Ctrl+Shift+P / Cmd+Shift+P) for building the manual.
            ScreenshotService.Register();

            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}