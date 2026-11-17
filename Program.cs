using Avalonia;
using System;
using System.Linq;

namespace DirOpusReImagined
{
    internal class Program
    {
        // Initialization code. Don't use any Avalonia, third-party APIs or any
        // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
        // yet and stuff might break.
        [STAThread]
        public static void Main(string[] args)
        {
            // Must run before any Avalonia API: the windowing platform reads its scale factor from
            // the environment exactly once, at initialisation. See DisplayScaling for the why.
            DisplayScaling.Bootstrap(args);

            // Folders named on the command line go to the DORI already running, if there is one.
            // So do scripts given with --cmd or --script.
            var paths = SingleInstance.PathsFrom(args);
            var scripts = SingleInstance.ScriptsFrom(args);
            var message = paths.Concat(scripts.Select(SingleInstance.EncodeScript)).ToList();
            if (message.Count > 0 && Array.IndexOf(args, SingleInstance.NewWindowSwitch) < 0 && SingleInstance.TrySend(message))
                return;
            SingleInstance.StartupPaths = paths;
            SingleInstance.StartupScripts = scripts;

            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }

        // Avalonia configuration, don't remove; also used by visual designer.
        public static AppBuilder BuildAvaloniaApp()
            => AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .LogToTrace();
    }
}
