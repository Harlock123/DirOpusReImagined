using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DirOpusReImagined
{
    /// <summary>
    /// Keeps windows from opening larger than the screen they land on.
    ///
    /// Every window in this app sets a fixed startup size in device-independent pixels
    /// (MainWindow is 1300x800, the dialogs run up to 820x620). Those are comfortable at
    /// scale 1.0, but <see cref="DisplayScaling"/> now applies the desktop's scale factor, and a
    /// DIP size is multiplied by it: 1300x800 at 1.5x wants 1950x1200 physical pixels, which does
    /// not fit on a 1920x1080 panel. The window opens with its lower and right edges — buttons
    /// included — off the screen, and on a tiling compositor the user cannot drag it back.
    ///
    /// Rather than hand-tuning two dozen XAML files, this clamps at runtime: one class handler on
    /// <see cref="Window.WindowOpenedEvent"/> covers every window in the app, including any added
    /// later, and does nothing at all when the window already fits.
    /// </summary>
    public static class WindowSizing
    {
        /// <summary>Breathing room left around the window, in DIPs, so a clamped window does not sit
        /// flush against the working-area edge and stays clear of decorations the frame size does
        /// not always account for.</summary>
        private const double Margin = 16;

        /// <summary>Never clamp below this, in DIPs. A pathologically small working area (a mis-reported
        /// screen, a tiny virtual display) should not produce an unusable sliver of a window.</summary>
        private const double MinSize = 320;

        private static IDisposable? _subscription;

        /// <summary>
        /// Registers the clamp for every window in the application. Call once during startup, before
        /// the main window opens. Calling it again is a no-op.
        /// </summary>
        public static void Install()
        {
            if (_subscription != null) return;
            _subscription = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) => Clamp(window));
        }

        /// <summary>
        /// Shrinks <paramref name="window"/> to fit its screen's working area, then pulls it back
        /// on-screen if clamping (or a startup position computed from the larger size) left it
        /// hanging over an edge.
        /// </summary>
        private static void Clamp(Window window)
        {
            try
            {
                // Maximized and full-screen windows are already the compositor's business.
                if (window.WindowState != WindowState.Normal) return;

                var screen = window.Screens?.ScreenFromWindow(window) ?? window.Screens?.Primary;
                if (screen == null) return;

                double scaling = screen.Scaling > 0 ? screen.Scaling : 1.0;

                // Bounds is the size actually in effect, which is what matters; Width/Height are NaN
                // on windows that size to content, and stale on ones the compositor has resized.
                // On a tiling compositor Bounds is the tiled size, which already fits, so nothing
                // is clamped and the window manager keeps full control -- that is intended.
                double currentWidth = window.Bounds.Width > 0 ? window.Bounds.Width : window.Width;
                double currentHeight = window.Bounds.Height > 0 ? window.Bounds.Height : window.Height;

                var (fitWidth, fitHeight) = FitToWorkingArea(
                    currentWidth, currentHeight,
                    screen.WorkingArea.Width, screen.WorkingArea.Height, scaling);

                if (!double.IsNaN(fitWidth)) window.Width = fitWidth;
                if (!double.IsNaN(fitHeight)) window.Height = fitHeight;

                NudgeOnScreen(window, screen, scaling);
            }
            catch
            {
                // Sizing is a convenience. A window that opens awkwardly beats one that throws on open.
            }
        }

        /// <summary>
        /// The size half of the clamp, kept free of Avalonia types so it is directly testable.
        /// Sizes are DIPs; the working area is physical pixels, as the platform reports it.
        /// Returns NaN for an axis that needs no change, so the caller can leave that property alone.
        /// </summary>
        public static (double Width, double Height) FitToWorkingArea(
            double width, double height, double areaPixelWidth, double areaPixelHeight, double scaling)
        {
            if (scaling <= 0 || double.IsNaN(scaling) || double.IsInfinity(scaling)) scaling = 1.0;

            double maxWidth = Math.Max(MinSize, (areaPixelWidth / scaling) - Margin);
            double maxHeight = Math.Max(MinSize, (areaPixelHeight / scaling) - Margin);

            double fitWidth = !double.IsNaN(width) && width > maxWidth ? maxWidth : double.NaN;
            double fitHeight = !double.IsNaN(height) && height > maxHeight ? maxHeight : double.NaN;
            return (fitWidth, fitHeight);
        }

        /// <summary>
        /// The position half of the clamp, likewise free of Avalonia types. All values are physical
        /// pixels. Far edges are pulled in before near edges, so a window larger than the area ends
        /// up flush with its top-left corner rather than pushed off the opposite side.
        /// </summary>
        public static (int X, int Y) FitPosition(
            int x, int y, int width, int height,
            int areaX, int areaY, int areaWidth, int areaHeight)
        {
            int right = areaX + areaWidth;
            int bottom = areaY + areaHeight;

            if (x + width > right) x = right - width;
            if (y + height > bottom) y = bottom - height;
            if (x < areaX) x = areaX;
            if (y < areaY) y = areaY;

            return (x, y);
        }

        /// <summary>
        /// Moves the window back inside the working area if any edge falls outside it. Needed because
        /// WindowStartupLocation="CenterScreen" centres against the pre-clamp size, and because a
        /// window restored near an edge can land partly off-screen.
        /// </summary>
        private static void NudgeOnScreen(Window window, Avalonia.Platform.Screen screen, double scaling)
        {
            var area = screen.WorkingArea;

            // Prefer the frame size (includes decorations) and fall back to the client size.
            var frame = window.FrameSize ?? window.ClientSize;
            int width = (int)Math.Round(frame.Width * scaling);
            int height = (int)Math.Round(frame.Height * scaling);
            if (width <= 0 || height <= 0) return;

            var position = window.Position;
            var (x, y) = FitPosition(position.X, position.Y, width, height,
                                     area.X, area.Y, area.Width, area.Height);

            if (x != position.X || y != position.Y)
                window.Position = new PixelPoint(x, y);
        }
    }
}
