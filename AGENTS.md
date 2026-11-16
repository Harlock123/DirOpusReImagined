# AGENTS.md

Notes for anyone, human or AI agent, changing this code. `README.md` covers the features, keyboard shortcuts,
screenshots and the rclone cloud set-up in detail; this file covers how the code fits together and how to change
it safely.

## What this is

DirOpusReImagined ("DORI"), a two-panel file manager inspired by Directory Opus 4 on the Amiga, in C# on .NET 10
(`net10.0`) and Avalonia 11.3. It runs on Windows, macOS and Linux. It browses local folders, archives (through
`SharpCompress`) and cloud storage (through rclone), and previews files with the author's `SyntaxColorizer`
control. The version is set in `DirOpusReImagined.csproj` (`AssemblyVersion`, currently 0.1.33.0).

## Commands

```bash
dotnet build DirOpusReImagined.sln                # global.json: SDK 8.0.0 with rollForward latestMajor
dotnet run --project DirOpusReImagined.csproj     # --scale <factor> sets the UI scale
./publish-all.sh                                  # self-contained single-file zips for every platform, into dist/
./publish-all.sh linux-x64 osx-arm64              # only the listed runtime identifiers
dotnet test tests/DirOpusReImagined.Tests         # xUnit suite; also run by CI before publishing
```

`publish-all.ps1` is the PowerShell version of the same script.

- **Tests:** `tests/DirOpusReImagined.Tests` covers the logic that runs without a window: `RenameEngine`,
  `DirectoryComparer`, `FileSearch`, conflict handling, the local, archive and cloud-path providers, and
  `FileUtility` helpers. Each disk test uses its own temp folder (`TempDir`). The main csproj sits at the root and
  globs `**/*.cs`, so it removes `tests/**`; keep that if you add test projects. Tests skipped with a `BUG:`
  reason pin known defects; fix the code, then remove the `Skip`.

- **Scaling:** `DisplayScaling.Bootstrap` reads `--scale` first, then the `AVALONIA_GLOBAL_SCALE_FACTOR` and
  `AVALONIA_SCREEN_SCALE_FACTORS` environment variables.
- **Screenshots:** Ctrl+Shift+P (Cmd on macOS) in any window saves a screenshot (`ScreenshotService`).

## Layout

Everything sits at the repository root; there is no `src/` folder.

- `Program.cs` and `App.axaml(.cs)`: start-up. `MainWindow.axaml.cs` (about 5,000 lines) holds the panels, tabs,
  action buttons, menus and most commands, and registers the rclone and archive providers.
- `TAIDataGrid.axaml(.cs)`: the custom file grid used by each panel, including drag and drop between panels.
- `PanelTab.cs`: one folder tab's saved state (path, history, sort, filter, cursor).
- `FileSystem/Checksums.cs` and `ChecksumWindow.axaml(.cs)`: computing, saving and verifying checksums in the
  coreutils formats (right-click → Checksums…).
- `FileCompare.cs` (`TextDiff`), `FileCompareWindow.axaml(.cs)` and `MainWindow.Compare.cs`: side-by-side file
  comparison (right-click → Compare Files…), a Myers line diff with a byte-level fallback for binary files.
- `MainWindow.Flat.cs`: flat view. A panel's `TaiDataGrid.FlatRoot` makes `FileUtility.PopulateFilePanel` list every file
  below that folder (`CollectFlat`, off the UI thread, never into linked folders), each named by its relative path, so
  the usual `JoinPanelPath` operations work; `TransferCoordinator` makes a missing local parent folder for such names.
- `KeyMap.cs` and `MainWindow.Shortcuts.cs`: panel keyboard shortcuts. Every command key goes through
  `KeyMap.Current` (defaults in `KeyMap.Commands`, changed by `<Shortcuts>` in `Configuration.xml`);
  `TaiDataGrid.RunShortcut` runs them, mostly by raising the `GridVerb` of the same name. A new panel command
  needs a `ShortcutCommand`, an entry in `KeyMap.Commands`, and a matching `GridVerb` (a test checks this).
