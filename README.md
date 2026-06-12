# DirOpusReImagined

This is **DirOpusReImagined** - a modern cross-platform file manager inspired by the classic Amiga Directory Opus 4.12.

## What It Does

It's a **dual-panel file manager** built with .NET 8 and Avalonia that runs on Windows, macOS, and Linux. Think of it as a professional-grade file browser with two independent panels side-by-side for efficient file management.

## Key Features

### Dual-Panel File Browsing
- Two independent file panels displayed side-by-side for efficient file management
- Each panel has its own path bar, back button, and drive selector
- Double-click a folder to navigate into it; use the back button to go up a level
- Swap panel contents or clone one panel's path to the other with dedicated buttons

### File Operations
- **Copy** files and folders between panels
- **Move** files and folders between panels
- **Delete** files and folders with confirmation dialog
- **Rename** files and folders with a pattern-based rename interface for batch operations
- **Create folders** in either panel
- **Create ZIP archives** from selected files

### Real-Time Filter
- Each panel has a filter text box that narrows the displayed files and folders as you type
- Filters by name using case-insensitive matching
- An X button next to each filter clears it instantly
- Filters reset automatically when navigating to a new directory

### Status Bar
- A status bar at the bottom of the window shows live information for each panel:
  - Total folder and file counts
  - Number of selected files and their combined size
  - Free disk space on the current drive

### Selection Management
- Click to select a single item; Ctrl+Click for multi-select; Shift+Click for range select
- "Select All" buttons to select all files (not folders) in a panel
- "Clear" buttons to deselect everything in a panel

### 36 Customizable Action Buttons
- A grid of 36 user-configurable command buttons below the panels
- Each button can launch any external program with flexible argument substitution
- Supports parameter placeholders like `%FD%`, `%LPATH%`, `%AF%`, and more
- Button appearance (color, text, alignment, tooltip) is fully configurable via XML

### Drive Presets
- Up to 5 configurable quick-navigation presets between the panels
- Each preset has a left-panel and right-panel button
- Supports environment variables: `$HOME`, `$ROOT`, `$DESKTOP`, `$DOCUMENTS`, `$PICTURES`

### Integrated Image Viewer
- Built-in image viewer for common formats (BMP, JPG, PNG, TIFF, GIF, ICO, etc.)
- Configurable via the `<UseIntegratedImageViewer>` setting

