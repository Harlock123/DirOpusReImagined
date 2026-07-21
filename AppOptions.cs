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
}