- `Layouts.cs` and `MainWindow.Layouts.cs`: saved layouts (Layouts ▾), stored under `<Layouts>`. A layout reuses the
  tab restore path (`LoadTab`), `SetHiddenColumns`, `ApplyViewMode` and `ShowButtonBank`. `TextPromptDialog` asks for
  a line of text (used for layout names and saved filters).
- `DuplicateFinder.cs`, `DuplicatesWindow.axaml(.cs)` and `MainWindow.Duplicates.cs`: Find Duplicates (size, then first
  64 KB, then full SHA-256), deleting through `FileOperation.DeleteBatch` so trashed copies can be undone.
- `SavedFilters.cs` and `MainWindow.Filters.cs`: the filter box's matching (`SavedFilters.Matcher`, `;`-separated
  patterns) and the saved filters behind its ▾ button, stored under `<SavedFilters>`.
- `CommandPalette.cs`, `CommandPaletteWindow.axaml(.cs)` and `MainWindow.Palette.cs`: the command palette
  (Ctrl/Cmd+Shift+A). `MainWindow.RunButtonEntry` runs any button's action, so buttons from banks not showing work too.
- `BatchAttributes.cs` (`ModeChange` parses chmod notation), `SetAttributesWindow.axaml(.cs)` and
  `MainWindow.SetAttributes.cs`: Set Dates & Permissions for a whole selection (local files only).