### Cloud Storage Access
- Browse Google Drive, OneDrive, Dropbox, S3, Box, and 40+ other providers via [rclone](https://rclone.org/)
- Access any configured remote with `cloud://<remote-name>/<path>` in the panel path box
- Automatic rclone download on first use, with live progress UI
- Lazy daemon startup — no overhead until you actually use cloud storage
- Dedicated diagnostics dialog showing binary location, configured remotes, live daemon log, and recent API requests

### Navigation
- Path text box with Enter key navigation
- Back button to go up one directory level
- Drive button for drive/volume selection
- Panel swap and clone buttons for quick path synchronization
- Cloud paths fully integrated alongside local — same double-click / breadcrumb / back behavior

### Display Options
- Toggle hidden file visibility with the "Show Hidden" checkbox
- Sort files by name or by size
- Configurable font sizes for grid content and headers
- Right-click context menu on panels for font size adjustment
- Tooltips on hover showing file/folder details

### Cross-Platform Support
- Runs on Windows, macOS, and Linux via Avalonia
- Platform-specific configuration files (Configuration.xml, MACConfiguration.xml, LINUXConfiguration.xml)
- Handles platform differences in path separators, special folders, and executable detection

### Configuration
- XML-based configuration file (`Configuration.xml`) for all customization
- Configurable executable file extensions for double-click launching
- Configurable image file extensions for the integrated viewer
- Per-panel start paths, font sizes, and titles

## Tech Stack

- **Framework**: Avalonia 11.0.0 (cross-platform UI)
- **Runtime**: .NET 8.0 / C#
- **XML-based configuration** for buttons and settings

The project is currently at version 0.0.6.0 and under active development. It's designed for power users, developers, and system administrators who need efficient file management with extensive customization options.

## Detailed Overview

The interface presents itself as two panels of files in a specified path.
The standard sorts of navigation allow one to move around the file system 
in each panel. There are also buttons to allow one to move up a directory 
to the left of each path text area above each panel.
Between the left and right panels is a button to allow one to copy and move
files and folders from one panel to the other. 

Doubkle clicking on a folder in a panel will open that folder in the same panel
adjusting the panels path text area accordingly.

To select a file simply click on it. To select multiple files hold down the
control key and click on the files you want to select. 

There are a series of buttons below the panels that allow one to user defined
functions. I first execution the application will read the definitions of these 
user defined functions from a file called `Configuration.xml` in the same folder.

A Sample of this file is shown below

```
<Settings>
	<Buttons>
		<Button>
			<Name>LPButton1</Name>
			<Content>VSCODE here</Content>
			<Background>Red</Background>
			<Foreground>Black</Foreground>
			<HorizontalAlignment>Center</HorizontalAlignment>
			<VeriticalAlignment>Center</VeriticalAlignment>
			<Margin>2,2,2,2</Margin>
			<Action>code</Action>
			<Args>%FD%</Args>
			<Shell>True</Shell>
			<Window>True</Window>
		</Button>
		<Button>
			<Name>LPButton2</Name>
			<Content>VSCODE All Files</Content>
			<Action>code</Action>
			<Args>%AF%</Args>
			<Shell>True</Shell>
			<Window>True</Window>
		</Button>
		<Button>
			<Name>LPButton3</Name>
			<Content>NotePad all seq</Content>
			<Action>notepad.exe</Action>
			<Args>%LAF%</Args>
			<Shell>False</Shell>
			<Window>False</Window>
		</Button>
		<Button>
			<Name>LPButton4</Name>
			<Content>Code Diff</Content>
			<Action>code</Action>
			<Args>--diff %LF1% %RF1%</Args>
			<Shell>True</Shell>
			<Window>False</Window>
			<ToolTip>Call VSCODE with the --diff parameter passing the first file selected in the Left and Right panel as arguments</ToolTip>
		</Button>
		<Button>
			<Name>LPButton33</Name>
			<Content>Windows Terminal on Left</Content>
			<Action>wt</Action>
			<Args>-d %LPATH%</Args>
			<Shell>False</Shell>
			<Window>False</Window>
			<ToolTip>Opens a windows terminal in the filder shown on the left panel</ToolTip>
		</Button>
		<Button>
			<Name>LPButton34</Name>
			<Content>Windows Terminal on Right</Content>
			<Action>wt</Action>
			<Args>-d %RPATH%</Args>
			<Shell>False</Shell>
			<Window>False</Window>
			<ToolTip>Opens windows terminal in the filder shown on the right panel</ToolTip>
		</Button>
		<Button>
			<Name>LPButton35</Name>
			<Content>Terminal on Left</Content>
			<Action>alacritty</Action>
			<Args>--working-directory %LPATH%</Args>
			<Shell>False</Shell>
			<Window>False</Window>
			<ToolTip>Opens an alacritty terminal in the filder shown on the left panel</ToolTip>
		</Button>
		<Button>
			<Name>LPButton36</Name>
			<Content>Terminal on right</Content>
			<Action>alacritty</Action>
			<Args>--working-directory %RPATH%</Args>
			<Shell>False</Shell>
			<Window>False</Window>
			<ToolTip>Opens an alacritty terminal in the filder shown on the right panel</ToolTip>
		</Button>
	</Buttons>

	<DrivePresets>
		<DrivePreset>
			<Order>1</Order>
			<Name>Home</Name>
			<Path>$HOME</Path>
		</DrivePreset>
		<DrivePreset>
			<Order>2</Order>
			<Name>Root</Name>
			<Path>$ROOT</Path>
		</DrivePreset>
		<DrivePreset>
			<Order>3</Order>
			<Name>DT</Name>
			<Path>$DESKTOP</Path>
		</DrivePreset>
		<DrivePreset>
			<Order>4</Order>
			<Name>Docs</Name>
			<Path>$DOCUMENTS</Path>
		</DrivePreset>
		<DrivePreset>
			<Order>5</Order>
			<Name>Pics</Name>
			<Path>$PICTURES</Path>
		</DrivePreset>
	</DrivePresets>

	<Executable>
		<Extensions>
			EXE,BAT,PS1,BMP,JPG,JPEG,TXT,PNG,TIFF,GIF,ICO,
			PNG,DOC,DOCX,XLS,XLSX,PPT,PPTX,PDF,ZIP,RAR,7Z,WAV,AAC,MP3,MP4,
			AVI,FLV,WMV,MOV,MPG,MPEG,FLAC,OGG,OGV,WEBM,HTML,HTM,XML,JSON,
			CSS,JS,TS,CS,CSHARP,CSHTML,ASPX,ASP,PHP,SQL,INI,CFG,LOG,MD,MARKDOWN
		</Extensions>
	</Executable>

	<Images>
		<ImageExtensions>BMP,JPG,JPEG,PNG,TIFF,TIF,GIF,ICO,PCX</ImageExtensions>
		<UseIntegratedImageViewer>False</UseIntegratedImageViewer>
	</Images>

	<LeftGrid>
		<FontSize>14</FontSize>
		<HeaderFontSize>16</HeaderFontSize>
		<Title>LEFT Grid</Title>
		<TitleFontSize>20</TitleFontSize>
		<StartPath>/</StartPath>
	</LeftGrid>

	<RightGrid>
		<FontSize>14</FontSize>
		<HeaderFontSize>16</HeaderFontSize>
		<Title>RIGHT Grid</Title>
		<TitleFontSize>20</TitleFontSize>
		<StartPath>/</StartPath>
	</RightGrid>

</Settings>

```

It is a work in progress.

Main Screen Interface

![Screenshot](https://github.com/Harlock123/DirOpusReImagined/blob/master/Images/MainScreenMac.png)

Arguments Parameters

The `<Args> </Args>` parameter can contain the following entries

* Any text that you want to pass to the command line

* `%FD%` - Full Path of the file or folder selected in the Left or Right Panel Left Panel is searched first

* `%AF%` - All Files in the Left or Right Panel Left Panel is searched first. Each argument is separated by a space

* `%LAF%` - All Files in the Left or Right Panel . Each argument is separated by a space Left panel is searched first

* `%RF1%` - Full Path of the file selected in the Right Panel

* `%LF1%` - Full Path of the file selected in the Left Panel

    Note That the above two parameters are usable together in a buttons definition
    To allow handing a file from each panel to a command. For example a diff command
    as shown in the above example configuration file

* `%RPAF%` - Full Path All Files in the Right Panel . Each argument is separated by a space

* `%LPAF%` - Full Path All Files in the Left Panel . Each argument is separated by a space

* `%LPATH%` - Full Path of what is currently being shown in the LEFT Panel

* `%RPATH%` - Full Path of what is currently being shown in the RIGHT Panel



The `<Action></Action>` Parameter needs to be the actual command that you want to execute on clicking the button. 
The Parsed ARGS from the above parameters will be appended to the command line.

The `<Name></Name>` Parameter is the name of the button. It is used to identify the button in the configuration file.
There are 36 buttons available in the interface numbered 1 to 36. The buttons are numbered from left to right
top to bottom. The first button is LPButton1 and the last button is LPButton36.

The `<Content></Content>` Parameter is the text that will appear on the button itself.

**Built-in command tokens** — if `<Content>` is set to one of the special tokens below, the button opens a built-in dialog instead of launching an external command. `<Action>`, `<Args>`, `<Shell>`, and `<Window>` are ignored for these buttons.

| Token | What the button does |
|---|---|
| `%BUTTONCONFIG%` | Opens the button-definition editor for the 36 action buttons |
| `%DRIVEINFO%` | Opens the Drive Information dialog showing mounted volumes |
| `%RCLONEDIAG%` | Opens the rclone Diagnostics dialog — binary location, daemon state, configured remotes (with Delete), live log, and an **Install rclone** button if it's missing |
| `%RCLONECONFIG%` | Opens the Add Remote dialog directly — pick a provider, fill the form (OAuth handled automatically for Google Drive / OneDrive / Dropbox / etc.), and the new remote appears as `cloud://<name>/` in any panel |

Example — adding a button that opens the cloud configuration dialog:

```xml
<Button>
    <Name>LPButton3</Name>
    <Content>%RCLONECONFIG%</Content>
    <Background>DarkGreen</Background>
    <Foreground>White</Foreground>
    <ToolTip>Add a new cloud storage remote</ToolTip>
</Button>
```

The `<Background></Background>` Parameter is the background color of the button

The `<Foreground></Foreground>` Parameter is the foreground color of the button

The `<HorizontalAlignment></HorizontalAlignment>` Parameter is the horizontal alignment of the text on the button
Valid values are Left, Center, Right

The `<VerticalAlignment></VerticalAlignment>` Parameter is the vertical alignment of the text on the button
Valid values are Top, Center, Bottom

The `<Margin></Margin>` Parameter is the margin around the button. The values are in the order Left, Top, Right, Bottom

The `<Shell></Shell>` Parameter is a boolean value that indicates whether the command should be executed in a shell or not
valid values are True or False

The `<Window></Window>` Parameter is a boolean value that indicates whether the command should be executed in a new window or not
valid values are True or False

The `<ToolTip></ToolTip>` Parameter is the text that will appear when the mouse hovers over the button for a few seconds


The configuration file contains a section called `<DrivePresets></DrivePresets>` that allows one to define a set of hardcoded 
path specifications to be loaded into the LEFT and RIGHT panels. The buttons for this functionality are located in the 
interface between the left and right panels. Set as block of 10 buttons seperated between each panel. 5 dedicated to the
LEFT and 5 to the RIGHT panel. The panels button tuples are numbered from 1 to 5. The first button set at the top
is number 1 and the bottom set is number 5.

Each button is defined between a `<DrivePreset></DrivePreset>` tag.

The `<Order></Order>` tag is used to define the order of the button in the panel.

The `<Name></Name>` tag is used to define the text that will appear on the button.
care should be taken to ensure that the text is not too long as it will be truncated if it does not fit on the button.

The `<Path></Path>` Specifier is the path that will be loaded into the panel when the button is clicked.

There are a number of special variables that can be used in the path specification.
- $HOME - The users home directory
- $ROOT - The root directory of the system 
  - usually C:\ on Windows, / on Linux and MacOS
- $DESKTOP - The users desktop directory
- $DOCUMENTS - The users documents directory 
  - Note:
       On Mac and Linux it will compare the folder returned by .Nets Environment.SpecialFolder
       enumeration with the users HOME folder. If they are the same it will then search the
       Home folder for a Documents, documents, DOCUMENTS folder and if found will return that.
- $PICTURES - The users pictures directory

## Cloud Storage (rclone)

DORI can browse cloud storage — Google Drive, OneDrive, Dropbox, S3, Box, and ~40 other providers — through [rclone](https://rclone.org/). Remotes you've configured in rclone appear as `cloud://<remote>/<path>` URIs you can type into any panel's path box just like a local path.

### How it works

- DORI launches a local `rclone rcd` daemon on a random `127.0.0.1` port and talks to it over HTTP with a per-session user/password.
- All provider authentication (OAuth tokens, API keys) is stored by rclone in its own config file — DORI never handles credentials directly.
- The daemon is launched lazily on the first cloud-path operation and shut down automatically when DORI closes.

### Step 1 — Install rclone

You have three broad choices for getting rclone onto your system. Pick whichever is easiest — DORI uses whichever it finds first on your PATH, falling back to its own private copy.

1. **Install system-wide with a package manager** (recommended if you already use one).
2. **Download the official binary** from rclone.org and put it on your PATH manually.
3. **Let DORI download it for you** — opens the rclone Diagnostics dialog and click **Install rclone**. DORI downloads the pinned version (v1.68.2) into its private data directory. No admin rights needed; doesn't affect anything else on your system.

#### macOS

| Method | Command |
|---|---|
| Homebrew | `brew install rclone` |
| MacPorts | `sudo port install rclone` |
| Official install script | `curl https://rclone.org/install.sh \| sudo bash` |
| Manual zip | [rclone.org/downloads](https://rclone.org/downloads/) → pick `rclone-vX.Y.Z-osx-amd64.zip` (Intel) or `rclone-vX.Y.Z-osx-arm64.zip` (Apple Silicon), extract, move `rclone` to `/usr/local/bin/` and `chmod +x` it |

#### Linux

| Distribution | Command |
|---|---|
| Debian / Ubuntu | `sudo apt install rclone` |
| Fedora / RHEL | `sudo dnf install rclone` |
| Arch / Manjaro | `sudo pacman -S rclone` |
| openSUSE | `sudo zypper install rclone` |
| Alpine | `sudo apk add rclone` |
| Any distro (always current) | `curl https://rclone.org/install.sh \| sudo bash` |
| Snap | `sudo snap install rclone` |

Distro packages can lag the upstream release by months. If you need the latest features, use the official install script or manual zip.

#### Windows

| Method | Command |
|---|---|
| winget | `winget install Rclone.Rclone` |
| Chocolatey | `choco install rclone` |
| Scoop | `scoop install rclone` |
| Manual zip | [rclone.org/downloads](https://rclone.org/downloads/) → pick `rclone-vX.Y.Z-windows-amd64.zip` (64-bit) or `rclone-vX.Y.Z-windows-386.zip` (32-bit), extract, copy `rclone.exe` somewhere on your `PATH` (e.g. `C:\Program Files\rclone\`) |

After a manual install on Windows, add the folder containing `rclone.exe` to your `PATH` environment variable (System Properties → Advanced → Environment Variables), then open a fresh terminal so it picks up the change.

#### Verify the install

Open a terminal and run:

```bash
rclone version
```

You should see output like `rclone v1.68.2` — the exact version will depend on your install method. If you get "command not found", either rclone isn't installed, or its location isn't on your `PATH`. DORI will still work in that case — just use the **Install rclone** button in the Diagnostics dialog to get a private copy.

#### Using DORI's built-in installer

If you skip the above and open a panel with `cloud://…`, DORI will throw a clear error telling you rclone isn't installed. Open the diagnostics dialog (bind any button's `<Content>` to `%RCLONEDIAG%`), click **Install rclone**, and watch the progress bar — DORI downloads the pinned binary for your OS and architecture, extracts it, and sets it up. Subsequent cloud operations work normally.

### Step 2 — Configure a remote

Remotes are set up interactively by running `rclone config` in a terminal. Each remote gets a short name — that's what you'll use in DORI as `cloud://<name>/`.

**Example: Google Drive**

```bash
rclone config
```

Answer the prompts:

| Prompt | Answer |
|---|---|
| e/n/d/r/c/s/q | `n` — new remote |
| name | `gdrive` (or whatever you'd like; remember, it's case-sensitive) |
| Storage | select **Google Drive** (usually `drive`) |
| client_id | blank (uses rclone's default) |
| client_secret | blank |
| scope | `1` (full access) |
| service_account_file | blank |
| Edit advanced config? | `n` |
| Use auto config? | `y` — opens your browser for OAuth |
| Configure this as a Shared Drive? | `n` (unless it is) |
| Keep this "gdrive" remote? | `y` |
| e/n/d/r/c/s/q | `q` |

rclone's [Google Drive docs](https://rclone.org/drive/) cover edge cases like shared drives, service accounts, and using your own OAuth client ID.

**Other providers:** rclone supports a long list. Setup follows the same shape — run `rclone config`, pick the provider, fill in provider-specific details. See:
- [OneDrive](https://rclone.org/onedrive/)
- [Dropbox](https://rclone.org/dropbox/)
- [S3](https://rclone.org/s3/)
- [Full provider list](https://rclone.org/overview/)

### Step 3 — Where `rclone.conf` lives

| Platform | Default config path |
|---|---|
| macOS / Linux | `~/.config/rclone/rclone.conf` |
| Windows | `%APPDATA%\rclone\rclone.conf` |

If `RCLONE_CONFIG` is set, rclone uses that path instead. DORI's embedded binary reads the same file as a system-installed rclone, so setup done with either works everywhere.

**If you only have DORI's embedded rclone** (no system install), invoke it directly:
- macOS: `~/Library/Application\ Support/DirOpusReImagined/rclone/rclone config`
- Linux: `~/.local/share/DirOpusReImagined/rclone/rclone config`
- Windows: `%LOCALAPPDATA%\DirOpusReImagined\rclone\rclone.exe config`

The Diagnostics dialog's **Binary** row shows the exact path on your system.

### Step 4 — Access cloud resources in DORI

1. Click either panel's path text box.
2. Type `cloud://<remote-name>/` — the leading part must match the remote name in `rclone.conf` exactly (case-sensitive).
3. Press Enter.
4. Browse as you would any local folder — double-click directories, use breadcrumbs, use the back button.

**Examples:**

| URI | Opens |
|---|---|
| `cloud://gdrive/` | Root of the `gdrive` remote |
| `cloud://gdrive/Photos/2024` | A subfolder |
| `cloud://dropbox/Work` | A different remote |
| `cloud://onedrive/Documents` | Yet another |

You can have one panel on a local path and the other on a cloud path — copy between them as usual.

### Diagnostics dialog

Bind a custom button in `Configuration.xml` to open the rclone diagnostics dialog:

```xml
<Button>
    <Name>LPButton1</Name>
    <Content>%RCLONEDIAG%</Content>
    <Background>DarkBlue</Background>
    <Foreground>White</Foreground>
</Button>
```

(`%RCLONEDIAG%` is a built-in command — any button whose `<Content>` is exactly that string triggers the dialog.)

The dialog shows:
- **Binary** — path to rclone (or "not installed"), with an Install button when missing
- **Running** / **Endpoint** — daemon state and local URL
- **Config** — path to `rclone.conf`
- **Configured remotes** — list of remote names with their `cloud://` URIs, read via rclone's `config/listremotes` API
- **Recent rc requests** — last 100 outgoing API calls (method, endpoint, JSON body, any error responses)
- **rclone daemon log** — live stdout/stderr from the background `rclone rcd` process

### Caveats

- Cloud listings have network latency — expect ~200 ms–1 s per directory hop depending on provider. The UI stays responsive during these calls.
- Subdirectory count and directory size columns are blank for cloud entries to avoid a round-trip per row (local entries still show these).
- Double-clicking a cloud **file** to launch it is not yet supported (download-to-temp-and-launch is planned).
- ZIP archive creation targeting a cloud path is not yet supported.

## Building

### Prerequisites

- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later

Verify your installation:

```bash
dotnet --version
```

### Clone the Repository

```bash
git clone https://github.com/Harlock123/DirOpusReImagined.git
cd DirOpusReImagined
```

### Build

```bash
dotnet build
```

The compiled output will be in `bin/Debug/net8.0/`.

### Run

```bash
dotnet run
```

Or run the compiled binary directly:

```bash
./bin/Debug/net8.0/DirOpusReImagined
```

On Windows:

```cmd
bin\Debug\net8.0\DirOpusReImagined.exe
```

### Publish All Platforms (Single-File Executables)

Build scripts are included that produce self-contained, single-file executables for all 6 supported platforms. No .NET runtime needed on the target machine.

**On macOS/Linux:**
```bash
./publish-all.sh
```

**On Windows (PowerShell):**
```powershell
.\publish-all.ps1
```

Output goes to `publish/<platform>/`:

| Platform | Runtime ID | Output |
|---|---|---|
| Windows Intel/AMD 64-bit | `win-x64` | `publish/win-x64/DirOpusReImagined.exe` |
| Windows Intel/AMD 32-bit | `win-x86` | `publish/win-x86/DirOpusReImagined.exe` |
| Windows ARM | `win-arm64` | `publish/win-arm64/DirOpusReImagined.exe` |
| macOS Intel | `osx-x64` | `publish/osx-x64/DirOpusReImagined` |
| macOS Apple Silicon | `osx-arm64` | `publish/osx-arm64/DirOpusReImagined` |
| Linux Intel/AMD | `linux-x64` | `publish/linux-x64/DirOpusReImagined` |
| Linux ARM | `linux-arm64` | `publish/linux-arm64/DirOpusReImagined` |

The `publish-all.sh` and `publish-all.ps1` scripts also produce per-platform zip files in `dist/` suitable for attaching to a GitHub release (`DirOpusReImagined-<version>-<rid>.zip`).

### Publish a Single Platform

To build for just one platform:

```bash
dotnet publish -c Release -r <runtime-id> --self-contained true \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -o publish/<runtime-id>
```

Replace `<runtime-id>` with one of: `win-x64`, `win-x86`, `win-arm64`, `osx-x64`, `osx-arm64`, `linux-x64`, `linux-arm64`.

### Configuration

Make sure `Configuration.xml` is in the same directory as the executable when running. The application looks for it in the following order:

1. The current working directory
2. Platform-specific locations:
   - **Linux/Unix**: `~/.config/dori/Configuration.xml`
   - **macOS**: `~/Library/Application Support/dori/Configuration.xml`
   - **Windows**: `%APPDATA%\dori\Configuration.xml`

The `Assets` folder (containing button icons) must also be present alongside the executable.

### Opening in an IDE

- **JetBrains Rider**: Open `DirOpusReImagined.sln`
- **Visual Studio**: Open `DirOpusReImagined.sln`
- **VS Code**: Open the project folder and install the C# Dev Kit extension

## Changelog

Notable changes, most recent first. Dates reflect when the work was implemented.

### 2026-06-12 — Avalonia 11.3 upgrade & Linux HiDPI scaling
- Upgraded Avalonia from `11.0.0-preview4` to the current stable `11.3.17`, which scales natively to the desktop's fractional scaling on Linux (Wayland/X11) — the app now follows the system scale (e.g. 180%) correctly.
- Migrated the breaking APIs the upgrade introduced: `ItemsControl.Items` → `ItemsSource`, `Application.Current.Clipboard` → `TopLevel.Clipboard`, `PointerPoint.GetCurrentPoint(Visual)`, and `FluentTheme Mode` → `RequestedThemeVariant`.
- Removed the now-redundant `XamlNameReferenceGenerator` package (Avalonia 11.3 ships its own XAML name generator).
- Cleaned up stale `bin/Debug/net7.0` and duplicate asset entries from the project file.
- Note: an earlier same-day workaround that set `AVALONIA_GLOBAL_SCALE_FACTOR` from the system DPI was removed, as the upgrade makes it unnecessary (and it would otherwise double-scale).

### 2026-04-24 — Cloud "Add Remote"
- Added in-app "Add Remote" functionality and further rclone cloud integration enhancements.

### 2026-04-22 — rclone cloud provider & diagnostics
- Introduced an rclone-based cloud file provider and a diagnostics UI for inspecting remotes and logs.
- Expanded the README with detailed rclone installation instructions across macOS, Linux, and Windows.

### 2026-04-21 — File system abstraction & release packaging
- Introduced an extensible file system abstraction and updated file operations to use it.
- Added versioning and multi-platform release packaging to the publish scripts.

### 2026-04-19 — Navigation & path tools
- Added "Copy Path" and "Copy Full Path" context-menu options.
- Added a DriveInfo dialog for viewing mounted volumes and their details.
- Implemented path-history tracking for navigation, with cross-platform path handling and safer file execution.

### 2026-04-17 — Permissions & folder size
- Added a file permissions dialog with context-menu integration.
- Added a "Calculate Folder Size" context-menu option and improved file-execution checks.

### 2026-04-16 — Breadcrumb navigation
- Added breadcrumb navigation for the path bars and enhanced keyboard interactions.

### 2026-04-15 — Cross-platform config & publishing
- Centralized configuration/asset loading (`FindConfigurationFile`, `FindAssetsDirectory`) for cross-platform support.
- Added cross-platform publish scripts for single-file executables and documented their usage.
- Adjusted UI margins, heights, and padding for consistent spacing.

### 2026-04-13 — Cross-platform execution
- Refactored file handling for cross-platform execution and added human-readable file-size formatting.
- Expanded the README with setup, build, and deployment instructions.

