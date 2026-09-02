namespace DirOpusReImagined;

/// <summary>Small holder for user options that aren't tied to a specific control. Loaded from and
/// saved to Configuration.xml by MainWindow.</summary>
public static class AppOptions
{
    /// <summary>When true, user-initiated deletes go to the OS trash/recycle bin instead of
    /// permanent deletion. Applies to local files only (cloud has no trash). Default: on (safe).</summary>
    public static bool UseTrash = true;

    /// <summary>When true, the rclone daemon is left running when the app closes and re-attached on
    /// the next launch, so cloud folders skip the ~15-20s cold-start on subsequent launches. Leaves
    /// a background rclone process running between launches. Default: off.</summary>
    public static bool KeepRcloneWarm = false;

    /// <summary>When true, each copied file is re-read on both sides and its checksum compared after
    /// the copy; a mismatch fails the operation. Only applies where both ends use the same local
    /// provider (local disk and Windows/UNC shares) so the hashes are comparable — cross-provider and
    /// cloud copies are not verified. Roughly doubles read I/O, so it is off by default.</summary>
    public static bool VerifyCopies = false;

    /// <summary>UI scale factor applied to every window. 0 means "auto" — let <see cref="DisplayScaling"/>
    /// work it out from the desktop. Only read at startup (Avalonia fixes the scale when the windowing
    /// platform initialises), so a change here needs a restart to take effect.</summary>
    public static double UiScale = 0;

    /// <summary>Size of the F9 preview window, in device-independent pixels. Persisted so the window
    /// reopens at whatever size it was last left, rather than resetting every time it is toggled.
    /// Defaults are a comfortable starting size on a 1080p screen; <see cref="WindowSizing"/> clamps
    /// them if the saved size no longer fits the screen the window lands on.</summary>
    public static double PreviewWidth = 700;
    public static double PreviewHeight = 500;

    /// <summary>Screen position of the F9 preview window, in physical pixels, or null when it has
    /// never been placed. Null means "let the window centre on its owner"; a saved position is
    /// validated against the screens actually present before it is used, so a window saved on a
    /// monitor that is no longer attached does not open off-screen.</summary>
    public static int? PreviewX;
    public static int? PreviewY;

    /// <summary>Per-panel layout mode (detail list vs. thumbnail grid). Persisted per side so each
    /// panel remembers its own view across launches. Default: list.</summary>
    public static GridViewMode LeftViewMode = GridViewMode.List;
    public static GridViewMode RightViewMode = GridViewMode.List;
}
