# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers the features, keyboard shortcuts,
screenshots and the rclone cloud set-up in detail; this file covers how the code fits together and how to change
it safely.

## What this is

DirOpusReImagined ("DORI"), a two-panel file manager inspired by Directory Opus 4 on the Amiga, in C# on .NET 8
(`net8.0`) and Avalonia 11.3. It runs on Windows, macOS and Linux. It browses local folders, archives (through
`SharpCompress`) and cloud storage (through rclone), and previews files with the author's `SyntaxColorizer`
control. The version is set in `DirOpusReImagined.csproj` (`AssemblyVersion`, currently 0.1.27.0).

## Commands

```bash
dotnet build DirOpusReImagined.sln                # global.json: SDK 8.0.0 with rollForward latestMajor
dotnet run --project DirOpusReImagined.csproj     # --scale <factor> sets the UI scale
./publish-all.sh                                  # self-contained single-file zips for every platform, into dist/
./publish-all.sh linux-x64 osx-arm64              # only the listed runtime identifiers
```

`publish-all.ps1` is the PowerShell version of the same script. There are no tests yet.

- **Scaling:** `DisplayScaling.Bootstrap` reads `--scale` first, then the `AVALONIA_GLOBAL_SCALE_FACTOR` and
  `AVALONIA_SCREEN_SCALE_FACTORS` environment variables.
- **Screenshots:** Ctrl+Shift+P (Cmd on macOS) in any window saves a screenshot (`ScreenshotService`).

## Layout

Everything sits at the repository root; there is no `src/` folder.

- `Program.cs` and `App.axaml(.cs)`: start-up. `MainWindow.axaml.cs` (about 5,000 lines) holds the panels, tabs,
  action buttons, menus and most commands, and registers the rclone and archive providers.
- `TAIDataGrid.axaml(.cs)`: the custom file grid used by each panel, including drag and drop between panels.
- `PanelTab.cs`: one folder tab's saved state (path, history, sort, filter, cursor).
- `FileSystem/`: the storage layer.
  - `IFileProvider.cs` and `ProviderRegistry.cs` pick a provider by path.
    - `LocalFileProvider` is the default.
    - `Archive/ArchiveFileProvider` handles `archive://` paths.
    - `Rclone/RcloneFileProvider` handles `cloud://` paths.
  - `OperationQueue`, `TransferCoordinator`, `TransferItem`, `ConflictScanner` and `ConflictResolution` run copies
    and moves off the UI thread with progress and conflict handling.
  - `Preview/`: `PreviewRegistry` and one `IPreviewProvider` per kind of file (images, PDF, Office, archives,
    media, and a byte fallback that always comes last).
  - `Rclone/`: the rclone client, daemon, list cache and remote manager. `RcloneBinaryManager` can download a
    pinned rclone version from `downloads.rclone.org`.
- `DirectoryCompare.cs` (`DirectoryComparer`), `RenameEngine.cs`, `FileSearch.cs`, `TrashService.cs`,
  `ThemeManager.cs`, `ExecutableDetector.cs` and `FileUtility.cs`: logic used by the main window and dialogs.
- `SystemInfo/`: memory information and the "open terminal here" launcher.
- The other `*.axaml` files are dialogs, one window per file.
- `Configuration.xml`, `LINUXConfiguration.xml` and `MACConfiguration .xml` (with a space in the name): the
  default settings and action buttons per platform. `Roots.xml` keeps types from being trimmed.
- `ScreenShots/` and `Images/`: pictures for the README. `CURRENT_RELEASE_*` are release notes for sharing.

## Rules that keep it working

- **Config lookup order matters.** `ConfigFile` looks for `Configuration.xml` in the working folder, then beside
  the executable, then in the user's folder. The user's folder is `~/.config/dori/`, `%APPDATA%\dori\` or
  `~/Library/Application Support/dori/`. Use `ConfigFile.Resolve()` for any read or write, so both use the same
  file.
- **Per-platform defaults.** `publish-all.sh` copies the matching platform file in as `Configuration.xml`. A new
  setting or button usually belongs in all three files.
- **Other stored files.** Bookmarks live in `BOOKMARKS.MD` beside the executable. Crash logs go to `crash.log` in
  the same `dori` folder as the user config.
- **Go through the provider.** File operations must call `ProviderRegistry.For(path)`, not `System.IO` directly,
  or archive and cloud paths break. Check `IsRemote` before slow per-item calls, and list remote folders off the UI
  thread.
- **Threads.** `OperationQueue` raises its events on a worker thread. Post to `Dispatcher.UIThread` before
  touching any control.
- **Trash is local only.** `TrashService` throws on failure and must not be given `archive://` or `cloud://` paths.
- **New previews register, they don't edit the viewer.** Add an `IPreviewProvider` and list it in
  `PreviewRegistry`.
- **Warnings are partly silenced.** The csproj's `NoWarn` hides several nullable warnings (CS8600 to CS8629 and
  others). Don't add to the list to hide new problems.
- **Tracked build output.** A few files under `bin/Debug/net7.0` and `bin/Debug/net8.0` (icons and a config) are
  committed. Leave them alone, and don't commit other build output.

## Making a change

- Check changes by running the app on each platform you can, since there are no tests.
- Update `README.md` (features, shortcuts, the overview) to match, and refresh `ScreenShots/` if the look changes.
- Raise `AssemblyVersion` and `FileVersion` together for a release; `publish-all.sh` names the zips from it.

There is no CI workflow; releases are built locally with `publish-all.sh` or `publish-all.ps1`.