- `FileSystem/Preview/ExifReader.cs` and `ImageOrientation.cs`: photo details for image previews (EXIF from JPEG,
  TIFF, PNG and WebP, every offset bounds-checked) and turning the decoded bitmap upright for EXIF orientations 2–8 (also used by `ThumbnailCache`; raise its
  `CacheVersion` whenever thumbnails are made differently, so old ones on disk aren't reused).
- `DiskUsage.cs` and `DiskUsageWindow.axaml(.cs)`: Disk Usage (sizes of everything under a local folder, largest first;
  linked folders count as nothing and are never walked into).
- `Labels.cs` (`LabelStore`, `Labels.xml` beside the config) and `MainWindow.Labels.cs`: colour labels and tags. Each listing
  copies them onto its rows (`AFileEntry.Label`/`LabelColour` fields for the tint and `tag:` filters, the `Tags` property
  for the column); `MainWindow.PushUndo` records renames and moves for undo and moves their labels too.
- `GitStatus.cs` and `MainWindow.Git.cs`: the Git column, from `git status --porcelain -z` run off the UI thread after each
  listing. `Tags` and `Git` are `TaiDataGrid.OptionalColumns`: shown only while some row has text in them.
- `ImageTools.cs` and `ImageToolsWindow.axaml(.cs)`: resize, convert and turn pictures with SkiaSharp (which Avalonia ships),
  always into new files. `FileSplitter.cs`: split into `name.001…` with `name.sha256`, and join with a check.
  `MainWindow.Tools.cs` opens these and Disk Usage, and runs split/join as `FileOperation`s in the queue.
- `SyncBrowse.cs` and `MainWindow.SyncBrowse.cs`: synced browsing (⇄ Follow). `AfterPanelNavigated`, called from
  `RefreshLPGridPostActions`/`RefreshRPGridPostActions`, compares the panel's old and new path.
- `ArchiveWriteBack.cs` and `MainWindow.ArchiveWriteBack.cs`: files extracted from archives for other programs
  (`ExtractArchiveEntryToTemp`) are watched, and a changed copy is offered back into the archive (`ArchiveEditor.Apply`).
- `ButtonPrompts.cs`: the `%ASK%` / `%ASK:Question%` button token, asked for in `RunButtonEntry` before the other tokens.
- `FileSystem/StreamCopy.cs`: local copies go through `name.dori-part` and are renamed when complete; a part of 4 MB or
  more is kept on cancel or failure and resumed by the next copy of the same file to the same place (`ResumablePrefix`).
  Verify Copies (`FileUtility.VerifyCopyIfEnabledAsync`, `VerifyArchiveAdds`) hashes through the providers, so archive legs
  are checked too.
- `MainWindow.Columns.cs`: choosing a panel's columns (header right-click), saved under `<Columns>`. The grid makes a
  column for each public property of `AFileEntry`; code refers to columns by property name (`SortColumnName`,
  `JustifyColumns`, `TruncateColumns`, `ColumnNameAt`), never by position, because hidden columns shift positions.
- `ButtonBanks.cs` and `MainWindow.Banks.cs`: banks of the 36 command buttons. Each bank is a `<Buttons name="…">`
  element in `Configuration.xml` (an old file's single unnamed `<Buttons>` reads as one bank), and
  `<ActiveButtonBank>` remembers which shows. `AddEditCmdButtonDefinition` edits copies of every bank and hands
  them back with `SetButtonBanks` on Save.
- `RecentFolders.cs` and `MainWindow.Recent.cs`: the recent-folders menu (◷ / Alt+Down), saved in `Configuration.xml`
  under `<RecentFolders>`.
- `UndoHistory.cs` and `MainWindow.Undo.cs`: Ctrl/Cmd+Z undo for renames, moves and deletes to the trash
  (`TrashService.Trash` returns a `TrashedItem` saying where each item went; `TrashService.Restore` puts it back). `UndoPlanner` records what
  really happened after an operation and checks the disk before reversing it; it never overwrites.
- `MainWindow.Refresh.cs`: Ctrl/Cmd+R refresh and the per-panel `FileSystemWatcher` that re-lists local folders
  changed by other programs, keeping the cursor, selection and scroll (`TaiDataGrid.CaptureViewState`).
- `FileSystem/`: the storage layer.
  - `IFileProvider.cs` and `ProviderRegistry.cs` pick a provider by path.
    - `LocalFileProvider` is the default.
    - `Archive/ArchiveFileProvider` handles `archive://` paths. `Archive/ArchiveEditor` changes .zip/.tar/.tar.gz
      archives (adds, removes, renames) in one rewrite via a temp file; each provider write method is a full
      rewrite, so bulk work goes through `ArchiveEdit` batches (`FileOperation.ArchiveTransfer`, `DeleteBatch`,
      archive renames in `ApplyRenamePlanDetailedAsync`). `Archive/ArchiveCreator` writes new archives.
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
- **Never walk into a linked folder.** Code that recurses through folders must skip entries with
  `FileEntry.IsLink` (or `LocalFileProvider.IsLink(path)`): a link back to a parent folder never ends.
  `SearchOption.AllDirectories` follows links, so don't use it for recursion. `MainWindow.Links.cs` has the link
  commands.
- **Trash is local only.** `TrashService` throws on failure and must not be given `archive://` or `cloud://` paths.
- **New previews register, they don't edit the viewer.** Add an `IPreviewProvider` and list it in
  `PreviewRegistry`.
- **Warnings are partly silenced.** The csproj's `NoWarn` hides several nullable warnings (CS8600 to CS8629 and
  others). Don't add to the list to hide new problems.
- **Tracked build output.** A few files under `bin/Debug/net7.0` and `bin/Debug/net8.0` (icons and a config) are
  committed. Leave them alone, and don't commit other build output.

## Making a change

- Run `dotnet test`, and add tests for new logic that doesn't need a window. UI behaviour still has to be checked by
  running the app on each platform you can.
- Update `README.md` (features, shortcuts, the overview) to match, and refresh `ScreenShots/` if the look changes.
- Raise `AssemblyVersion` and `FileVersion` together for a release; `publish-all.sh` names the zips from it.

`.github/workflows/build.yml` builds every push and runs `publish-all.sh` once per platform, so CI ships exactly
what a local run would. Pushes to `master` refresh the rolling `latest` pre-release; a `v*` tag makes a proper
release. `publish-all.sh` and `publish-all.ps1` still work locally.
